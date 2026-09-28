using System.Text.Json;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts;
using Sepp.Contracts.Personnes;
using Sepp.Personnes.Application.Affectations;
using Sepp.Personnes.Application.EtatsParticuliers;
using Sepp.Personnes.Application.Imports;
using Sepp.Personnes.Application.Occupations;
using Sepp.Personnes.Application.Personnes;
using Sepp.Personnes.Domain.Personnes;

using Shouldly;

namespace Sepp.Personnes.Application.Tests;

public class UseCasesTests
{
    private static readonly Guid Affilie = Guid.CreateVersion7();
    private static readonly Guid AutreAffilie = Guid.CreateVersion7();
    private static readonly DateOnly Debut = new(2026, 1, 1);

    private readonly InMemoryStore _store = new();
    private readonly FakeNissIndex _index = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>NISS valide (contrôle modulo 97) d'une personne née le 30/07/1985.</summary>
    private static string Niss(int ordre) => NissValide("850730", ordre);

    private static string NissValide(string aammjj, int ordre, bool apres2000 = false)
    {
        var corps = long.Parse($"{aammjj}{ordre:D3}");
        var controle = 97 - ((apres2000 ? 2_000_000_000L + corps : corps) % 97);
        return $"{aammjj}{ordre:D3}{controle:D2}";
    }

    private static IdentiteDto Identite(string nom = "Dupont") => new(nom, "Marie", new DateOnly(1985, 7, 30), Sexe.Feminin, Language.Fr);

    private static NouvelleOccupationDto Occupation(Guid? affilie = null) =>
        new(affilie ?? Affilie, null, TypeTravailleur.Salarie, TypeContrat.DureeIndeterminee, Debut, null);

    private sealed record Contexte(ICurrentUser User, PerimetreUtilisateur Perimetre, AccesPersonnes Acces);

    private Contexte Comme(string role, Guid? affilie = null)
    {
        var user = new FakeUser(role);
        var perimetre = new PerimetreUtilisateur(user, new FakeContexte(affilie));
        return new Contexte(user, perimetre, new AccesPersonnes(_store, user, perimetre));
    }

    private CreerPersonneHandler Creer(Contexte c) =>
        new(_store, new EnregistrementTravailleurs(_store, _index), _store, _store, c.User, c.Perimetre);

    private async Task<Guid> CreerTravailleur(int ordre = 42, Guid? affilie = null)
    {
        var result = await Creer(Comme(Roles.GestionnaireDossiers))
            .HandleAsync(new CreerPersonne(Niss(ordre), Identite(), null, Occupation(affilie)), _ct);
        return result.Value.PersonneId;
    }

    private void AucunNissNiEtatEnClairDansLesEvenements(params string[] interdits)
    {
        foreach (var evenement in _store.Published)
        {
            var json = JsonSerializer.Serialize(evenement, evenement.GetType(), JsonSerializerOptions.Web);
            foreach (var interdit in interdits)
            {
                json.ShouldNotContain(interdit, Case.Insensitive);
            }
        }
    }

    [Fact]
    public async Task Le_gestionnaire_enregistre_un_travailleur_et_l_occupation_est_publiee_sans_niss()
    {
        var result = await Creer(Comme(Roles.GestionnaireDossiers))
            .HandleAsync(new CreerPersonne(Niss(42), Identite(), null, Occupation()), _ct);

        result.Value.Nouvelle.ShouldBeTrue();
        var evt = _store.Published.ShouldHaveSingleItem().ShouldBeOfType<OccupationDebutee>();
        evt.PersonneId.ShouldBe(result.Value.PersonneId);
        evt.AffilieId.ShouldBe(Affilie);
        AucunNissNiEtatEnClairDansLesEvenements(Niss(42));
    }

    [Fact]
    public async Task Un_conseiller_en_prevention_ne_peut_pas_enregistrer_de_travailleur()
    {
        var result = await Creer(Comme(Roles.ConseillerSecurite)).HandleAsync(new CreerPersonne(Niss(42), Identite(), null, Occupation()), _ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Un_niss_invalide_est_une_erreur_de_validation_qui_ne_le_revele_pas()
    {
        var result = await Creer(Comme(Roles.GestionnaireDossiers)).HandleAsync(new CreerPersonne("85073003329", Identite(), null, Occupation()), _ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Message.ShouldNotContain("85073003329");
        _store.Personnes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_niss_deja_connu_reprend_la_personne_existante()
    {
        var id = await CreerTravailleur();

        var second = await Creer(Comme(Roles.GestionnaireDossiers))
            .HandleAsync(new CreerPersonne(Niss(42), Identite(), null, Occupation(AutreAffilie)), _ct);

        second.Value.Nouvelle.ShouldBeFalse();
        second.Value.PersonneId.ShouldBe(id);
        _store.Personnes.ShouldHaveSingleItem().Occupations.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Un_niss_connu_avec_une_autre_identite_est_un_conflit()
    {
        await CreerTravailleur();

        var result = await Creer(Comme(Roles.GestionnaireDossiers))
            .HandleAsync(new CreerPersonne(Niss(42), Identite("Durand"), null, Occupation(AutreAffilie)), _ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public async Task L_employeur_n_enregistre_que_pour_son_affilie()
    {
        var employeur = Comme(Roles.Employeur, Affilie);

        (await Creer(employeur).HandleAsync(new CreerPersonne(Niss(42), Identite(), null, Occupation(AutreAffilie)), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await Creer(employeur).HandleAsync(new CreerPersonne(Niss(42), Identite(), null, null), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await Creer(employeur).HandleAsync(new CreerPersonne(Niss(42), Identite(), null, Occupation()), _ct))
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task L_employeur_ne_voit_que_ses_travailleurs_et_leurs_occupations_chez_lui()
    {
        var id = await CreerTravailleur(affilie: AutreAffilie);
        var employeur = Comme(Roles.Employeur, Affilie);

        (await new ObtenirPersonneHandler(employeur.Acces, employeur.Perimetre).HandleAsync(new ObtenirPersonne(id), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.NotFound);
        (await new RechercherParNissHandler(_store, _index, employeur.User, employeur.Perimetre).HandleAsync(new RechercherParNiss(Niss(42)), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.NotFound);

        await Creer(employeur).HandleAsync(new CreerPersonne(Niss(42), Identite(), null, Occupation()), _ct);

        var fiche = await new ObtenirPersonneHandler(employeur.Acces, employeur.Perimetre).HandleAsync(new ObtenirPersonne(id), _ct);
        fiche.Value.Occupations.ShouldHaveSingleItem().AffilieId.ShouldBe(Affilie);
        fiche.Value.NissMasque.ShouldNotContain("0730");
    }

    [Fact]
    public async Task La_recherche_par_niss_passe_par_l_index_aveugle()
    {
        var id = await CreerTravailleur();
        var cpmt = Comme(Roles.Cpmt);

        var trouvee = await new RechercherParNissHandler(_store, _index, cpmt.User, cpmt.Perimetre).HandleAsync(new RechercherParNiss("85.07.30-042." + Niss(42)[9..]), _ct);

        trouvee.Value.Id.ShouldBe(id);
        _store.Personnes.Single().NissHash.ShouldNotContain(Niss(42));
    }

    [Fact]
    public async Task La_direction_ne_lit_pas_les_donnees_des_travailleurs()
    {
        var direction = Comme(Roles.Direction);

        var result = await new RechercherParNissHandler(_store, _index, direction.User, direction.Perimetre).HandleAsync(new RechercherParNiss(Niss(42)), _ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task L_identite_n_est_corrigee_que_par_le_sepp()
    {
        var id = await CreerTravailleur();
        var employeur = Comme(Roles.Employeur, Affilie);
        var gestionnaire = Comme(Roles.GestionnaireDossiers);
        var nouvelle = Identite() with { Prenom = "Marie-Claire" };

        (await new ModifierIdentiteHandler(employeur.Acces, employeur.Perimetre, _store).HandleAsync(new ModifierIdentite(id, nouvelle), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new ModifierIdentiteHandler(gestionnaire.Acces, gestionnaire.Perimetre, _store).HandleAsync(new ModifierIdentite(id, nouvelle), _ct))
            .IsSuccess.ShouldBeTrue();
        _store.Personnes.Single().Prenom.ShouldBe("Marie-Claire");
    }

    private EnregistrerEntreeDimonaHandler Dimona(Contexte c) =>
        new(_store, new EnregistrementTravailleurs(_store, _index), _index, _store, _store, c.User, c.Perimetre);

    private static EnregistrerEntreeDimona Entree(string reference) =>
        new(reference, Niss(42), Identite(), Affilie, null, TypeTravailleur.Etudiant, TypeContrat.Etudiant, Debut, null);

    [Fact]
    public async Task L_entree_dimona_est_idempotente_sur_la_reference()
    {
        var gestionnaire = Comme(Roles.GestionnaireDossiers);

        var premier = await Dimona(gestionnaire).HandleAsync(Entree("DIM0001"), _ct);
        var rejeu = await Dimona(gestionnaire).HandleAsync(Entree(" dim0001 "), _ct);

        premier.Value.DejaEnregistree.ShouldBeFalse();
        rejeu.Value.DejaEnregistree.ShouldBeTrue();
        rejeu.Value.OccupationId.ShouldBe(premier.Value.OccupationId);
        rejeu.Value.PersonneId.ShouldBe(premier.Value.PersonneId);
        _store.Published.OfType<OccupationDebutee>().ShouldHaveSingleItem();
        _store.Personnes.ShouldHaveSingleItem().Occupations.ShouldHaveSingleItem().TypeTravailleur.ShouldBe(TypeTravailleur.Etudiant);
    }

    [Fact]
    public async Task Une_reference_dimona_deja_attribuee_a_une_autre_personne_est_un_conflit()
    {
        var gestionnaire = Comme(Roles.GestionnaireDossiers);
        await Dimona(gestionnaire).HandleAsync(Entree("DIM0001"), _ct);

        var result = await Dimona(gestionnaire).HandleAsync(Entree("DIM0001") with { Niss = Niss(43) }, _ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public async Task Une_reference_dimona_non_alphanumerique_est_refusee()
    {
        var result = await Dimona(Comme(Roles.GestionnaireDossiers)).HandleAsync(Entree("DIM-0001"), _ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task La_sortie_dimona_termine_l_occupation_une_seule_fois()
    {
        var gestionnaire = Comme(Roles.GestionnaireDossiers);
        await Dimona(gestionnaire).HandleAsync(Entree("DIM1"), _ct);
        var sortie = new EnregistrerSortieDimonaHandler(_store, _store, _store, gestionnaire.User, gestionnaire.Perimetre);

        (await sortie.HandleAsync(new EnregistrerSortieDimona("dim1", new DateOnly(2026, 3, 31)), _ct)).IsSuccess.ShouldBeTrue();
        (await sortie.HandleAsync(new EnregistrerSortieDimona("DIM1", new DateOnly(2026, 3, 31)), _ct)).IsSuccess.ShouldBeTrue();

        _store.Published.OfType<OccupationTerminee>().ShouldHaveSingleItem().DateFin.ShouldBe(new DateOnly(2026, 3, 31));
        (await sortie.HandleAsync(new EnregistrerSortieDimona("INCONNUE", Debut), _ct)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task L_alimentation_dimona_est_refusee_a_l_employeur()
    {
        var result = await Dimona(Comme(Roles.Employeur, Affilie)).HandleAsync(Entree("DIM1"), _ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task L_entreprise_utilisatrice_affecte_l_interimaire_a_ses_postes()
    {
        var gestionnaire = Comme(Roles.GestionnaireDossiers);
        var interim = new NouvelleOccupationDto(AutreAffilie, Affilie, TypeTravailleur.Interimaire, TypeContrat.Interim, Debut, new DateOnly(2026, 1, 31));
        var creee = await Creer(gestionnaire).HandleAsync(new CreerPersonne(Niss(42), Identite(), null, interim), _ct);
        var utilisateur = Comme(Roles.Employeur, Affilie);
        var poste = Guid.CreateVersion7();

        var affectation = await new AffecterHandler(utilisateur.Acces, utilisateur.Perimetre, _store, _store)
            .HandleAsync(new Affecter(creee.Value.PersonneId, creee.Value.OccupationId!.Value, poste, Guid.CreateVersion7(), Debut, null), _ct);

        affectation.IsSuccess.ShouldBeTrue();
        var evt = _store.Published.OfType<AffectationModifiee>().ShouldHaveSingleItem();
        evt.PosteId.ShouldBe(poste);
        evt.DateFin.ShouldBe(new DateOnly(2026, 2, 1));
    }

    [Fact]
    public async Task Terminer_l_occupation_publie_la_cloture_des_affectations()
    {
        var gestionnaire = Comme(Roles.GestionnaireDossiers);
        var creee = await Creer(gestionnaire).HandleAsync(new CreerPersonne(Niss(42), Identite(), null, Occupation()), _ct);
        var personneId = creee.Value.PersonneId;
        var occupationId = creee.Value.OccupationId!.Value;
        await new AffecterHandler(gestionnaire.Acces, gestionnaire.Perimetre, _store, _store)
            .HandleAsync(new Affecter(personneId, occupationId, Guid.CreateVersion7(), Guid.CreateVersion7(), Debut, null), _ct);

        (await new TerminerOccupationHandler(gestionnaire.Acces, gestionnaire.Perimetre, _store, _store)
            .HandleAsync(new TerminerOccupation(personneId, occupationId, new DateOnly(2026, 6, 30)), _ct)).IsSuccess.ShouldBeTrue();

        _store.Published.OfType<AffectationModifiee>().Last().DateFin.ShouldBe(new DateOnly(2026, 7, 1));
        _store.Published.OfType<OccupationTerminee>().ShouldHaveSingleItem();
        var historique = await new ListerAffectationsHandler(gestionnaire.Acces, gestionnaire.Perimetre).HandleAsync(new ListerAffectations(personneId, null), _ct);
        historique.Value.ShouldHaveSingleItem().ValideJusquAu.ShouldBe(new DateOnly(2026, 7, 1));
    }

    [Fact]
    public async Task Une_grossesse_est_publiee_sous_une_categorie_generique()
    {
        var id = await CreerTravailleur();
        var employeur = Comme(Roles.Employeur, Affilie);

        var etatId = await new DeclarerEtatParticulierHandler(employeur.Acces, employeur.Perimetre, _store, _store)
            .HandleAsync(new DeclarerEtatParticulier(id, TypeEtatParticulier.Grossesse, new DateOnly(2026, 3, 1), null), _ct);

        var evt = _store.Published.OfType<EtatParticulierDeclare>().ShouldHaveSingleItem();
        evt.EtatParticulierId.ShouldBe(etatId.Value);
        evt.Categorie.ShouldBe(EvenementsIntegration.ProtectionMaternite);
        AucunNissNiEtatEnClairDansLesEvenements(Niss(42), "grossesse", "allaitement", "Dupont");
    }

    [Fact]
    public async Task La_fin_anticipee_d_un_etat_particulier_republie_l_etat_courant()
    {
        var id = await CreerTravailleur();
        var gestionnaire = Comme(Roles.GestionnaireDossiers);
        var etatId = (await new DeclarerEtatParticulierHandler(gestionnaire.Acces, gestionnaire.Perimetre, _store, _store)
            .HandleAsync(new DeclarerEtatParticulier(id, TypeEtatParticulier.Allaitement, Debut, new DateOnly(2026, 9, 30)), _ct)).Value;

        await new TerminerEtatParticulierHandler(gestionnaire.Acces, gestionnaire.Perimetre, _store, _store)
            .HandleAsync(new TerminerEtatParticulier(id, etatId, new DateOnly(2026, 5, 31)), _ct);

        var evenements = _store.Published.OfType<EtatParticulierDeclare>().ToList();
        evenements.Count.ShouldBe(2);
        evenements.ShouldAllBe(e => e.EtatParticulierId == etatId && e.Categorie == "PROTECTION_MATERNITE");
        evenements[1].DateFin.ShouldBe(new DateOnly(2026, 5, 31));
    }

    [Fact]
    public async Task Les_etats_particuliers_ne_sont_lus_que_par_le_gestionnaire_le_cpmt_et_le_declarant()
    {
        var id = await CreerTravailleur();
        var gestionnaire = Comme(Roles.GestionnaireDossiers);
        await new DeclarerEtatParticulierHandler(gestionnaire.Acces, gestionnaire.Perimetre, _store, _store)
            .HandleAsync(new DeclarerEtatParticulier(id, TypeEtatParticulier.TravailDeNuit, Debut, null), _ct);

        async Task<Result<IReadOnlyList<EtatParticulierDto>>> Lister(Contexte c) =>
            await new ListerEtatsParticuliersHandler(c.Acces, c.User, c.Perimetre).HandleAsync(new ListerEtatsParticuliers(id), _ct);

        (await Lister(Comme(Roles.ConseillerSecurite))).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await Lister(Comme(Roles.Cpmt))).Value.ShouldHaveSingleItem().Type.ShouldBe(TypeEtatParticulier.TravailDeNuit);
        (await Lister(Comme(Roles.Employeur, Affilie))).Value.ShouldBeEmpty();
    }

    private ImporterTravailleursHandler Import(Contexte c) =>
        new(_store, new EnregistrementTravailleurs(_store, _index), _index, _store, _store, c.User, c.Perimetre);

    private static readonly string[] Colonnes = ["niss", "nom", "prenom", "date_naissance", "sexe", "langue", "date_debut", "type_travailleur", "email"];

    private static LigneImport Ligne(int numero, string niss, string nom = "Dupont", string naissance = "30/07/1985", string type = "", string email = "") =>
        new(numero, new Dictionary<string, string>
        {
            ["niss"] = niss,
            ["nom"] = nom,
            ["prenom"] = "Marie",
            ["date_naissance"] = naissance,
            ["sexe"] = "F",
            ["langue"] = "fr",
            ["date_debut"] = "2026-01-01",
            ["type_travailleur"] = type,
            ["email"] = email,
        });

    [Fact]
    public async Task L_import_controle_chaque_ligne_et_detecte_les_doublons()
    {
        await CreerTravailleur(ordre: 7, affilie: AutreAffilie);
        var lignes = new[]
        {
            Ligne(2, Niss(1), email: "marie@example.test"),
            Ligne(3, "85073003329"),
            Ligne(4, Niss(1)),
            Ligne(5, Niss(7)),
            Ligne(6, Niss(8), naissance: "pas une date", type: "Astronaute"),
            Ligne(7, Niss(9), type: "interimaire"),
        };

        var rapport = (await Import(Comme(Roles.GestionnaireDossiers)).HandleAsync(new ImporterTravailleurs(Affilie, Colonnes, lignes, false), _ct)).Value;

        rapport.Total.ShouldBe(6);
        rapport.PersonnesCreees.ShouldBe(1);
        rapport.OccupationsAjoutees.ShouldBe(1);
        rapport.Erreurs.ShouldBe(4);
        rapport.Lignes.Single(l => l.Ligne == 2).Statut.ShouldBe(StatutLigneImport.PersonneCreee);
        rapport.Lignes.Single(l => l.Ligne == 3).Erreurs.ShouldHaveSingleItem().ShouldContain("modulo 97");
        rapport.Lignes.Single(l => l.Ligne == 4).Erreurs.ShouldHaveSingleItem().ShouldContain("ligne 2");
        rapport.Lignes.Single(l => l.Ligne == 5).Statut.ShouldBe(StatutLigneImport.OccupationAjoutee);
        rapport.Lignes.Single(l => l.Ligne == 6).Erreurs.Count.ShouldBe(2);
        rapport.Lignes.Single(l => l.Ligne == 7).Erreurs.ShouldHaveSingleItem().ShouldContain("affilié utilisateur");
        _store.Personnes.Count.ShouldBe(2);
        _store.Published.OfType<OccupationDebutee>().Count().ShouldBe(3);
        JsonSerializer.Serialize(rapport).ShouldNotContain(Niss(1));
    }

    [Fact]
    public async Task Une_simulation_d_import_n_enregistre_rien()
    {
        var rapport = (await Import(Comme(Roles.GestionnaireDossiers))
            .HandleAsync(new ImporterTravailleurs(Affilie, Colonnes, [Ligne(2, Niss(1))], true), _ct)).Value;

        rapport.PersonnesCreees.ShouldBe(1);
        _store.Personnes.ShouldBeEmpty();
        _store.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Un_en_tete_incomplet_est_refuse()
    {
        var result = await Import(Comme(Roles.GestionnaireDossiers))
            .HandleAsync(new ImporterTravailleurs(Affilie, ["niss", "nom", "colonne_inconnue"], [Ligne(2, Niss(1))], false), _ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Message.ShouldContain("prenom");
        result.Error.Message.ShouldContain("colonne_inconnue");
    }

    [Fact]
    public async Task L_employeur_n_importe_que_pour_son_affilie()
    {
        var result = await Import(Comme(Roles.Employeur, Affilie))
            .HandleAsync(new ImporterTravailleurs(AutreAffilie, Colonnes, [Ligne(2, Niss(1))], false), _ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task La_liste_des_travailleurs_d_un_affilie_est_limitee_a_son_perimetre()
    {
        await CreerTravailleur(ordre: 1);
        await CreerTravailleur(ordre: 2, affilie: AutreAffilie);
        var employeur = Comme(Roles.Employeur, Affilie);
        var handler = new ListerTravailleursHandler(_store, employeur.User, employeur.Perimetre);

        (await handler.HandleAsync(new ListerTravailleurs(Affilie, new DateOnly(2026, 6, 1)), _ct)).Value.ShouldHaveSingleItem();
        (await handler.HandleAsync(new ListerTravailleurs(AutreAffilie, new DateOnly(2026, 6, 1)), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public void Grossesse_et_allaitement_sont_confondus_dans_une_categorie_generique()
    {
        var grossesse = EvenementsIntegration.Categorie(TypeEtatParticulier.Grossesse);

        EvenementsIntegration.Categorie(TypeEtatParticulier.Allaitement).ShouldBe(grossesse);
        grossesse.ShouldNotContain("grossesse", Case.Insensitive);
        grossesse.ShouldNotContain("allaitement", Case.Insensitive);
        Enum.GetValues<TypeEtatParticulier>().Select(EvenementsIntegration.Categorie).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public void Les_evenements_du_service_sont_des_contrats_personnes()
    {
        IntegrationEvent[] evenements =
        [
            new OccupationDebutee(Guid.Empty, Guid.Empty, Guid.Empty, Debut),
            new EtatParticulierDeclare(Guid.Empty, Guid.Empty, "X", Debut, null),
        ];

        evenements.ShouldAllBe(e => EventContractAttribute.Of(e.GetType()).Topic == "personnes");
    }
}
