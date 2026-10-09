using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Personnes;
using Sepp.Personnes.Application.Mutations;
using Sepp.Personnes.Application.Personnes;
using Sepp.Personnes.Domain.Personnes;

using Shouldly;

namespace Sepp.Personnes.Application.Tests;

/// <summary>AFF-20, AFF-22, ARC-06, DAT-04 : traitement des mutations du registre national.</summary>
public class MutationsRegistreNationalTests
{
    private static readonly Guid Affilie = Guid.CreateVersion7();
    private static readonly DateOnly Debut = new(2026, 1, 1);

    private readonly InMemoryStore _store = new();
    private readonly FakeNissIndex _index = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static string NissValide(int ordre)
    {
        var corps = long.Parse($"850730{ordre:D3}");
        return $"850730{ordre:D3}{97 - (corps % 97):D2}";
    }

    private async Task<Guid> CreerTravailleur(string niss)
    {
        var user = new FakeUser(Roles.GestionnaireDossiers);
        var perimetre = new PerimetreUtilisateur(user, new FakeContexte());
        var result = await new CreerPersonneHandler(_store, new EnregistrementTravailleurs(_store, _index), _store, _store, user, perimetre)
            .HandleAsync(new CreerPersonne(niss, new IdentiteDto("Dupont", "Marie", new DateOnly(1985, 7, 30), Sexe.Feminin, Language.Fr), null,
                new NouvelleOccupationDto(Affilie, null, TypeTravailleur.Salarie, TypeContrat.DureeIndeterminee, Debut, null)), _ct);
        _store.Published.Clear();
        return result.Value.PersonneId;
    }

    private EnregistrerMutationRegistreNationalHandler Handler(params string[] roles)
    {
        var user = new FakeUser(roles);
        return new(_store, _index, _store, _store, user, new PerimetreUtilisateur(user, new FakeContexte()));
    }

    private static EnregistrerMutationRegistreNational Mutation(string niss, string reference, TypeMutationRegistreNational type, DateOnly? effet = null) =>
        new(reference, niss, type, effet ?? new DateOnly(2026, 9, 1), null, null, null, null);

    [Fact]
    public async Task Un_changement_de_nom_met_a_jour_la_personne_sans_publier_d_evenement()
    {
        var niss = NissValide(42);
        var id = await CreerTravailleur(niss);

        var result = await Handler(Roles.Integrations).HandleAsync(Mutation(niss, "RN-1", TypeMutationRegistreNational.ChangementNom) with { Nom = "Martin" }, _ct);

        result.Value.ShouldBe(new MutationEnregistreeDto(id, StatutMutation.Appliquee));
        var personne = _store.Personnes.Single();
        personne.Nom.ShouldBe("Martin");
        personne.Mutations.ShouldHaveSingleItem().Reference.ShouldBe("RN-1");

        // ARC-06 : un changement d'identité ne produit aucun événement (aucun consommateur n'en a besoin).
        _store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_changement_d_adresse_et_de_langue_est_applique()
    {
        var niss = NissValide(42);
        await CreerTravailleur(niss);
        var handler = Handler(Roles.Integrations);

        (await handler.HandleAsync(Mutation(niss, "RN-1", TypeMutationRegistreNational.ChangementAdresse) with
        {
            Adresse = new AdresseDto("Avenue Louise", "1", null, "1050", "Ixelles"),
        }, _ct)).IsSuccess.ShouldBeTrue();
        (await handler.HandleAsync(Mutation(niss, "RN-2", TypeMutationRegistreNational.ChangementLangue) with { Langue = Language.Nl }, _ct)).IsSuccess.ShouldBeTrue();

        var personne = _store.Personnes.Single();
        personne.Adresse!.Rue.ShouldBe("Avenue Louise");
        personne.Langue.ShouldBe(Language.Nl);
        personne.Mutations.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Le_traitement_est_idempotent_sur_la_reference_de_la_mutation()
    {
        var niss = NissValide(42);
        var id = await CreerTravailleur(niss);
        var handler = Handler(Roles.Integrations);
        var mutation = Mutation(niss, "RN-1", TypeMutationRegistreNational.ChangementNom) with { Nom = "Martin" };

        var premiere = await handler.HandleAsync(mutation, _ct);
        await handler.HandleAsync(Mutation(niss, "RN-2", TypeMutationRegistreNational.ChangementNom) with { Nom = "Durand" }, _ct);
        var savesAvantRejeu = _store.Saves;
        var rejeu = await handler.HandleAsync(mutation, _ct);

        premiere.Value.Statut.ShouldBe(StatutMutation.Appliquee);
        rejeu.Value.ShouldBe(new MutationEnregistreeDto(id, StatutMutation.DejaAppliquee));
        _store.Saves.ShouldBe(savesAvantRejeu);
        _store.Personnes.Single().Nom.ShouldBe("Durand");
    }

    [Fact]
    public async Task Un_deces_clot_les_occupations_actives_et_publie_occupation_terminee()
    {
        var niss = NissValide(42);
        var id = await CreerTravailleur(niss);
        var deces = new DateOnly(2026, 9, 15);

        var result = await Handler(Roles.Integrations).HandleAsync(Mutation(niss, "RN-9", TypeMutationRegistreNational.Deces, deces), _ct);

        result.Value.ShouldBe(new MutationEnregistreeDto(id, StatutMutation.Appliquee));
        var personne = _store.Personnes.Single();
        personne.DateDeces.ShouldBe(deces);
        var occupation = personne.Occupations.Single();
        occupation.DateFin.ShouldBe(deces);
        var terminee = _store.Published.OfType<OccupationTerminee>().ShouldHaveSingleItem();
        (terminee.PersonneId, terminee.OccupationId, terminee.DateFin).ShouldBe((id, occupation.Id, deces));
    }

    [Fact]
    public async Task Les_evenements_publies_par_un_deces_ne_contiennent_ni_niss_ni_identite()
    {
        var niss = NissValide(42);
        await CreerTravailleur(niss);

        await Handler(Roles.Integrations).HandleAsync(Mutation(niss, "RN-9", TypeMutationRegistreNational.Deces, new DateOnly(2026, 9, 15)), _ct);

        var json = System.Text.Json.JsonSerializer.Serialize(_store.Published.Cast<object>().ToList());
        json.ShouldNotContain(niss);
        json.ShouldNotContain("Dupont");
        json.ShouldNotContain("Marie");
    }

    [Fact]
    public async Task Une_personne_inconnue_est_ignoree_sans_erreur()
    {
        var result = await Handler(Roles.Integrations).HandleAsync(Mutation(NissValide(77), "RN-1", TypeMutationRegistreNational.Deces), _ct);

        result.Value.ShouldBe(new MutationEnregistreeDto(null, StatutMutation.PersonneInconnue));
        _store.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Une_reference_deja_utilisee_pour_une_autre_personne_est_un_conflit()
    {
        await CreerTravailleur(NissValide(42));
        await CreerTravailleur(NissValide(43));
        var handler = Handler(Roles.Integrations);
        await handler.HandleAsync(Mutation(NissValide(42), "RN-1", TypeMutationRegistreNational.ChangementNom) with { Nom = "Martin" }, _ct);

        var result = await handler.HandleAsync(Mutation(NissValide(43), "RN-1", TypeMutationRegistreNational.ChangementNom) with { Nom = "Martin" }, _ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public async Task Une_mutation_invalide_est_une_erreur_de_validation_sans_effet()
    {
        var niss = NissValide(42);
        await CreerTravailleur(niss);
        var handler = Handler(Roles.Integrations);

        (await handler.HandleAsync(Mutation(niss, "RN-1", TypeMutationRegistreNational.ChangementNom), _ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await handler.HandleAsync(Mutation(niss, " ", TypeMutationRegistreNational.Deces), _ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await handler.HandleAsync(Mutation("85073000000", "RN-2", TypeMutationRegistreNational.Deces), _ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);

        _store.Personnes.Single().Mutations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Le_niss_n_apparait_pas_dans_la_representation_textuelle_de_la_commande_ni_dans_l_erreur()
    {
        var niss = NissValide(42);
        var commande = Mutation(niss, "RN-1", TypeMutationRegistreNational.Deces);

        commande.ToString().ShouldNotContain(niss);
        var result = await Handler(Roles.Integrations).HandleAsync(commande with { Niss = niss[..10] + "X" }, _ct);
        result.Error!.Message.ShouldNotContain(niss[..10]);
    }

    [Fact]
    public async Task Les_mutations_sont_reservees_au_compte_technique_et_aux_internes_habilites()
    {
        var niss = NissValide(42);
        await CreerTravailleur(niss);
        var commande = Mutation(niss, "RN-1", TypeMutationRegistreNational.Deces);

        (await Handler(Roles.Employeur).HandleAsync(commande, _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await Handler(Roles.Cpmt).HandleAsync(commande, _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await Handler(Roles.Integrations).HandleAsync(commande, _ct)).IsSuccess.ShouldBeTrue();
    }
}
