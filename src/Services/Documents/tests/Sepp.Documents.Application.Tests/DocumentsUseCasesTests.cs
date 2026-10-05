using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Documents;
using Sepp.Documents.Application.Documents;
using Sepp.Documents.Application.Generation;
using Sepp.Documents.Application.Modeles;
using Sepp.Documents.Domain.Commun;
using Sepp.Documents.Domain.Documents;
using Sepp.Documents.Domain.Fusion;
using Sepp.Documents.Domain.Langues;
using Sepp.Documents.Domain.Modeles;

using Shouldly;

namespace Sepp.Documents.Application.Tests;

public class DocumentsUseCasesTests
{
    private static readonly Guid Affilie = Guid.CreateVersion7();
    private static readonly Guid Personne = Guid.CreateVersion7();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static GenererDocument Demande(string code, TypeDestinataire type = TypeDestinataire.Personne, Guid? destinataire = null, bool publier = false) => new(
        code, type, destinataire ?? Personne, "test", "objet", Guid.CreateVersion7(),
        new Dictionary<string, ValeurChamp> { ["nom"] = ValeurChamp.Simple("Marie") }, null, null, null, Affilie, Personne, publier);

    private static ChampDeclare Nom => new("nom", TypeChamp.Texte, true, "Nom");

    private static GenererDocumentHandler Generer(Contexte c) => new(c.Generateur, c.Store, c.Acces, c.Store, c.Store);

    private async Task<DocumentDto> GenererPublie(Contexte c, ZoneDocument zone, TypeDestinataire type = TypeDestinataire.Personne, Guid? destinataire = null)
    {
        c.Publie("COURRIER", Language.Fr, zone, "Bonjour {{nom}}", Nom);
        var resultat = await Generer(c).HandleAsync(Demande("COURRIER", type, destinataire, publier: true), _ct);
        return resultat.Value;
    }

    // ---- Génération ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Un_document_est_genere_chiffre_archive_et_horodate()
    {
        var c = new Contexte(Roles.GestionnaireDossiers);
        c.Publie("COURRIER", Language.Fr, ZoneDocument.Standard, "# Bonjour {{nom}}", Nom);

        var resultat = await Generer(c).HandleAsync(Demande("courrier"), _ct);

        var dto = resultat.Value;
        dto.Format.ShouldBe("PDF/A-1a");
        dto.Empreinte.Length.ShouldBe(64);
        dto.AutoriteHorodatage.ShouldBe("test");
        var document = c.Store.Documents.Single();
        var stocke = c.Stockage.Objets[document.StockageUri];
        FakeRendu.Texte(stocke).ShouldNotContain("Marie"); // jamais en clair dans le stockage
        GenerateurDocuments.Empreinte(stocke).ShouldBe(document.EmpreinteStockage);
        FakeRendu.Texte(c.Chiffrement.Dechiffrer(document.Zone, document.Id, stocke)).ShouldContain("Bonjour Marie");
        c.Store.Acces.ShouldContain(a => a.Action == Sepp.BuildingBlocks.Application.Auditing.ActionAudit.Creation && a.DocumentId == document.Id);
    }

    [Fact]
    public async Task La_generation_exige_un_modele_publie()
    {
        var c = new Contexte(Roles.GestionnaireDossiers);
        var brouillon = Modele.Creer("COURRIER", Language.Fr, TypeModele.Courrier, ZoneDocument.Standard, "X", null, "{{nom}}", [Nom]);
        c.Store.Add(brouillon);

        var resultat = await Generer(c).HandleAsync(Demande("COURRIER"), _ct);

        resultat.Error!.Code.ShouldBe("documents.modele-non-publie");
        c.Store.Documents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_fusion_invalide_est_une_erreur_de_validation_sans_rien_archiver()
    {
        var c = new Contexte(Roles.GestionnaireDossiers);
        c.Publie("COURRIER", Language.Fr, ZoneDocument.Standard, "{{nom}}", Nom);
        var demande = Demande("COURRIER") with { Valeurs = new Dictionary<string, ValeurChamp>() };

        var resultat = await Generer(c).HandleAsync(demande, _ct);

        resultat.Error!.Kind.ShouldBe(ErrorKind.Validation);
        c.Stockage.Objets.ShouldBeEmpty();
    }

    [Fact]
    public async Task La_langue_suit_le_regime_de_l_affilie_ou_la_demande_explicite()
    {
        var c = new Contexte(Roles.GestionnaireDossiers);
        c.Publie("COURRIER", Language.Fr, ZoneDocument.Standard, "{{nom}}", Nom);
        c.Publie("COURRIER", Language.Nl, ZoneDocument.Standard, "{{nom}}", Nom);
        c.Source.Informations = new InformationsLinguistiques(RegimeLinguistique.Neerlandais, Language.Nl, null);

        var regime = (await Generer(c).HandleAsync(Demande("COURRIER", TypeDestinataire.Affilie, Affilie), _ct)).Value;
        var demandee = (await Generer(c).HandleAsync(Demande("COURRIER", TypeDestinataire.Affilie, Affilie) with { Langue = Language.Fr }, _ct)).Value;

        regime.Langue.ShouldBe(Language.Nl);
        regime.MotifLangue.ShouldContain("néerlandaise");
        demandee.Langue.ShouldBe(Language.Fr);
    }

    [Fact]
    public async Task Generer_un_document_medical_exige_le_droit_d_ecriture_du_dossier_de_sante()
    {
        var gestionnaire = new Contexte(Roles.GestionnaireDossiers);
        gestionnaire.Publie("MEDICAL", Language.Fr, ZoneDocument.Medicale, "{{nom}}", Nom);
        var cpmt = new Contexte(Roles.Cpmt);
        cpmt.Publie("MEDICAL", Language.Fr, ZoneDocument.Medicale, "{{nom}}", Nom);

        (await Generer(gestionnaire).HandleAsync(Demande("MEDICAL"), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await Generer(cpmt).HandleAsync(Demande("MEDICAL"), _ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Un_externe_ne_peut_pas_generer_de_document()
    {
        var c = new Contexte(Roles.Employeur);
        c.Publie("COURRIER", Language.Fr, ZoneDocument.Standard, "{{nom}}", Nom);

        (await Generer(c).HandleAsync(Demande("COURRIER"), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task La_publication_emet_document_publie_sans_aucune_donnee_de_contenu()
    {
        var c = new Contexte(Roles.GestionnaireDossiers);

        var dto = await GenererPublie(c, ZoneDocument.Standard);

        var evenement = c.Store.Published.OfType<DocumentPublie>().ShouldHaveSingleItem();
        evenement.DocumentId.ShouldBe(dto.Id);
        evenement.CodeModele.ShouldBe("COURRIER");
        evenement.TypeDestinataire.ShouldBe("personne");
        evenement.DestinataireId.ShouldBe(Personne);
    }

    [Fact]
    public async Task Un_exemplaire_du_dossier_ne_peut_pas_etre_publie()
    {
        var c = new Contexte(Roles.Cpmt);
        c.Publie("COURRIER", Language.Fr, ZoneDocument.Standard, "{{nom}}", Nom);

        (await Generer(c).HandleAsync(Demande("COURRIER", TypeDestinataire.Dossier, publier: true), _ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
    }

    // ---- Lecture : un document médical n'est pas lisible sans permission -------------------------------------------------

    [Fact]
    public async Task Un_document_medical_n_est_pas_lisible_sans_la_permission_du_dossier_de_sante()
    {
        var auteur = new Contexte(Roles.Cpmt);
        var dto = await GenererPublie(auteur, ZoneDocument.Medicale);

        foreach (var role in new[] { Roles.GestionnaireDossiers, Roles.AdministrateurFonctionnel, Roles.Planificateur, Roles.Cpap })
        {
            var lecteur = new Contexte(role);
            auteur.Store.Documents.ForEach(lecteur.Store.Documents.Add);
            lecteur.Stockage.Objets.Clear();
            foreach (var (uri, octets) in auteur.Stockage.Objets)
            {
                lecteur.Stockage.Objets[uri] = octets;
            }

            var contenu = new LireContenuDocumentHandler(lecteur.Store, lecteur.Stockage, lecteur.Chiffrement, lecteur.Store, lecteur.Acces);
            var metadonnees = new ObtenirDocumentHandler(lecteur.Store, lecteur.Acces);

            (await contenu.HandleAsync(new LireContenuDocument(dto.Id, null), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden, role);
            (await metadonnees.HandleAsync(new ObtenirDocument(dto.Id), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden, role);
            lecteur.Store.Acces.ShouldBeEmpty(role);
        }
    }

    [Fact]
    public async Task Le_cpmt_lit_un_document_medical_et_la_lecture_est_journalisee_dans_la_zone_medicale()
    {
        var c = new Contexte(Roles.Cpmt);
        var dto = await GenererPublie(c, ZoneDocument.Medicale);
        var handler = new LireContenuDocumentHandler(c.Store, c.Stockage, c.Chiffrement, c.Store, c.Acces);

        var resultat = await handler.HandleAsync(new LireContenuDocument(dto.Id, "dossier de suivi"), _ct);

        FakeRendu.Texte(resultat.Value.Contenu).ShouldContain("Bonjour Marie");
        c.Store.Acces.ShouldContain(a => a.Action == Sepp.BuildingBlocks.Application.Auditing.ActionAudit.Lecture && a.Zone == ZoneDocument.Medicale);
    }

    [Fact]
    public async Task Le_travailleur_lit_ses_documents_publies_mais_pas_ceux_d_un_autre()
    {
        var c = new Contexte(Roles.Cpmt);
        var propre = await GenererPublie(c, ZoneDocument.Medicale);
        var travailleur = new Contexte(Roles.Travailleur);
        travailleur.Perimetre.PersonneId = Personne;
        var autre = new Contexte(Roles.Travailleur);
        autre.Perimetre.PersonneId = Guid.CreateVersion7();

        var document = c.Store.Documents.Single();
        new AccesDocumentsPourTest(travailleur).VerifierLecture(document).ShouldBeNull();
        new AccesDocumentsPourTest(autre).VerifierLecture(document).ShouldNotBeNull().Kind.ShouldBe(ErrorKind.NotFound);
        propre.Statut.ShouldBe(StatutDocument.Publie);
    }

    [Fact]
    public async Task L_employeur_ne_lit_que_les_documents_standard_publies_de_son_affilie()
    {
        var c = new Contexte(Roles.GestionnaireDossiers);
        c.Publie("COURRIER", Language.Fr, ZoneDocument.Standard, "{{nom}}", Nom);
        c.Publie("MEDICAL", Language.Fr, ZoneDocument.Medicale, "{{nom}}", Nom);
        var standard = (await Generer(c).HandleAsync(Demande("COURRIER", TypeDestinataire.Affilie, Affilie, publier: true), _ct)).Value;

        var employeur = new Contexte(Roles.Employeur);
        employeur.Perimetre.Affilies = [Affilie];
        var autre = new Contexte(Roles.Employeur);
        autre.Perimetre.Affilies = [Guid.CreateVersion7()];
        var document = c.Store.Documents.Single(d => d.Id == standard.Id);

        new AccesDocumentsPourTest(employeur).VerifierLecture(document).ShouldBeNull();
        new AccesDocumentsPourTest(autre).VerifierLecture(document).ShouldNotBeNull().Kind.ShouldBe(ErrorKind.NotFound);
    }

    // ---- Intégrité --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task L_alteration_du_fichier_stocke_est_detectee_a_la_lecture_et_par_la_verification()
    {
        var c = new Contexte(Roles.Cpmt);
        var dto = await GenererPublie(c, ZoneDocument.Standard);
        var document = c.Store.Documents.Single();
        var verif = new VerifierIntegriteDocumentHandler(c.Store, c.Stockage, c.Chiffrement, c.Acces, c.Horloge);
        var lecture = new LireContenuDocumentHandler(c.Store, c.Stockage, c.Chiffrement, c.Store, c.Acces);

        (await verif.HandleAsync(new VerifierIntegriteDocument(dto.Id), _ct)).Value.Integre.ShouldBeTrue();

        var octets = c.Stockage.Objets[document.StockageUri];
        octets[^40] ^= 0x01;

        var apres = (await verif.HandleAsync(new VerifierIntegriteDocument(dto.Id), _ct)).Value;
        apres.Integre.ShouldBeFalse();
        apres.EmpreinteStockageCalculee.ShouldNotBe(apres.EmpreinteStockageAttendue);
        apres.Conclusion.ShouldContain("altéré");
        (await lecture.HandleAsync(new LireContenuDocument(dto.Id, null), _ct)).Error!.Code.ShouldBe("document.altere");
    }

    [Fact]
    public async Task Un_objet_remplace_par_celui_d_un_autre_document_est_detecte()
    {
        var c = new Contexte(Roles.Cpmt);
        c.Publie("COURRIER", Language.Fr, ZoneDocument.Standard, "{{nom}}", Nom);
        var a = (await Generer(c).HandleAsync(Demande("COURRIER"), _ct)).Value;
        var b = (await Generer(c).HandleAsync(Demande("COURRIER"), _ct)).Value;
        var docA = c.Store.Documents.Single(d => d.Id == a.Id);
        var docB = c.Store.Documents.Single(d => d.Id == b.Id);
        c.Stockage.Objets[docA.StockageUri] = c.Stockage.Objets[docB.StockageUri];

        var verif = new VerifierIntegriteDocumentHandler(c.Store, c.Stockage, c.Chiffrement, c.Acces, c.Horloge);

        (await verif.HandleAsync(new VerifierIntegriteDocument(a.Id), _ct)).Value.Integre.ShouldBeFalse();
    }

    [Fact]
    public async Task Un_objet_disparu_est_signale()
    {
        var c = new Contexte(Roles.Cpmt);
        var dto = await GenererPublie(c, ZoneDocument.Standard);
        c.Stockage.Objets.Clear();
        var verif = new VerifierIntegriteDocumentHandler(c.Store, c.Stockage, c.Chiffrement, c.Acces, c.Horloge);

        var resultat = (await verif.HandleAsync(new VerifierIntegriteDocument(dto.Id), _ct)).Value;

        resultat.Integre.ShouldBeFalse();
        resultat.ObjetPresent.ShouldBeFalse();
    }

    // ---- Idempotence, signature ----------------------------------------------------------------------------------------

    [Fact]
    public async Task La_generation_avec_une_meme_cle_d_idempotence_renvoie_le_document_existant()
    {
        var c = new Contexte(Roles.GestionnaireDossiers);
        c.Publie("COURRIER", Language.Fr, ZoneDocument.Standard, "{{nom}}", Nom);
        var demande = new DemandeGeneration("COURRIER", TypeDestinataire.Personne, Personne, "test", "objet", Guid.CreateVersion7(),
            _ => new Dictionary<string, ValeurChamp> { ["nom"] = ValeurChamp.Simple("M") }, new ContexteLinguistique(null, null, null, null), Affilie, Personne, null, "cle-1");

        var premier = (await c.Generateur.GenererAsync(demande, _ct)).Value;
        var second = (await c.Generateur.GenererAsync(demande, _ct)).Value;

        premier.Existant.ShouldBeFalse();
        second.Existant.ShouldBeTrue();
        second.Document.ShouldBeSameAs(premier.Document);
        c.Stockage.Objets.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Un_document_se_signe_une_fois_par_signataire_avec_une_preuve_sur_l_empreinte()
    {
        var c = new Contexte("medecin-1", [Roles.Cpmt]);
        var dto = await GenererPublie(c, ZoneDocument.Medicale);
        var handler = new SignerDocumentHandler(c.Store, new FakeSignature(c.Horloge), c.Store, c.Acces, c.Utilisateur, c.Store);

        var signature = (await handler.HandleAsync(new SignerDocument(dto.Id), _ct)).Value;
        var encore = await handler.HandleAsync(new SignerDocument(dto.Id), _ct);

        signature.EmpreinteSignee.ShouldBe(dto.Empreinte);
        signature.Preuve.ShouldContain(dto.Empreinte);
        encore.Error!.Code.ShouldBe("document.deja-signe");
    }

    // ---- Modèles : validation avant publication (DOC-01) ------------------------------------------------------------------

    [Fact]
    public async Task Un_modele_medical_est_valide_par_le_cpmt_dirigeant_et_publie_par_l_administrateur()
    {
        var admin = new Contexte(Roles.AdministrateurFonctionnel);
        var creer = new CreerModeleHandler(admin.Store, admin.Store, admin.Utilisateur);
        var id = (await creer.HandleAsync(new CreerModele("SANTE.TEST", Language.Fr, TypeModele.Formulaire, "medicale", "Test", null, "{{nom}}", [new ChampDto("nom", TypeChamp.Texte, true, "Nom")]), _ct)).Value;

        // L'administrateur seul ne peut pas valider un modèle médical.
        var parAdmin = new ValiderModeleHandler(admin.Store, admin.Store, admin.Utilisateur, admin.Acces, admin.Horloge);
        (await parAdmin.HandleAsync(new ValiderModele(id), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        // Et ne peut pas publier un modèle non validé.
        var publier = new PublierModeleHandler(admin.Store, admin.Store, admin.Utilisateur, admin.Horloge);
        (await publier.HandleAsync(new PublierModele(id), _ct)).Error!.Kind.ShouldBe(ErrorKind.Conflict);

        var dirigeant = new Contexte("dirigeant", [Roles.CpmtDirigeant]);
        dirigeant.Store.Modeles.AddRange(admin.Store.Modeles);
        var parDirigeant = new ValiderModeleHandler(dirigeant.Store, dirigeant.Store, dirigeant.Utilisateur, dirigeant.Acces, dirigeant.Horloge);
        (await parDirigeant.HandleAsync(new ValiderModele(id), _ct)).IsSuccess.ShouldBeTrue();

        (await publier.HandleAsync(new PublierModele(id), _ct)).IsSuccess.ShouldBeTrue();
        admin.Store.Modeles.Single().Statut.ShouldBe(StatutModele.Publie);
        admin.Store.Modeles.Single().ValidePar.ShouldBe("dirigeant");
    }

    [Fact]
    public async Task Un_modele_standard_est_valide_par_l_administrateur_pas_par_un_conseiller()
    {
        var admin = new Contexte(Roles.AdministrateurFonctionnel);
        var id = (await new CreerModeleHandler(admin.Store, admin.Store, admin.Utilisateur)
            .HandleAsync(new CreerModele("COURRIER.TEST", Language.Fr, TypeModele.Courrier, "standard", "Test", null, "{{nom}}", [new ChampDto("nom", TypeChamp.Texte, true, "Nom")]), _ct)).Value;
        var conseiller = new Contexte(Roles.Cpmt);
        conseiller.Store.Modeles.AddRange(admin.Store.Modeles);

        (await new ValiderModeleHandler(conseiller.Store, conseiller.Store, conseiller.Utilisateur, conseiller.Acces, conseiller.Horloge)
            .HandleAsync(new ValiderModele(id), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new ValiderModeleHandler(admin.Store, admin.Store, admin.Utilisateur, admin.Acces, admin.Horloge)
            .HandleAsync(new ValiderModele(id), _ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task La_publication_d_une_nouvelle_version_retire_la_precedente()
    {
        var c = new Contexte(Roles.AdministrateurFonctionnel);
        var v1 = c.Publie("COURRIER", Language.Fr, ZoneDocument.Standard, "{{nom}}", Nom);
        var versionHandler = new CreerNouvelleVersionHandler(c.Store, c.Store, c.Utilisateur);
        var v2Id = (await versionHandler.HandleAsync(new CreerNouvelleVersion(v1.Id), _ct)).Value;
        (await new ValiderModeleHandler(c.Store, c.Store, c.Utilisateur, c.Acces, c.Horloge).HandleAsync(new ValiderModele(v2Id), _ct)).IsSuccess.ShouldBeTrue();

        (await new PublierModeleHandler(c.Store, c.Store, c.Utilisateur, c.Horloge).HandleAsync(new PublierModele(v2Id), _ct)).IsSuccess.ShouldBeTrue();

        v1.Statut.ShouldBe(StatutModele.Retire);
        c.Store.Modeles.Single(m => m.Id == v2Id).Statut.ShouldBe(StatutModele.Publie);
        c.Store.Modeles.Count(m => m.Statut == StatutModele.Publie).ShouldBe(1);
    }

    [Fact]
    public async Task Un_modele_au_contenu_dangereux_est_refuse_a_la_creation()
    {
        var c = new Contexte(Roles.AdministrateurFonctionnel);
        var creer = new CreerModeleHandler(c.Store, c.Store, c.Utilisateur);

        var resultat = await creer.HandleAsync(new CreerModele("X", Language.Fr, TypeModele.Courrier, "standard", "X", null, "{{Environment.Exit(1)}}", []), _ct);

        resultat.Error!.Kind.ShouldBe(ErrorKind.Validation);
        c.Store.Modeles.ShouldBeEmpty();
    }

    [Fact]
    public async Task La_gestion_des_modeles_est_reservee_a_l_administrateur_fonctionnel()
    {
        var c = new Contexte(Roles.Cpmt);

        var resultat = await new CreerModeleHandler(c.Store, c.Store, c.Utilisateur)
            .HandleAsync(new CreerModele("X", Language.Fr, TypeModele.Courrier, "standard", "X", null, "x", []), _ct);

        resultat.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    /// <summary>Contrôle d'accès d'un autre utilisateur sur les documents d'un contexte (périmètre externe porté par le contexte).</summary>
    private sealed class AccesDocumentsPourTest(Contexte c)
    {
        public Error? VerifierLecture(Document document) => c.Acces.VerifierLecture(document);
    }
}
