using System.Text.Json;

using Sepp.Contracts.Examens;
using Sepp.Sagas.Tests.Plateforme;

using Shouldly;

namespace Sepp.Sagas.Tests;

/// <summary>
/// Saga « examen de reprise du travail » de bout en bout (ARC-33, POR-04, SAN-02, SAN-10, SAN-13, PLA-06, PLA-07) : cinq
/// hôtes réels dans le même processus, un seul PostgreSQL, un bus de test routé comme Terraform, une horloge simulée.
/// Chaque scénario dispose de sa fenêtre de temps (aucun créneau partagé) et de ses propres identifiants.
/// </summary>
public sealed class ScenariosSagaTests(PlateformeSaga plateforme) : IClassFixture<PlateformeSaga>
{
    private const string ObservationSecrete = "Anamnese-secrete-Zorglub depuis 2019";
    private const string JustificationSecrete = "Justification-secrete-Machinchose L5-S1";
    private const string RecommandationsSecretes = "Recommandations-secretes-Kinesitruc";

    private static readonly string[] Secrets = ["Zorglub", "Machinchose", "Kinesitruc"];

    private readonly OutilsScenario _o = new(plateforme);

    /// <summary>Nominal complet jusqu'à la décision signée ; renvoie les identifiants et l'état final observé.</summary>
    private async Task<(Travailleur Travailleur, Guid Reprise, Guid Examen, Guid Decision, EtatFinal Etat, int Livraisons)> NominalAsync(int fenetre, bool chaos, bool ordreInverse)
    {
        var t = _o.Demarrer(fenetre);
        var livraisonsAvant = plateforme.Bus.Livraisons;
        plateforme.Bus.Chaos = chaos;
        await _o.OuvrirCreneauAsync(t.JourCreneau);

        // 1-2. Annonce, obligation, créneau d'urgence, rendez-vous.
        var reprise = await _o.AnnoncerAsync(t);
        await _o.PomperAsync();
        var apresPlanification = await _o.RepriseAsync(reprise);
        apresPlanification.GetProperty("statut").GetString().ShouldBe("Planifiee");
        var rendezVous = (await _o.RendezVousDeAsync(t.Personne)).EnumerateArray().Single();
        rendezVous.GetProperty("urgent").GetBoolean().ShouldBeTrue();
        var rendezVousId = rendezVous.GetProperty("id").GetGuid();

        // 3. La convocation part (Communications), Planification la constate, Obligations passe l'obligation à « convoqué ».
        await _o.ExpedierEtPomperAsync();
        (await _o.RepriseAsync(reprise)).GetProperty("statut").GetString().ShouldBe("Convoquee");
        (await _o.ObligationAsync(t.Personne)).GetProperty("statut").GetString().ShouldBe("Convoque");

        // 4. Le jour du rendez-vous : examen clôturé, décision signée.
        plateforme.Horloge.Faux.Advance(TimeSpan.FromDays(2));
        var (_, examen) = await _o.RealiserExamenAsync(t, rendezVousId, ObservationSecrete);
        if (ordreInverse)
        {
            // La décision arrive à Obligations avant la clôture de l'examen.
            plateforme.Bus.Retenus.Add("surveillance-medicale.examen-cloture.v1");
        }

        var decision = await _o.SignerDecisionAsync(examen, JustificationSecrete, RecommandationsSecretes);
        await _o.PomperAsync();
        if (ordreInverse)
        {
            // Décision reçue avant l'examen : parquée, la saga n'est pas terminée.
            (await _o.RepriseAsync(reprise)).GetProperty("statut").GetString().ShouldNotBe("Terminee");
            plateforme.Bus.Relacher();
            await _o.PomperAsync();
        }

        // 5. Formulaire en trois exemplaires, notifications envoyées.
        await _o.ExpedierEtPomperAsync();

        var finale = await _o.RepriseAsync(reprise);
        var documents = await _o.DocumentsDeDecisionAsync(decision);
        var messages = await _o.MessagesAsync(t.Personne, t.Affilie);
        var etat = new EtatFinal(
            finale.GetProperty("statut").GetString()!,
            (await _o.ObligationAsync(t.Personne)).GetProperty("statut").GetString()!,
            finale.GetProperty("horsDelai").GetBoolean(),
            documents.GetArrayLength(),
            _o.Publies("documents.document-publie.v1").Count(m => m.Charge.Contains(decision.ToString(), StringComparison.OrdinalIgnoreCase)),
            string.Join(", ", messages
                .Select(m => $"{m.GetProperty("type").GetString()}/{m.GetProperty("statut").GetString()}")
                .Order(StringComparer.Ordinal)),
            (await _o.RendezVousAsync(rendezVousId)).GetProperty("statut").GetString()!);
        return (t, reprise, examen, decision, etat, plateforme.Bus.Livraisons - livraisonsAvant);
    }

    [Fact]
    public async Task Scenario_1_nominal_de_l_annonce_a_la_notification()
    {
        var (t, reprise, examen, decision, etat, _) = await NominalAsync(0, chaos: false, ordreInverse: false);

        // La saga est terminée : examen réalisé et décision émise, document de l'employeur rattaché.
        var vue = await _o.RepriseAsync(reprise);
        vue.GetProperty("statut").GetString().ShouldBe("Terminee");
        vue.GetProperty("horsDelai").GetBoolean().ShouldBeFalse();
        vue.GetProperty("examenId").GetGuid().ShouldBe(examen);
        vue.GetProperty("decisionId").GetGuid().ShouldBe(decision);
        vue.GetProperty("documentEmployeurId").ValueKind.ShouldNotBe(JsonValueKind.Null);
        (await _o.ObligationAsync(t.Personne)).GetProperty("statut").GetString().ShouldBe("Realise");

        // Trois exemplaires du formulaire, dont deux publiés pour envoi ; notifications partie.
        etat.Documents.ShouldBe(3);
        etat.DocumentsPublies.ShouldBe(2);
        var messages = etat.Messages.Split(", ");
        messages.ShouldContain("ConvocationRendezVous/Envoye");
        messages.Count(m => m == "NotificationDocument/Envoye").ShouldBe(2);

        // Chaîne d'événements attendue, dans l'ordre.
        var sujets = plateforme.Bus.Publies.Select(m => m.Sujet).ToList();
        foreach (var (avant, apres) in new[]
                 {
                     ("obligations.reprise-enregistree.v1", "obligations.obligation-creee.v1"),
                     ("obligations.obligation-creee.v1", "planification.rendez-vous-planifie.v1"),
                     ("planification.convocation-emise.v1", "communications.message-envoye.v1"),
                     ("communications.message-envoye.v1", "planification.convocation-envoyee.v1"),
                     ("surveillance-medicale.examen-cloture.v1", "surveillance-medicale.decision-emise.v1"),
                     ("surveillance-medicale.decision-emise.v1", "documents.document-publie.v1"),
                 })
        {
            sujets.IndexOf(avant).ShouldBeGreaterThanOrEqualTo(0, avant);
            sujets.IndexOf(avant).ShouldBeLessThan(sujets.LastIndexOf(apres), $"{avant} doit précéder {apres}");
        }

        _o.VerifierCharges(Secrets);
    }

    [Fact]
    public async Task Scenario_2_une_absence_de_moins_de_quatre_semaines_ne_requiert_aucun_examen()
    {
        var t = _o.Demarrer(1, semainesAbsence: 2);
        await _o.OuvrirCreneauAsync(t.JourCreneau);

        var reprise = await _o.AnnoncerAsync(t);
        await _o.PomperAsync();

        (await _o.RepriseAsync(reprise)).GetProperty("statut").GetString().ShouldBe("ExamenNonRequis");

        // La reprise est enregistrée puis constatée « non requise » : aucune obligation n'est ouverte.
        _o.PubliesPour("obligations.reprise-enregistree.v1", reprise)
            .Select(m => JsonDocument.Parse(m.Charge).RootElement.GetProperty("statut").GetString())
            .ShouldBe(["Enregistree", "NonRequise"]);
        _o.PubliesPour("obligations.obligation-creee.v1", t.Personne).ShouldBeEmpty();
        (await _o.RendezVousDeAsync(t.Personne)).GetArrayLength().ShouldBe(0);
        _o.VerifierCharges();
    }

    [Fact]
    public async Task Scenario_3_l_annulation_apres_planification_libere_le_rendez_vous_et_previent_le_travailleur()
    {
        var t = _o.Demarrer(2);
        await _o.OuvrirCreneauAsync(t.JourCreneau);
        var reprise = await _o.AnnoncerAsync(t);
        await _o.PomperAsync();
        await _o.ExpedierEtPomperAsync();
        var rendezVousId = (await _o.RendezVousDeAsync(t.Personne)).EnumerateArray().Single().GetProperty("id").GetGuid();
        (await _o.RepriseAsync(reprise)).GetProperty("statut").GetString().ShouldBe("Convoquee");

        await _o.AnnulerRepriseAsync(reprise);
        await _o.PomperAsync();
        await _o.ExpedierEtPomperAsync();

        (await _o.RepriseAsync(reprise)).GetProperty("statut").GetString().ShouldBe("Annulee");
        (await _o.ObligationAsync(t.Personne)).GetProperty("statut").GetString().ShouldBe("Annule");

        // Obligations publie la clôture, Planification annule le rendez-vous pour « ObligationLevee ».
        _o.PubliesPour("obligations.obligation-cloturee.v1", t.Personne).ShouldHaveSingleItem().Charge.ShouldContain("Annule");
        var annulation = _o.PubliesPour("planification.rendez-vous-annule.v1", rendezVousId).ShouldHaveSingleItem();
        annulation.Charge.ShouldContain("ObligationLevee");
        (await _o.RendezVousAsync(rendezVousId)).GetProperty("statut").GetString().ShouldBe("Annule");

        // Communications prévient le travailleur : le message d'annulation part.
        var messages = await _o.MessagesAsync(t.Personne);
        messages.ShouldContain(m => m.GetProperty("type").GetString() == "AnnulationRendezVous" && m.GetProperty("statut").GetString() == "Envoye");
        _o.VerifierCharges();
    }

    [Fact]
    public async Task Scenario_4_l_absence_au_rendez_vous_met_l_obligation_a_absent_avec_une_alerte()
    {
        var t = _o.Demarrer(3);
        await _o.OuvrirCreneauAsync(t.JourCreneau);
        var reprise = await _o.AnnoncerAsync(t);
        await _o.PomperAsync();
        await _o.ExpedierEtPomperAsync();
        var rendezVousId = (await _o.RendezVousDeAsync(t.Personne)).EnumerateArray().Single().GetProperty("id").GetGuid();

        // Le travailleur ne vient pas : constat le lendemain du rendez-vous.
        plateforme.Horloge.Faux.Advance(TimeSpan.FromDays(3));
        await _o.ConstaterAbsenceAsync(rendezVousId);
        await _o.PomperAsync();

        (await _o.ObligationAsync(t.Personne)).GetProperty("statut").GetString().ShouldBe("Absent");
        var vue = await _o.RepriseAsync(reprise);
        vue.GetProperty("nombreAbsences").GetInt32().ShouldBe(1);
        vue.GetProperty("alertes").GetArrayLength().ShouldBeGreaterThan(0);
        _o.PubliesPour("planification.absence-rendez-vous-constatee.v1", rendezVousId).ShouldHaveSingleItem();
        _o.VerifierCharges();
    }

    [Fact]
    public async Task Scenario_5_la_date_limite_depassee_marque_hors_delai_et_n_echoit_qu_une_fois()
    {
        var t = _o.Demarrer(4);
        await _o.OuvrirCreneauAsync(t.JourCreneau);
        var reprise = await _o.AnnoncerAsync(t);
        await _o.PomperAsync();
        var obligation = (await _o.ObligationAsync(t.Personne)).GetProperty("id").GetGuid();
        var limite = DateOnly.Parse((await _o.RepriseAsync(reprise)).GetProperty("dateLimite").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

        // Aucun examen : le lendemain de la date limite, la minuterie marque « hors délai » ; le traitement quotidien
        // signale l'échéance, une seule fois même s'il tourne plusieurs fois.
        plateforme.Horloge.Faux.Advance(TimeSpan.FromDays(limite.DayNumber - _o.Aujourdhui().DayNumber + 2));
        await _o.TraiterEcheancesAsync();
        await _o.PomperAsync();
        await _o.TraiterEcheancesAsync();
        await _o.TraiterEcheancesAsync();
        await _o.PomperAsync();

        var vue = await _o.RepriseAsync(reprise);
        vue.GetProperty("horsDelai").GetBoolean().ShouldBeTrue();
        vue.GetProperty("enRetard").GetBoolean().ShouldBeTrue();
        _o.PubliesPour("obligations.obligation-echue.v1", obligation).Count.ShouldBe(1);
        _o.VerifierCharges();
    }

    [Fact]
    public async Task Scenario_6_l_ordre_inverse_et_les_doublons_donnent_le_meme_etat_final()
    {
        var reference = await NominalAsync(5, chaos: false, ordreInverse: false);
        var inverse = await NominalAsync(6, chaos: false, ordreInverse: true);
        var chaos = await NominalAsync(7, chaos: true, ordreInverse: true);

        inverse.Etat.ShouldBe(reference.Etat);
        chaos.Etat.ShouldBe(reference.Etat);
        reference.Etat.StatutReprise.ShouldBe("Terminee");

        // Le mode chaos a bien livré chaque message deux fois.
        chaos.Livraisons.ShouldBeGreaterThanOrEqualTo(reference.Livraisons * 3 / 2);

        // Les doublons n'ont créé ni second rendez-vous, ni second dossier de documents.
        foreach (var run in new[] { inverse, chaos })
        {
            (await _o.RendezVousDeAsync(run.Travailleur.Personne)).GetArrayLength().ShouldBe(1);
            (await _o.DocumentsDeDecisionAsync(run.Decision)).GetArrayLength().ShouldBe(3);
        }

        _o.VerifierCharges(Secrets);
    }

    [Fact]
    public async Task Scenario_7_sans_creneau_disponible_l_urgence_n_est_pas_couverte()
    {
        var t = _o.Demarrer(8);

        var reprise = await _o.AnnoncerAsync(t);
        await _o.PomperAsync();

        var vue = await _o.RepriseAsync(reprise);
        vue.GetProperty("statut").GetString().ShouldBe("NonCouverte");
        vue.GetProperty("urgenceNonCouverte").GetBoolean().ShouldBeTrue();
        vue.GetProperty("alertes").GetArrayLength().ShouldBeGreaterThan(0);
        _o.PubliesPour("planification.urgence-non-couverte.v1", t.Personne).ShouldNotBeEmpty();
        (await _o.RendezVousDeAsync(t.Personne)).GetArrayLength().ShouldBe(0);
        _o.VerifierCharges();
    }
}
