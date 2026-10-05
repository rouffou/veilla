using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Obligations;
using Sepp.Contracts.Planification;
using Sepp.Contracts.Prevention;
using Sepp.SurveillanceMedicale.Application.Conservation;
using Sepp.SurveillanceMedicale.Application.Consultation;
using Sepp.SurveillanceMedicale.Application.Decisions;
using Sepp.SurveillanceMedicale.Application.Dossiers;
using Sepp.SurveillanceMedicale.Application.Examens;

using Shouldly;

namespace Sepp.SurveillanceMedicale.Integration.Tests;

/// <summary>
/// Secret médical de bout en bout contre un vrai PostgreSQL : matrice des droits, relation de soin, motif et bris de
/// glace, journalisation de chaque accès, chiffrement du contenu clinique au repos (SQL brut), événements et journaux
/// sans contenu clinique, décisions et recours, purge avec validation humaine, projections idempotentes.
/// </summary>
public sealed class SurveillanceMedicaleApiTests(SurveillanceMedicaleFixture fixture) : IClassFixture<SurveillanceMedicaleFixture>
{
    private const string Anamnese = "Céphalées-chroniques-Zorglub depuis 2019";
    private const string ExamenClinique = "Souffle-cardiaque-Bidulon discret";
    private const string Justification = "Hernie-discale-Machinchose L5-S1";
    private const string Recommandations = "Kinésithérapie-Truc deux fois par semaine";
    private const string ReponseQuestionnaire = "Douleurs-dorsales-Plouf";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Guid _affilie = Guid.CreateVersion7();

    private HttpClient Cpmt => fixture.Client(Roles.Cpmt, "cpmt-1");

    private static async Task<Guid> Id(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task Ok(HttpResponseMessage response, HttpStatusCode attendu = HttpStatusCode.OK) =>
        response.StatusCode.ShouldBe(attendu, await response.Content.ReadAsStringAsync());

    private async Task<(Guid DossierId, Guid ExamenId)> DossierAvecExamenAsync()
    {
        var dossierId = await Id(await Cpmt.PostAsJsonAsync("/api/v1/dossiers", new { personneId = Guid.CreateVersion7() }, _ct));
        var examenId = await Id(await Cpmt.PostAsJsonAsync("/api/v1/examens",
            new { dossierId, typeExamen = "EVALUATION_PERIODIQUE", affilieId = _affilie, date = "2026-03-02" }, _ct));
        return (dossierId, examenId);
    }

    [Fact]
    public async Task Les_sondes_repondent_et_une_requete_anonyme_est_refusee()
    {
        (await fixture.Factory.CreateClient().GetAsync("/health/ready", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await fixture.Factory.CreateClient().GetAsync($"/api/v1/dossiers/{Guid.CreateVersion7()}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(Roles.ConseillerSecurite)]
    [InlineData(Roles.GestionnaireDossiers)]
    [InlineData(Roles.Employeur)]
    public async Task Un_profil_non_medical_recoit_403_sur_tout_le_dossier_et_ne_laisse_aucune_trace(string role)
    {
        var (dossierId, examenId) = await DossierAvecExamenAsync();
        var avant = (await fixture.TracesAsync(dossierId, _ct)).Count;
        var client = fixture.Client(role, "intrus", _affilie, motif: "curiosité");

        (await client.GetAsync($"/api/v1/dossiers/{dossierId}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/v1/dossiers/{dossierId}/consultation", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/v1/dossiers/{dossierId}/export", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/v1/examens/{examenId}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync($"/api/v1/examens/{examenId}/observation", new { anamnese = "x" }, _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/v1/dossiers", new { personneId = Guid.CreateVersion7() }, _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await fixture.TracesAsync(dossierId, _ct)).Count.ShouldBe(avant);
    }

    [Fact]
    public async Task Chaque_acces_est_trace_et_hors_relation_de_soin_le_motif_est_exige()
    {
        var (dossierId, examenId) = await DossierAvecExamenAsync();
        await Ok(await Cpmt.GetAsync($"/api/v1/dossiers/{dossierId}", _ct));
        await Ok(await Cpmt.PutAsJsonAsync($"/api/v1/examens/{examenId}/observation", new { anamnese = Anamnese }, _ct), HttpStatusCode.NoContent);
        await Ok(await Cpmt.GetAsync($"/api/v1/dossiers/{dossierId}/export", _ct));

        var autre = fixture.Client(Roles.Cpmt, "cpmt-2");
        var refus = await autre.GetAsync($"/api/v1/dossiers/{dossierId}", _ct);
        refus.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refus.Content.ReadAsStringAsync(_ct)).ShouldContain("dossier-sante.motif-obligatoire");
        (await fixture.Client(Roles.Cpmt, "cpmt-2", brisDeGlace: true).GetAsync($"/api/v1/dossiers/{dossierId}", _ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await Ok(await fixture.Client(Roles.Cpmt, "cpmt-2", motif: "Avis demandé par le Dr Dupré").GetAsync($"/api/v1/dossiers/{dossierId}/consultation", _ct));
        await Ok(await fixture.Client(Roles.Infirmier, "inf-9", motif: "Remplacement urgent", brisDeGlace: true).GetAsync($"/api/v1/dossiers/{dossierId}", _ct));

        var traces = await fixture.TracesAsync(dossierId, _ct);
        traces.ShouldAllBe(t => t.Zone == "medicale");
        traces.Select(t => (t.Action, t.ObjetType, t.Motif, t.BrisDeGlace)).ShouldBe(
        [
            ("creation", "dossier-sante.dossier", null, false),
            ("creation", "dossier-sante.examen", null, false),
            ("lecture", "dossier-sante.dossier", null, false),
            ("modification", "dossier-sante.examen", null, false),
            ("export", "dossier-sante.export", null, false),
            ("lecture", "dossier-sante.consultation", "Avis demandé par le Dr Dupré", false),
            ("lecture", "dossier-sante.dossier", "Remplacement urgent", true),
        ]);
    }

    [Fact]
    public async Task Le_contenu_clinique_n_apparait_jamais_en_clair_ni_en_base_ni_dans_les_evenements_ni_dans_les_journaux()
    {
        var (dossierId, examenId) = await DossierAvecExamenAsync();
        await Ok(await Cpmt.PutAsJsonAsync($"/api/v1/examens/{examenId}/observation", new { anamnese = Anamnese, examenClinique = ExamenClinique }, _ct), HttpStatusCode.NoContent);
        var hl7 = "MSH|^~\\&|AUDIO|SEPP|||20260302||ORU^R01|1|P|2.5\rOBX|1|NM|AUDIO.PERTE_4000_OD^Perte 4 kHz||47|dB|0-25|H\rOBX|2|NM|AUDIO.PERTE_4000_OG||12|dB\r";
        var import = await Cpmt.PostAsJsonAsync($"/api/v1/examens/{examenId}/imports", new { typeActe = "Audiometrie", format = "HL7", contenu = hl7 }, _ct);
        await Ok(import);
        (await import.Content.ReadFromJsonAsync<ResultatEnregistreDto>(Json, _ct))!.ShouldSatisfyAllConditions(
            r => r.Inhabituel.ShouldBeTrue(), r => r.PropositionFrequenceId.ShouldNotBeNull());
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/dossiers/{dossierId}/questionnaires", new
        {
            modeleCode = "SANTE_GENERAL",
            reponses = new[] { new { codeQuestion = "TRAITEMENT_EN_COURS", valeur = "OUI" }, new { codeQuestion = "TABAC", valeur = "JAMAIS" }, new { codeQuestion = "PLAINTES", valeur = ReponseQuestionnaire } },
        }, _ct), HttpStatusCode.Created);
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/dossiers/{dossierId}/pieces-jointes",
            new { documentId = Guid.CreateVersion7(), categorie = "Imagerie", titre = "IRM-Zorglub du rachis", dateDocument = "2026-02-20" }, _ct), HttpStatusCode.Created);
        await Ok(await Cpmt.PostAsync($"/api/v1/examens/{examenId}/cloture", null, _ct), HttpStatusCode.NoContent);
        var decisionId = await Id(await Cpmt.PostAsJsonAsync($"/api/v1/examens/{examenId}/decision", new
        {
            categorie = "ApteAvecMesures",
            mesures = new[] { "PROTECTION_AUDITIVE" },
            valideJusquAu = "2027-03-01",
            justification = Justification,
            recommandations = Recommandations,
        }, _ct));
        await Ok(await Cpmt.PostAsync($"/api/v1/decisions/{decisionId}/signature", null, _ct));

        // Lecture déchiffrée par le personnel autorisé.
        var examen = await Cpmt.GetFromJsonAsync<ExamenDto>($"/api/v1/examens/{examenId}", Json, _ct);
        examen!.Observation!.Anamnese.ShouldBe(Anamnese);
        examen.Resultats.Single().Mesures.First(m => m.Code == "AUDIO.PERTE_4000_OD").Valeur.ShouldBe(47m);

        // SQL brut : colonnes chiffrées avec la clé de la zone médicale, aucune valeur clinique en clair, nulle part.
        ((string)(await fixture.ScalaireAsync("SELECT anamnese_chiffre FROM observation_clinique o JOIN examen e ON e.id = o.examen_id WHERE e.id = @id", _ct, ("id", examenId)))!)
            .ShouldStartWith($"v1:{SurveillanceMedicaleFixture.CleTest}:");
        ((string)(await fixture.ScalaireAsync("SELECT justification_chiffre FROM decision WHERE id = @id", _ct, ("id", decisionId)))!)
            .ShouldStartWith($"v1:{SurveillanceMedicaleFixture.CleTest}:");
        foreach (var clinique in new[] { "Zorglub", "Bidulon", "Machinchose", "Kinésithérapie-Truc", "Plouf" })
        {
            (await fixture.OccurrencesEnBaseAsync(clinique, _ct)).ShouldBe(0, $"« {clinique} » trouvé en clair en base");
            fixture.Journal.Entrees.Where(e => e.Contains(clinique, StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty($"« {clinique} » trouvé dans les journaux");
        }

        // Événements : uniquement identifiants, catégorie, mesures codées, validité (ARC-06).
        var evenements = await fixture.EvenementsAsync(_ct);
        var emise = evenements.Last(e => e.Type == "surveillance-medicale.decision-emise.v1").Payload;
        emise.ShouldContain("APTE_AVEC_MESURES");
        emise.ShouldContain("PROTECTION_AUDITIVE");
        evenements.ShouldContain(e => e.Type == "surveillance-medicale.examen-cloture.v1" && e.Payload.Contains(examenId.ToString()));
        foreach (var (_, payload) in evenements)
        {
            payload.ShouldNotContain("Hernie");
            payload.ShouldNotContain("Céphalées");
            payload.ShouldNotContain("Zorglub");
        }
    }

    [Fact]
    public async Task Decision_recours_et_lecture_par_l_employeur_de_son_seul_affilie()
    {
        var (_, examenId) = await DossierAvecExamenAsync();
        var decisionId = await Id(await Cpmt.PostAsJsonAsync($"/api/v1/examens/{examenId}/decision",
            new { categorie = "InaptitudeTemporaire", valideJusquAu = "2027-06-30", justification = Justification }, _ct));

        var employeur = fixture.Client(Roles.Employeur, "employeur-1", _affilie);
        (await employeur.GetAsync($"/api/v1/decisions/{decisionId}", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Cpmt.PostAsync($"/api/v1/decisions/{decisionId}/signature", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await Ok(await Cpmt.PostAsync($"/api/v1/examens/{examenId}/cloture", null, _ct), HttpStatusCode.NoContent);
        await Ok(await Cpmt.PostAsync($"/api/v1/decisions/{decisionId}/signature", null, _ct));

        var resume = await employeur.GetFromJsonAsync<DecisionResumeDto>($"/api/v1/decisions/{decisionId}", Json, _ct);
        resume!.Categorie.ShouldBe("INAPTITUDE_TEMPORAIRE");
        (await employeur.GetStringAsync($"/api/v1/decisions/{decisionId}/formulaire?exemplaire=Employeur", _ct)).ShouldNotContain("Machinchose");
        (await employeur.GetAsync($"/api/v1/decisions/{decisionId}/formulaire?exemplaire=Dossier", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await employeur.GetAsync($"/api/v1/decisions/{decisionId}/complete", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.Client(Roles.Employeur, "employeur-2", Guid.CreateVersion7()).GetAsync($"/api/v1/decisions/{decisionId}", _ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await fixture.Client(Roles.GestionnaireDossiers, "gest-1").GetAsync($"/api/v1/decisions/{decisionId}", _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await fixture.Client(Roles.Cpmt, "cpmt-1").GetStringAsync($"/api/v1/decisions/{decisionId}/formulaire?exemplaire=Dossier", _ct)).ShouldContain("Machinchose");

        // SAN-34 : recours auprès du médecin-inspecteur social, issue réformée → nouvelle transmission.
        var signeeLe = resume.SigneeLe!.Value;
        var recours = await Cpmt.PostAsJsonAsync($"/api/v1/decisions/{decisionId}/recours",
            new { type = "RecoursMedecinInspecteur", dateIntroduction = DateOnly.FromDateTime(signeeLe.UtcDateTime).AddDays(1).ToString("yyyy-MM-dd") }, _ct);
        recours.StatusCode.ShouldBe(HttpStatusCode.Created, await recours.Content.ReadAsStringAsync(_ct));
        var dto = (await recours.Content.ReadFromJsonAsync<RecoursDto>(Json, _ct))!;
        dto.IntroduitDansLeDelai.ShouldBeTrue();
        dto.DateLimiteIssue.ShouldBeGreaterThan(dto.DateIntroduction);

        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/decisions/{decisionId}/recours/{dto.Id}/issue", new
        {
            issue = "Reformee",
            dateIssue = dto.DateIntroduction.AddDays(20).ToString("yyyy-MM-dd"),
            reformation = new { categorie = "Apte" },
            commentaire = "Décision du médecin-inspecteur",
        }, _ct), HttpStatusCode.NoContent);
        (await employeur.GetFromJsonAsync<DecisionResumeDto>($"/api/v1/decisions/{decisionId}", Json, _ct))!.Categorie.ShouldBe("APTE");
        (await fixture.EvenementsAsync(_ct)).Count(e => e.Type == "surveillance-medicale.decision-emise.v1" && e.Payload.Contains(decisionId.ToString())).ShouldBe(2);
    }

    [Fact]
    public async Task La_purge_exige_la_validation_du_responsable_et_laisse_une_preuve_de_destruction()
    {
        var (dossierId, examenId) = await DossierAvecExamenAsync();
        await Ok(await Cpmt.PutAsJsonAsync($"/api/v1/examens/{examenId}/observation", new { anamnese = Anamnese }, _ct), HttpStatusCode.NoContent);
        await Ok(await Cpmt.PostAsync($"/api/v1/examens/{examenId}/cloture", null, _ct), HttpStatusCode.NoContent);
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/dossiers/{dossierId}/expositions", new { agent = "AMIANTE", niveau = "ELEVE", debut = "2010-01-01", fin = "2026-01-31" }, _ct), HttpStatusCode.Created);
        var archive = await Cpmt.PostAsync($"/api/v1/dossiers/{dossierId}/archivage", null, _ct);
        await Ok(archive);
        var datePurge = (await archive.Content.ReadFromJsonAsync<DossierResumeDto>(Json, _ct))!.DatePurgePrevue!.Value;
        datePurge.Year.ShouldBeGreaterThanOrEqualTo(2066); // amiante : 40 ans après la dernière activité

        (await Cpmt.PostAsync($"/api/v1/purges/propositions?date={datePurge:yyyy-MM-dd}", null, _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var dirigeant = fixture.Client(Roles.CpmtDirigeant, "dirigeant-1");
        (await dirigeant.PostAsJsonAsync($"/api/v1/purges/{dossierId}/validation", new { motif = "Conservation échue" }, _ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var proposes = await dirigeant.PostAsync($"/api/v1/purges/propositions?date={datePurge:yyyy-MM-dd}", null, _ct);
        (await proposes.Content.ReadFromJsonAsync<List<DossierResumeDto>>(Json, _ct))!.ShouldContain(d => d.Id == dossierId);
        ((long)(await fixture.ScalaireAsync("SELECT count(*) FROM examen WHERE dossier_id = @id", _ct, ("id", dossierId)))!).ShouldBe(1);

        var validation = await dirigeant.PostAsJsonAsync($"/api/v1/purges/{dossierId}/validation", new { motif = "Conservation légale échue" }, _ct);
        await Ok(validation);
        var preuve = (await validation.Content.ReadFromJsonAsync<PreuveDestructionDto>(Json, _ct))!;
        preuve.ShouldSatisfyAllConditions(p => p.ValideePar.ShouldBe("dirigeant-1"), p => p.NombreElements.ShouldBe(3), p => p.Empreinte.Length.ShouldBe(64));

        foreach (var table in new[] { "dossier_sante", "examen", "observation_clinique", "exposition" })
        {
            var colonne = table switch { "dossier_sante" => "id", "observation_clinique" => "examen_id", _ => "dossier_id" };
            var cle = table == "observation_clinique" ? examenId : dossierId;
            ((long)(await fixture.ScalaireAsync($"SELECT count(*) FROM {table} WHERE {colonne} = @id", _ct, ("id", cle)))!).ShouldBe(0, table);
        }

        (await dirigeant.GetFromJsonAsync<List<PreuveDestructionDto>>("/api/v1/purges/preuves", Json, _ct))!.ShouldContain(p => p.DossierId == dossierId);
        (await fixture.TracesAsync(dossierId, _ct)).Last().ShouldSatisfyAllConditions(t => t.Action.ShouldBe("suppression"), t => t.Motif.ShouldBe("Conservation légale échue"));
        (await Cpmt.GetAsync($"/api/v1/dossiers/{dossierId}", _ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Les_projections_sont_idempotentes_et_le_rendez_vous_etablit_la_relation_de_soin()
    {
        var personne = Guid.CreateVersion7();
        var dossierId = await Id(await Cpmt.PostAsJsonAsync("/api/v1/dossiers", new { personneId = personne }, _ct));
        var obligation = new ObligationCreee(Guid.CreateVersion7(), personne, _affilie, "EVALUATION_PERIODIQUE", new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 1));
        var rdv = new RendezVousPlanifie(Guid.CreateVersion7(), personne, _affilie, new DateTimeOffset(2026, 3, 10, 8, 0, 0, TimeSpan.Zero), [obligation.ObligationId]);
        var messageObligation = Guid.CreateVersion7();
        (await fixture.DispatcherAsync(messageObligation, obligation, _ct)).ShouldBe(1);
        (await fixture.DispatcherAsync(messageObligation, obligation, _ct)).ShouldBe(0);
        (await fixture.DispatcherAsync(Guid.CreateVersion7(), rdv, _ct)).ShouldBe(1);

        var groupe = Guid.CreateVersion7();
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/dossiers/{dossierId}/groupes-exposition", new { groupeExpositionId = groupe, affilieId = _affilie, valideDu = "2026-01-01" }, _ct), HttpStatusCode.Created);
        var mesurage = new MesurageEnregistre(Guid.CreateVersion7(), groupe, _affilie, "BRUIT", "ELEVE", new DateOnly(2026, 2, 15));
        (await fixture.DispatcherAsync(Guid.CreateVersion7(), mesurage, _ct)).ShouldBe(1);
        (await fixture.DispatcherAsync(Guid.CreateVersion7(), mesurage, _ct)).ShouldBe(1);

        var infirmier = fixture.Client(Roles.Infirmier, "inf-1");
        (await infirmier.GetAsync($"/api/v1/dossiers/{dossierId}/consultation", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await Id(await infirmier.PostAsJsonAsync("/api/v1/examens", new { dossierId, rendezVousId = rdv.RendezVousId }, _ct));

        var vue = await infirmier.GetFromJsonAsync<VueConsultationDto>($"/api/v1/dossiers/{dossierId}/consultation?date=2026-03-10", Json, _ct);
        vue!.ExamensDus.Single().ObligationId.ShouldBe(obligation.ObligationId);
        vue.Expositions.Single().Agent.ShouldBe("BRUIT");
        vue.Historique.Single().ProfessionnelId.ShouldBe("inf-1");
    }

    [Fact]
    public async Task Le_questionnaire_pre_rempli_sur_tablette_ne_donne_aucun_acces_en_lecture()
    {
        var personne = Guid.CreateVersion7();
        var assistant = fixture.Client(Roles.AssistantMedical, "assistant-1");
        await Id(await assistant.PostAsJsonAsync("/api/v1/questionnaires/pre-remplissage", new
        {
            personneId = personne,
            modeleCode = "SANTE_GENERAL",
            reponses = new[] { new { codeQuestion = "TRAITEMENT_EN_COURS", valeur = "NON" }, new { codeQuestion = "TABAC", valeur = "ACTUEL" } },
        }, _ct));
        (await assistant.GetAsync($"/api/v1/dossiers?personneId={personne}", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var dossier = await Cpmt.GetFromJsonAsync<DossierResumeDto>($"/api/v1/dossiers?personneId={personne}", Json, _ct);
        var prerempli = await fixture.Client(Roles.Cpmt, "cpmt-1", motif: "Préparation de la consultation")
            .GetFromJsonAsync<QuestionnairePreRempliDto>($"/api/v1/dossiers/{dossier!.Id}/questionnaires/SANTE_GENERAL/pre-rempli", Json, _ct);
        prerempli!.DernieresReponses!.Reponses.ShouldContain(r => r.CodeQuestion == "TABAC" && r.Valeur == "ACTUEL");
        prerempli.DernieresReponses.Source.ToString().ShouldBe("Tablette");
    }

    [Fact]
    public async Task Vaccination_stock_declaration_fedris_et_transfert_de_dossier()
    {
        var (dossierId, _) = await DossierAvecExamenAsync();
        var centre = Guid.CreateVersion7();
        var lotId = await Id(await Cpmt.PostAsJsonAsync("/api/v1/lots-vaccins",
            new { centreId = centre, vaccinCode = "HEPATITE_B", numeroLot = "HB-2026-01", peremption = "2027-12-31", quantite = 10 }, _ct));
        (await fixture.Client(Roles.GestionnaireDossiers, "gest-1").GetAsync($"/api/v1/centres/{centre}/stock-vaccins", _ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/dossiers/{dossierId}/vaccinations", new { vaccinCode = "HEPATITE_B", dose = 1, lotId, remarque = "Réaction-Zorglub locale" }, _ct), HttpStatusCode.Created);
        (await Cpmt.GetStringAsync($"/api/v1/centres/{centre}/stock-vaccins", _ct)).ShouldContain("\"quantite\":9");
        (await fixture.EvenementsAsync(_ct)).ShouldContain(e => e.Type == "surveillance-medicale.vaccination-administree.v1" && e.Payload.Contains("HEPATITE_B"));
        await Ok(await Cpmt.GetAsync($"/api/v1/dossiers/{dossierId}/vaccinations/rappels", _ct));

        // SAN-70, SAN-71 : déclaration préremplie, envoi à Fedris (simulateur), demande d'information et réponse.
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/dossiers/{dossierId}/expositions", new { agent = "BRUIT", niveau = "ELEVE", debut = "2020-01-01" }, _ct), HttpStatusCode.Created);
        var declaration = await Cpmt.PostAsJsonAsync($"/api/v1/dossiers/{dossierId}/declarations-mp",
            new { codeMaladie = "1.605.03", description = "Surdité-Zorglub bilatérale", agentCausal = "BRUIT" }, _ct);
        declaration.StatusCode.ShouldBe(HttpStatusCode.Created, await declaration.Content.ReadAsStringAsync(_ct));
        var declarationId = (await declaration.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("id").GetGuid();
        (await declaration.Content.ReadAsStringAsync(_ct)).ShouldContain("BRUIT");
        var envoi = await Cpmt.PostAsync($"/api/v1/declarations-mp/{declarationId}/envoi", null, _ct);
        await Ok(envoi);
        (await envoi.Content.ReadAsStringAsync(_ct)).ShouldContain("SIM-FEDRIS-");
        var demandeId = await Id(await Cpmt.PostAsJsonAsync($"/api/v1/declarations-mp/{declarationId}/demandes-information",
            new { dateDemande = "2026-10-01", echeance = "2026-10-31", objet = "Audiogrammes antérieurs" }, _ct));
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/declarations-mp/{declarationId}/demandes-information/{demandeId}/reponse",
            new { date = "2026-10-05", reponse = "Audiogrammes 2022-2025 joints" }, _ct), HttpStatusCode.NoContent);
        await Ok(await Cpmt.PostAsync($"/api/v1/declarations-mp/{declarationId}/suivi", null, _ct));

        // SAN-42 : transfert sortant (paquet chiffré, empreinte), puis réception contrôlée par empreinte.
        var transfertId = await Id(await Cpmt.PostAsJsonAsync($"/api/v1/dossiers/{dossierId}/transferts", new { contrepartie = "0123456789", motif = "ChangementEmployeur" }, _ct));
        var transmis = await Cpmt.PostAsync($"/api/v1/transferts/{transfertId}/transmission", null, _ct);
        await Ok(transmis);
        var empreinte = (await transmis.Content.ReadFromJsonAsync<JsonElement>(_ct)).GetProperty("empreinte").GetString()!;
        ((string)(await fixture.ScalaireAsync("SELECT paquet_chiffre FROM transfert_dossier WHERE id = @id", _ct, ("id", transfertId)))!)
            .ShouldStartWith($"v1:{SurveillanceMedicaleFixture.CleTest}:");

        const string paquet = "{\"dossier\":\"recu\"}";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(paquet)));
        (await Cpmt.PostAsJsonAsync("/api/v1/transferts/entrants",
            new { personneId = Guid.CreateVersion7(), contrepartie = "9876543210", motif = "ChangementEmployeur", paquet, empreinte, referenceCanal = "R1" }, _ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var entrant = await Id(await Cpmt.PostAsJsonAsync("/api/v1/transferts/entrants",
            new { personneId = Guid.CreateVersion7(), contrepartie = "9876543210", motif = "ChangementEmployeur", paquet, empreinte = hash, referenceCanal = "R1" }, _ct));
        await Ok(await fixture.Client(Roles.Cpmt, "cpmt-1", motif: "Intégration du dossier reçu").PostAsync($"/api/v1/transferts/{entrant}/integration", null, _ct));

        (await fixture.OccurrencesEnBaseAsync("Zorglub", _ct)).ShouldBe(0);
        (await fixture.OccurrencesEnBaseAsync("Audiogrammes", _ct)).ShouldBe(0);
    }
}
