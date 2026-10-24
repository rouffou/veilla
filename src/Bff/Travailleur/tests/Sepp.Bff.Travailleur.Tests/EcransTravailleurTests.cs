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
    private readonly IPersonnesApi _personnes = Substitute.For<IPersonnesApi>();
    private readonly IAffiliesApi _affilies = Substitute.For<IAffiliesApi>();
    private readonly IObligationsApi _obligations = Substitute.For<IObligationsApi>();
    private readonly IDocumentsApi _documents = Substitute.For<IDocumentsApi>();

    private EcransRendezVous RendezVous => new(_planification, new Horloge(Maintenant));

    private EcransQuestionnaires Questionnaires => new(_surveillance, _obligations);

    private readonly IDocumentsMedicaleApi _medicale = Substitute.For<IDocumentsMedicaleApi>();

    private EcransDocuments Documents => new(_documents);

    private EcransDocuments DocumentsDeuxInstances => new(_documents, _medicale);

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

    [Fact]
    public async Task L_annulation_relaie_la_raison_codee_et_la_precision()
    {
        var id = Guid.CreateVersion7();
        AnnulationCorpsAval? envoye = null;
        await _planification.AnnulerAsync(id, Arg.Do<AnnulationCorpsAval>(c => envoye = c), Ct);

        await RendezVous.AnnulerAsync(id, new AnnulationCorps("ProblemeTransport", "Gare fermée"), Ct);
        await RendezVous.AnnulerAsync(id, null, Ct);

        envoye.ShouldNotBeNull();
        await _planification.Received(1).AnnulerAsync(id, Arg.Is<AnnulationCorpsAval>(c => c.Raison == "ProblemeTransport" && c.Commentaire == "Gare fermée"), Ct);
        await _planification.Received(1).AnnulerAsync(id, Arg.Is<AnnulationCorpsAval>(c => c.Raison == null && c.Commentaire == null), Ct);
    }

    [Fact]
    public async Task Le_deplacement_est_un_seul_appel_a_Planification_et_projette_le_rendez_vous()
    {
        var rdv = Rdv(_personne, "2026-11-03T09:00:00Z") with { NombreDeplacements = 1 };
        var creneau = Guid.CreateVersion7();
        _planification.DeplacerAsync(rdv.Id, creneau, Ct).Returns(rdv);

        var ecran = await RendezVous.DeplacerAsync(_personne, rdv.Id, new DeplacementCorps(creneau), Ct);

        ecran.Id.ShouldBe(rdv.Id);
        ecran.NombreDeplacements.ShouldBe(1);
        await _planification.Received(1).DeplacerAsync(rdv.Id, creneau, Ct);
        await _planification.DidNotReceiveWithAnyArgs().ReserverAsync(default!, Ct);
        await _planification.DidNotReceiveWithAnyArgs().AnnulerAsync(default, default!, Ct);
    }

    [Fact]
    public async Task Un_rendez_vous_d_une_autre_personne_n_est_jamais_renvoye_au_portail()
    {
        var etranger = Rdv(_autre, "2026-11-03T09:00:00Z");
        _planification.DeplacerAsync(etranger.Id, Arg.Any<Guid>(), Ct).Returns(etranger);
        _planification.ConfirmerAsync(etranger.Id, Ct).Returns(etranger);

        (await Should.ThrowAsync<ErreurAvalException>(() => RendezVous.DeplacerAsync(_personne, etranger.Id, new DeplacementCorps(Guid.CreateVersion7()), Ct)))
            .Status.ShouldBe(HttpStatusCode.NotFound);
        (await Should.ThrowAsync<ErreurAvalException>(() => RendezVous.ConfirmerAsync(_personne, etranger.Id, Ct)))
            .Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task La_confirmation_projette_la_date_de_confirmation()
    {
        var confirme = DateTimeOffset.Parse("2026-10-05T10:00:00Z");
        var rdv = Rdv(_personne, "2026-11-03T09:00:00Z") with { PresenceConfirmeeLe = confirme };
        _planification.ConfirmerAsync(rdv.Id, Ct).Returns(rdv);

        var ecran = await RendezVous.ConfirmerAsync(_personne, rdv.Id, Ct);

        ecran.PresenceConfirmeeLe.ShouldBe(confirme);
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
    public async Task Une_demande_offerte_au_travailleur_est_relayee_sans_personne_dans_le_corps(string type)
    {
        var id = Guid.CreateVersion7();
        _obligations.EnregistrerDemandeAsync(Arg.Any<MaDemandeCorpsAval>(), Ct).Returns(id);

        var demande = await Questionnaires.DemanderAsync(_personne, new DemandeCorps(_affilie, type), Ct);

        demande.Id.ShouldBe(id);
        await _obligations.Received(1).EnregistrerDemandeAsync(new MaDemandeCorpsAval(_affilie, type.Trim().ToUpperInvariant()), Ct);
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

    // --- POR-11 : employeurs et examens à planifier ---

    private EcransEmployeursEtExamens EmployeursEtExamens => new(_personnes, _affilies, _obligations);

    [Fact]
    public async Task Un_employeur_n_apparait_qu_une_fois_meme_avec_plusieurs_occupations_et_le_plus_ancien_d_abord()
    {
        var autre = Guid.CreateVersion7();
        _personnes.ListerMesOccupationsAsync(Ct).Returns(
        [
            new OccupationAval(Guid.CreateVersion7(), autre, "Salarie", new DateOnly(2026, 3, 1), null),
            new OccupationAval(Guid.CreateVersion7(), _affilie, "Salarie", new DateOnly(2025, 1, 1), null),
            new OccupationAval(Guid.CreateVersion7(), _affilie, "Salarie", new DateOnly(2026, 6, 1), null),
        ]);

        var employeurs = await EmployeursEtExamens.MesEmployeursAsync(Ct);

        employeurs.Select(e => e.AffilieId).ShouldBe([_affilie, autre]);
        employeurs[0].DateDebut.ShouldBe(new DateOnly(2025, 1, 1));
    }

    [Fact]
    public async Task Les_employeurs_sont_nommes_par_Affilies_et_une_denomination_d_un_autre_affilie_est_ignoree()
    {
        var autre = Guid.CreateVersion7();
        _personnes.ListerMesOccupationsAsync(Ct).Returns([new OccupationAval(Guid.CreateVersion7(), _affilie, "Salarie", new DateOnly(2025, 1, 1), null)]);
        _affilies.ListerMesEmployeursAsync(Ct).Returns(
        [
            new EmployeurAval(autre, "Atelier Durand", "Fr"),
            new EmployeurAval(_affilie, "Boulangerie Dupont", "Nl"),
        ]);

        var employeur = (await EmployeursEtExamens.MesEmployeursAsync(Ct)).ShouldHaveSingleItem();

        employeur.AffilieId.ShouldBe(_affilie);
        employeur.Denomination.ShouldBe("Boulangerie Dupont");
        employeur.Langue.ShouldBe("Nl");
    }

    [Fact]
    public async Task Un_employeur_inconnu_d_Affilies_reste_liste_sans_denomination()
    {
        _personnes.ListerMesOccupationsAsync(Ct).Returns([new OccupationAval(Guid.CreateVersion7(), _affilie, "Salarie", new DateOnly(2025, 1, 1), null)]);
        _affilies.ListerMesEmployeursAsync(Ct).Returns([]);

        var employeur = (await EmployeursEtExamens.MesEmployeursAsync(Ct)).ShouldHaveSingleItem();

        employeur.Denomination.ShouldBeNull();
        employeur.Langue.ShouldBeNull();
    }

    [Fact]
    public async Task Les_employeurs_restent_listes_quand_Affilies_est_indisponible_ou_refuse()
    {
        _personnes.ListerMesOccupationsAsync(Ct).Returns([new OccupationAval(Guid.CreateVersion7(), _affilie, "Salarie", new DateOnly(2025, 1, 1), null)]);
        _affilies.ListerMesEmployeursAsync(Ct).ThrowsAsync(new ServiceIndisponibleException("affilies"));
        (await EmployeursEtExamens.MesEmployeursAsync(Ct)).ShouldHaveSingleItem().Denomination.ShouldBeNull();

        _affilies.ListerMesEmployeursAsync(Ct).ThrowsAsync(new ErreurAvalException("affilies", HttpStatusCode.Forbidden, "affilie.lecture-interdite", null));
        (await EmployeursEtExamens.MesEmployeursAsync(Ct)).ShouldHaveSingleItem().Denomination.ShouldBeNull();
    }

    [Fact]
    public async Task Une_panne_de_Personnes_n_est_pas_masquee()
    {
        _personnes.ListerMesOccupationsAsync(Ct).ThrowsAsync(new ServiceIndisponibleException("personnes"));
        _affilies.ListerMesEmployeursAsync(Ct).Returns([]);

        await Should.ThrowAsync<ServiceIndisponibleException>(() => EmployeursEtExamens.MesEmployeursAsync(Ct));
    }

    [Fact]
    public async Task Les_examens_a_planifier_d_une_autre_personne_sont_ecartes_et_tries_par_echeance()
    {
        ExamenAPlanifierAval Examen(Guid personne, string limite) =>
            new(Guid.CreateVersion7(), personne, _affilie, "EVALUATION_PERIODIQUE", "Évaluation", DateOnly.Parse(limite), DateOnly.Parse(limite), "APlanifier", false);
        var tard = Examen(_personne, "2026-12-01");
        var tot = Examen(_personne, "2026-11-01");
        _obligations.ListerMesExamensAPlanifierAsync("fr", Ct).Returns([tard, Examen(_autre, "2026-10-01"), tot]);

        var examens = await EmployeursEtExamens.MesExamensAPlanifierAsync(_personne, "fr", Ct);

        examens.Select(e => e.ObligationId).ShouldBe([tot.Id, tard.Id]);
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

    // --- POR-13 : deux instances Documents (décision #356) ---

    [Fact]
    public async Task Les_listes_des_deux_instances_sont_fusionnees_et_triees_sans_zone()
    {
        var standard = Doc(_personne) with { Date = DateTimeOffset.Parse("2026-09-01T08:00:00Z") };
        var medical = Doc(_personne, "SANTE.CARNET_VACCINATION") with { Date = DateTimeOffset.Parse("2026-10-01T08:00:00Z") };
        _documents.ListerDocumentsDeLaPersonneAsync(_personne, Ct).Returns([standard, Doc(_autre)]);
        _medicale.ListerDocumentsDeLaPersonneAsync(_personne, Ct).Returns([medical]);

        var liste = await DocumentsDeuxInstances.MesDocumentsAsync(_personne, Ct);

        liste.Select(d => d.Id).ShouldBe([medical.Id, standard.Id]);
        typeof(DocumentEcran).GetProperties().Select(p => p.Name).ShouldNotContain(n => n.Contains("Zone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Sans_instance_medicale_une_seule_instance_sert_toutes_les_zones()
    {
        _documents.ListerDocumentsDeLaPersonneAsync(_personne, Ct).Returns([Doc(_personne)]);

        (await Documents.MesDocumentsAsync(_personne, Ct)).Count.ShouldBe(1);
        await _medicale.DidNotReceiveWithAnyArgs().ListerDocumentsDeLaPersonneAsync(default, Ct);
    }

    [Fact]
    public async Task Une_instance_indisponible_rend_la_liste_indisponible_et_l_accueil_ne_signale_que_les_documents()
    {
        _documents.ListerDocumentsDeLaPersonneAsync(_personne, Ct).Returns([Doc(_personne)]);
        _medicale.ListerDocumentsDeLaPersonneAsync(_personne, Ct).Throws(new ServiceIndisponibleException(NomsServices.DocumentsMedicale));
        _planification.ListerMesRendezVousAsync(Ct).Returns([]);
        _surveillance.ListerModelesQuestionnaireAsync("fr", Ct).Returns([]);

        await Should.ThrowAsync<ServiceIndisponibleException>(() => DocumentsDeuxInstances.MesDocumentsAsync(_personne, Ct));

        var ecran = await new EcransAccueil(RendezVous, DocumentsDeuxInstances, Questionnaires).AccueilAsync(_personne, "fr", Ct);

        ecran.Indisponibles.ShouldBe(["documents"]);
        ecran.ProchainsRendezVous.ShouldNotBeNull();
    }

    [Fact]
    public async Task Le_contenu_est_lu_chez_l_instance_qui_connait_le_document()
    {
        var document = Doc(_personne, "SANTE.COPIE_DOSSIER");
        _documents.ObtenirDocumentAsync(document.Id, Ct).Throws(Introuvable());
        _medicale.ObtenirDocumentAsync(document.Id, Ct).Returns(document);
        _medicale.LireContenuAsync(document.Id, Ct).Returns(([7], (string?)"copie.pdf"));

        var (contenu, nom) = await DocumentsDeuxInstances.ContenuAsync(_personne, document.Id, Ct);

        contenu.ShouldBe([7]);
        nom.ShouldBe("copie.pdf");
        await _documents.DidNotReceiveWithAnyArgs().LireContenuAsync(default, Ct);
    }

    [Fact]
    public async Task Le_contenu_est_lu_chez_l_instance_standard_quand_elle_repond_200()
    {
        var document = Doc(_personne);
        _documents.ObtenirDocumentAsync(document.Id, Ct).Returns(document);
        _medicale.ObtenirDocumentAsync(document.Id, Ct).Throws(Introuvable());
        _documents.LireContenuAsync(document.Id, Ct).Returns(([1], (string?)"a.pdf"));

        (await DocumentsDeuxInstances.ContenuAsync(_personne, document.Id, Ct)).Contenu.ShouldBe([1]);
        await _medicale.DidNotReceiveWithAnyArgs().LireContenuAsync(default, Ct);
    }

    [Fact]
    public async Task Un_document_inconnu_des_deux_instances_donne_404()
    {
        var id = Guid.CreateVersion7();
        _documents.ObtenirDocumentAsync(id, Ct).Throws(Introuvable());
        _medicale.ObtenirDocumentAsync(id, Ct).Throws(Introuvable());

        var erreur = await Should.ThrowAsync<ErreurAvalException>(() => DocumentsDeuxInstances.ContenuAsync(_personne, id, Ct));

        erreur.Status.ShouldBe(HttpStatusCode.NotFound);
        erreur.Code.ShouldBe("document.inconnu");
    }

    [Fact]
    public async Task Le_document_d_un_autre_destinataire_chez_l_instance_medicale_n_est_pas_lu()
    {
        var etranger = Doc(_autre);
        _documents.ObtenirDocumentAsync(etranger.Id, Ct).Throws(Introuvable());
        _medicale.ObtenirDocumentAsync(etranger.Id, Ct).Returns(etranger);

        var erreur = await Should.ThrowAsync<ErreurAvalException>(() => DocumentsDeuxInstances.ContenuAsync(_personne, etranger.Id, Ct));

        erreur.Status.ShouldBe(HttpStatusCode.NotFound);
        await _medicale.DidNotReceiveWithAnyArgs().LireContenuAsync(default, Ct);
    }

    [Fact]
    public async Task Une_instance_en_panne_ne_masque_pas_un_document_trouve_sur_l_autre()
    {
        var document = Doc(_personne);
        _documents.ObtenirDocumentAsync(document.Id, Ct).Throws(new ServiceIndisponibleException(NomsServices.Documents));
        _medicale.ObtenirDocumentAsync(document.Id, Ct).Returns(document);
        _medicale.LireContenuAsync(document.Id, Ct).Returns(([3], (string?)"m.pdf"));

        (await DocumentsDeuxInstances.ContenuAsync(_personne, document.Id, Ct)).Contenu.ShouldBe([3]);
    }

    [Fact]
    public async Task Un_404_et_une_instance_en_panne_ne_concluent_pas_a_un_document_inconnu()
    {
        var id = Guid.CreateVersion7();
        _documents.ObtenirDocumentAsync(id, Ct).Throws(Introuvable());
        _medicale.ObtenirDocumentAsync(id, Ct).Throws(new ServiceIndisponibleException(NomsServices.DocumentsMedicale));

        await Should.ThrowAsync<ServiceIndisponibleException>(() => DocumentsDeuxInstances.ContenuAsync(_personne, id, Ct));
    }

    private static ErreurAvalException Introuvable() =>
        new(NomsServices.Documents, HttpStatusCode.NotFound, "introuvable", null);

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
