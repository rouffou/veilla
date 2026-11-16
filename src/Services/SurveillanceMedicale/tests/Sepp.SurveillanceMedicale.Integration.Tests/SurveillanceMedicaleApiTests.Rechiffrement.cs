using System.Globalization;
using System.Net;
using System.Net.Http.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.SurveillanceMedicale.Adapters.Persistence;
using Sepp.SurveillanceMedicale.Application.Decisions;

using Shouldly;

namespace Sepp.SurveillanceMedicale.Integration.Tests;

/// <summary>
/// Rechiffrement des champs (#396, ADR 0012) sur la base réelle du service (contenu clinique, zone médicale) : toutes les colonnes chiffrées du
/// modèle sont couvertes (garde-fou : une colonne ajoutée ou renommée doit apparaître ici), et une base dont toutes les valeurs
/// utilisent déjà la DEK courante n'est pas réécrite.
/// </summary>
public sealed partial class SurveillanceMedicaleApiTests
{
    /// <summary>
    /// Garde-fou d'architecture et de données (#482) : tout convertisseur qui chiffre porte le marqueur et le suffixe
    /// (sinon <see cref="ColonnesChiffrees.Depuis"/> échoue), et la vérification fondée sur les données ne trouve aucune
    /// valeur chiffrée dans une colonne que le modèle ne déclare pas chiffrée.
    /// </summary>
    [Fact]
    public async Task Les_convertisseurs_de_chiffrement_sont_marques_et_aucune_valeur_chiffree_n_est_hors_du_modele()
    {
        // #502, P3-C : au moins une ligne par table chiffrée, pour que la vérification fondée sur les données porte sur
        // des valeurs réelles.
        await EcrireUneLigneParTableChiffreeAsync();

        IReadOnlyList<ColonneChiffree> colonnes;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            colonnes = ColonnesChiffrees.Depuis(scope.ServiceProvider.GetRequiredService<SurveillanceMedicaleDbContext>().Model);
        }

        var verification = await RechiffrementHote.VerifierAsync<SurveillanceMedicaleDbContext>(
            fixture.Factory.Services, s => s.GetRequiredService<ChiffrementZoneMedicale>().Encryptor, new OptionsRechiffrement(), NullLogger.Instance, _ct);

        colonnes.ShouldNotBeEmpty();
        verification.ColonnesHorsModele.ShouldBeEmpty();
        verification.ColonnesAbsentes.ShouldBeEmpty();
        verification.Colonnes.Where(c => c.DuModele).Select(c => c.Colonne).ShouldBe(colonnes.Select(c => c.Nom));
        verification.Colonnes.Where(c => c.DuModele).GroupBy(c => c.Colonne[..c.Colonne.LastIndexOf('.')])
            .Where(t => t.Sum(c => c.Chiffrees) == 0).Select(t => t.Key).ShouldBeEmpty("table chiffrée sans aucune valeur chiffrée");
    }

    /// <summary>
    /// Une ligne, avec une valeur chiffrée, dans chaque table chiffrée du service : observation, résultat d'acte,
    /// questionnaire, pièce jointe, décision, recours, vaccination, test tuberculinique, déclaration de maladie
    /// professionnelle, demande d'information Fedris et transfert de dossier.
    /// </summary>
    private async Task EcrireUneLigneParTableChiffreeAsync()
    {
        var (dossierId, examenId) = await DossierAvecExamenAsync();
        await Ok(await Cpmt.PutAsJsonAsync($"/api/v1/examens/{examenId}/observation", new { anamnese = Anamnese, examenClinique = ExamenClinique }, _ct), HttpStatusCode.NoContent);
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/examens/{examenId}/imports",
            new { typeActe = "Audiometrie", format = "HL7", contenu = "MSH|^~\\&|AUDIO|SEPP|||20260302||ORU^R01|1|P|2.5\rOBX|1|NM|AUDIO.PERTE_4000_OD||12|dB\r" }, _ct));
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/dossiers/{dossierId}/questionnaires", new
        {
            modeleCode = "SANTE_GENERAL",
            reponses = new[] { new { codeQuestion = "TRAITEMENT_EN_COURS", valeur = "NON" }, new { codeQuestion = "TABAC", valeur = "JAMAIS" }, new { codeQuestion = "PLAINTES", valeur = ReponseQuestionnaire } },
        }, _ct), HttpStatusCode.Created);
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/dossiers/{dossierId}/pieces-jointes",
            new { documentId = Guid.CreateVersion7(), categorie = "Imagerie", titre = "Radiographie", dateDocument = "2026-02-20" }, _ct), HttpStatusCode.Created);

        // Décision signée, puis recours et son issue commentée.
        await Ok(await Cpmt.PostAsync($"/api/v1/examens/{examenId}/cloture", null, _ct), HttpStatusCode.NoContent);
        var decisionId = await Id(await Cpmt.PostAsJsonAsync($"/api/v1/examens/{examenId}/decision",
            new { categorie = "Apte", valideJusquAu = "2027-03-01", justification = Justification, recommandations = Recommandations }, _ct));
        var signee = await Cpmt.SendAsync(Signature(decisionId, 1), _ct);
        await Ok(signee);
        var signeeLe = (await signee.Content.ReadFromJsonAsync<DecisionResumeDto>(Json, _ct))!.SigneeLe!.Value;
        var recours = await Cpmt.PostAsJsonAsync($"/api/v1/decisions/{decisionId}/recours",
            new { type = "Concertation", dateIntroduction = DateOnly.FromDateTime(signeeLe.UtcDateTime).AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, _ct);
        recours.StatusCode.ShouldBe(HttpStatusCode.Created, await recours.Content.ReadAsStringAsync(_ct));
        var dto = (await recours.Content.ReadFromJsonAsync<RecoursDto>(Json, _ct))!;
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/decisions/{decisionId}/recours/{dto.Id}/issue",
            new { issue = "Confirmee", dateIssue = dto.DateIntroduction.AddDays(10).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), commentaire = "Issue confirmée" }, _ct),
            HttpStatusCode.NoContent);

        // Vaccination et test tuberculinique lu.
        var lotId = await Id(await Cpmt.PostAsJsonAsync("/api/v1/lots-vaccins",
            new { centreId = Guid.CreateVersion7(), vaccinCode = "HEPATITE_B", numeroLot = "HB-502", peremption = "2027-12-31", quantite = 2 }, _ct));
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/dossiers/{dossierId}/vaccinations", new { vaccinCode = "HEPATITE_B", dose = 1, lotId, remarque = "Aucune réaction" }, _ct), HttpStatusCode.Created);
        var testId = await Id(await Cpmt.PostAsJsonAsync($"/api/v1/dossiers/{dossierId}/tests-tuberculiniques", new { datePose = "2026-03-02" }, _ct));
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/dossiers/{dossierId}/tests-tuberculiniques/{testId}/lecture",
            new { dateLecture = "2026-03-05", resultat = "Negatif", indurationMm = 2 }, _ct), HttpStatusCode.NoContent);

        // Déclaration de maladie professionnelle envoyée, demande d'information de Fedris et sa réponse.
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/dossiers/{dossierId}/expositions", new { agent = "BRUIT", niveau = "ELEVE", debut = "2020-01-01" }, _ct), HttpStatusCode.Created);
        var declarationId = await Id(await Cpmt.PostAsJsonAsync($"/api/v1/dossiers/{dossierId}/declarations-mp",
            new { codeMaladie = "1.605.03", description = "Surdité bilatérale", agentCausal = "BRUIT" }, _ct));
        await Ok(await Cpmt.PostAsync($"/api/v1/declarations-mp/{declarationId}/envoi", null, _ct));
        var demandeId = await Id(await Cpmt.PostAsJsonAsync($"/api/v1/declarations-mp/{declarationId}/demandes-information",
            new { dateDemande = "2026-10-01", echeance = "2026-10-31", objet = "Audiogrammes antérieurs" }, _ct));
        await Ok(await Cpmt.PostAsJsonAsync($"/api/v1/declarations-mp/{declarationId}/demandes-information/{demandeId}/reponse",
            new { date = "2026-10-05", reponse = "Audiogrammes joints" }, _ct), HttpStatusCode.NoContent);

        // Transfert sortant transmis (paquet chiffré).
        var transfertId = await Id(await Cpmt.PostAsJsonAsync($"/api/v1/dossiers/{dossierId}/transferts", new { contrepartie = "0123456789", motif = "ChangementEmployeur" }, _ct));
        await Ok(await Cpmt.PostAsync($"/api/v1/transferts/{transfertId}/transmission", null, _ct));
    }

    [Fact]
    public async Task Le_rechiffrement_couvre_toutes_les_colonnes_chiffrees_et_ne_reecrit_pas_les_valeurs_a_jour()
    {
        var rapport = await RechiffrementHote.ExecuterAsync<SurveillanceMedicaleDbContext>(
            fixture.Factory.Services, s => s.GetRequiredService<ChiffrementZoneMedicale>().Encryptor, new OptionsRechiffrement { PauseEntreLots = TimeSpan.Zero }, NullLogger.Instance, _ct);

        rapport.Colonnes.Select(c => c.Colonne).ShouldBe(
        [
            "decision.justification_chiffre",
            "decision.recommandations_chiffre",
            "declaration_mp.contenu_chiffre",
            "demande_information_fedris.objet_chiffre",
            "demande_information_fedris.reponse_chiffre",
            "observation_clinique.anamnese_chiffre",
            "observation_clinique.examen_clinique_chiffre",
            "piece_jointe.description_chiffre",
            "piece_jointe.titre_chiffre",
            "questionnaire_rempli.reponses_chiffre",
            "recours.commentaire_chiffre",
            "resultat_acte.commentaire_chiffre",
            "resultat_acte.valeurs_chiffre",
            "test_tuberculinique.lecture_chiffre",
            "transfert_dossier.paquet_chiffre",
            "vaccination.remarque_chiffre",
        ]);
        rapport.Complet.ShouldBeTrue();
        rapport.Rechiffrees.ShouldBe(0);
        rapport.Colonnes.ShouldAllBe(c => c.Examinees == 0);
    }
}
