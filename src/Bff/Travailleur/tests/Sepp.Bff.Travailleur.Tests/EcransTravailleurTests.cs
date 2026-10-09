using System.Net;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using Sepp.Bff.Travailleur.Aval;
using Sepp.Bff.Travailleur.Ecrans;

using Shouldly;

namespace Sepp.Bff.Travailleur.Tests;

public sealed class EcransTravailleurTests
{
    private static readonly DateTimeOffset Maintenant = DateTimeOffset.Parse("2026-10-05T10:00:00Z");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Guid _personne = Guid.CreateVersion7();
    private readonly Guid _autre = Guid.CreateVersion7();
    private readonly Guid _affilie = Guid.CreateVersion7();
    private readonly IPlanificationApi _planification = Substitute.For<IPlanificationApi>();
    private readonly ISurveillanceMedicaleApi _surveillance = Substitute.For<ISurveillanceMedicaleApi>();
    private readonly IObligationsApi _obligations = Substitute.For<IObligationsApi>();
    private readonly IDocumentsApi _documents = Substitute.For<IDocumentsApi>();

    private EcransRendezVous RendezVous => new(_planification, new Horloge(Maintenant));

    private EcransQuestionnaires Questionnaires => new(_surveillance, _obligations);

    private EcransDocuments Documents => new(_documents);

    private RendezVousAval Rdv(Guid personne, string debut, string statut = "Planifie") =>
        new(Guid.CreateVersion7(), personne, _affilie, Guid.CreateVersion7(), DateTimeOffset.Parse(debut), DateTimeOffset.Parse(debut).AddMinutes(30),
            "EVALUATION_PERIODIQUE", statut, null);

    private static DocumentAval Doc(Guid destinataire, string code = "SANTE.EVALUATION.TRAVAILLEUR", string statut = "Publie", string type = "Personne") =>
        new(Guid.CreateVersion7(), code, "Fr", type, destinataire, "Travailleur", "PDF/A-1a", 1024, DateTimeOffset.Parse("2026-09-01T08:00:00Z"), statut,
            DateTimeOffset.Parse("2026-09-02T08:00:00Z"));

    // --- POR-11 ---

    [Fact]
    public async Task Seuls_les_rendez_vous_de_la_personne_sont_gardes_dans_l_ordre_chronologique()
    {
        var plusTard = Rdv(_personne, "2026-12-03T09:00:00Z");
        var plusTot = Rdv(_personne, "2026-11-03T09:00:00Z");
        _planification.ListerMesRendezVousAsync(Ct).Returns([plusTard, Rdv(_autre, "2026-11-01T09:00:00Z"), plusTot]);

        var liste = await RendezVous.MesRendezVousAsync(_personne, Ct);

        liste.Select(r => r.Id).ShouldBe([plusTot.Id, plusTard.Id]);
    }

    [Fact]
    public async Task Les_prochains_rendez_vous_sont_planifies_a_venir_et_limites()
    {
        _planification.ListerMesRendezVousAsync(Ct).Returns(
        [
            Rdv(_personne, "2026-09-01T09:00:00Z"),
            Rdv(_personne, "2026-10-20T09:00:00Z", "Annule"),
            Rdv(_personne, "2026-10-21T09:00:00Z"),
            Rdv(_personne, "2026-10-22T09:00:00Z"),
            Rdv(_personne, "2026-10-23T09:00:00Z"),
            Rdv(_personne, "2026-10-24T09:00:00Z"),
        ]);

        var prochains = await RendezVous.ProchainsAsync(_personne, 3, Ct);

        prochains.Select(r => r.Debut.Day).ShouldBe([21, 22, 23]);
    }

    [Fact]
    public async Task La_reservation_utilise_la_personne_du_jeton()
    {
        var creneau = Guid.CreateVersion7();
        var obligation = Guid.CreateVersion7();
        var reserve = Guid.CreateVersion7();
        ReservationCorpsAval? envoye = null;
        _planification.ReserverAsync(Arg.Do<ReservationCorpsAval>(c => envoye = c), Ct).Returns(reserve);

        var resultat = await RendezVous.ReserverAsync(_personne, new ReservationCorps(creneau, _affilie, [obligation]), Ct);

        resultat.Id.ShouldBe(reserve);
        envoye!.PersonneId.ShouldBe(_personne);
        envoye.CreneauId.ShouldBe(creneau);
        envoye.AffilieId.ShouldBe(_affilie);
        envoye.ObligationIds.ShouldBe([obligation]);
    }

    [Fact]
    public async Task Un_refus_du_service_est_propage_tel_quel()
    {
        _planification.ReserverAsync(Arg.Any<ReservationCorpsAval>(), Ct)
            .Throws(new ErreurAvalException(NomsServices.Planification, HttpStatusCode.Conflict, "creneau.indisponible", "Indisponible."));

        var erreur = await Should.ThrowAsync<ErreurAvalException>(() =>
            RendezVous.ReserverAsync(_personne, new ReservationCorps(Guid.CreateVersion7(), _affilie, null), Ct));

        erreur.Status.ShouldBe(HttpStatusCode.Conflict);
        erreur.Code.ShouldBe("creneau.indisponible");
    }

    // --- POR-12 ---

    [Fact]
    public async Task Le_questionnaire_est_ecrit_pour_la_personne_du_jeton_et_les_reponses_sont_relayees()
    {
        var id = Guid.CreateVersion7();
        PreRemplissageCorpsAval? envoye = null;
        _surveillance.PreRemplirQuestionnaireAsync(Arg.Do<PreRemplissageCorpsAval>(c => envoye = c), Ct).Returns(id);

        var accuse = await Questionnaires.RemplirAsync(_personne, "SANTE-GENERAL", new QuestionnaireReponsesCorps([new ReponseSaisie("Q1", "Non")]), Ct);

        accuse.Id.ShouldBe(id);
        envoye!.PersonneId.ShouldBe(_personne);
        envoye.ModeleCode.ShouldBe("SANTE-GENERAL");
        envoye.Reponses.ShouldBe([new ReponseAval("Q1", "Non")]);
    }

    [Fact]
    public async Task Les_modeles_sont_projetes_sans_reponse()
    {
        _surveillance.ListerModelesQuestionnaireAsync("fr", Ct).Returns(
        [
            new ModeleQuestionnaireAval(Guid.CreateVersion7(), "SANTE-GENERAL", 2, "Questionnaire de santé",
                [new QuestionModeleAval("Q1", "Fumez-vous ?", "OuiNon", true, [])]),
        ]);

        var modeles = await Questionnaires.ModelesAsync("fr", Ct);

        modeles.ShouldHaveSingleItem().Questions.ShouldHaveSingleItem().Obligatoire.ShouldBeTrue();
        modeles[0].Version.ShouldBe(2);
    }

    [Theory]
    [InlineData("CONSULTATION_SPONTANEE")]
    [InlineData(" visite_pre_reprise ")]
    public async Task Une_demande_offerte_au_travailleur_est_relayee_avec_la_personne_du_jeton(string type)
    {
        var id = Guid.CreateVersion7();
        _obligations.EnregistrerDemandeAsync(Arg.Any<DemandeCorpsAval>(), Ct).Returns(id);

        var demande = await Questionnaires.DemanderAsync(_personne, new DemandeCorps(_affilie, type), Ct);

        demande.Id.ShouldBe(id);
        await _obligations.Received(1).EnregistrerDemandeAsync(new DemandeCorpsAval(_personne, _affilie, type.Trim().ToUpperInvariant()), Ct);
    }

    [Theory]
    [InlineData("EVALUATION_PERIODIQUE")]
    [InlineData("EXAMEN_REPRISE")]
    [InlineData("")]
    public async Task Un_autre_type_de_demande_est_refuse_sans_appel(string type)
    {
        var erreur = await Should.ThrowAsync<ErreurAvalException>(() => Questionnaires.DemanderAsync(_personne, new DemandeCorps(_affilie, type), Ct));

        erreur.Status.ShouldBe(HttpStatusCode.BadRequest);
        erreur.Code.ShouldBe("demande.type-invalide");
        await _obligations.DidNotReceiveWithAnyArgs().EnregistrerDemandeAsync(default!, Ct);
    }

    // --- POR-13 ---

    [Fact]
    public async Task Seuls_les_documents_publies_pour_la_personne_sont_listes_les_plus_recents_d_abord()
    {
        var recent = Doc(_personne) with { Date = DateTimeOffset.Parse("2026-10-01T08:00:00Z"), PublieLe = null };
        var ancien = Doc(_personne, "AUTRE.COURRIER");
        _documents.ListerDocumentsDeLaPersonneAsync(_personne, Ct).Returns(
        [
            ancien,
            Doc(_personne, statut: "Archive"),
            Doc(_autre),
            Doc(_affilie, type: "Affilie"),
            recent,
        ]);

        var liste = await Documents.MesDocumentsAsync(_personne, Ct);

        liste.Select(d => d.Id).ShouldBe([recent.Id, ancien.Id]);
        liste[0].Categorie.ShouldBe(EcransDocuments.CategorieEvaluationSante);
        liste[1].Categorie.ShouldBe(EcransDocuments.CategorieAutre);
    }

    [Fact]
    public async Task Le_contenu_d_un_document_d_un_autre_destinataire_n_est_jamais_lu()
    {
        var etranger = Doc(_autre);
        _documents.ObtenirDocumentAsync(etranger.Id, Ct).Returns(etranger);

        var erreur = await Should.ThrowAsync<ErreurAvalException>(() => Documents.ContenuAsync(_personne, etranger.Id, Ct));

        erreur.Status.ShouldBe(HttpStatusCode.NotFound);
        await _documents.DidNotReceiveWithAnyArgs().LireContenuAsync(default, Ct);
    }

    [Fact]
    public async Task Le_contenu_prend_le_nom_du_service_ou_un_nom_par_defaut()
    {
        var document = Doc(_personne);
        _documents.ObtenirDocumentAsync(document.Id, Ct).Returns(document);
        _documents.LireContenuAsync(document.Id, Ct).Returns(([1, 2], (string?)null));

        var (contenu, nom) = await Documents.ContenuAsync(_personne, document.Id, Ct);

        contenu.ShouldBe([1, 2]);
        nom.ShouldBe($"document-{document.Id}.pdf");
    }

    // --- Accueil composite ---

    [Fact]
    public async Task L_accueil_signale_les_sections_dont_le_service_est_indisponible()
    {
        _planification.ListerMesRendezVousAsync(Ct).Throws(new ServiceIndisponibleException(NomsServices.Planification));
        _documents.ListerDocumentsDeLaPersonneAsync(_personne, Ct).Returns([Doc(_personne)]);
        _surveillance.ListerModelesQuestionnaireAsync("fr", Ct).Returns(
        [
            new ModeleQuestionnaireAval(Guid.CreateVersion7(), "A", 1, "A", []),
            new ModeleQuestionnaireAval(Guid.CreateVersion7(), "B", 1, "B", []),
        ]);
        var accueil = new EcransAccueil(RendezVous, Documents, Questionnaires);

        var ecran = await accueil.AccueilAsync(_personne, "fr", Ct);

        ecran.ProchainsRendezVous.ShouldBeNull();
        ecran.DocumentsRecents.ShouldNotBeNull().Count.ShouldBe(1);
        ecran.QuestionnairesDisponibles.ShouldBe(2);
        ecran.Indisponibles.ShouldBe(["rendezVous"]);
    }

    private sealed class Horloge(DateTimeOffset maintenant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => maintenant;
    }
}
