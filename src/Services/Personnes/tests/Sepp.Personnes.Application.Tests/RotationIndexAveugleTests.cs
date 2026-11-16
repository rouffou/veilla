using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Personnes;
using Sepp.Personnes.Application.Imports;
using Sepp.Personnes.Application.Mutations;
using Sepp.Personnes.Application.Occupations;
using Sepp.Personnes.Application.Personnes;
using Sepp.Personnes.Domain.Personnes;

using Shouldly;

namespace Sepp.Personnes.Application.Tests;

/// <summary>
/// Rotation de la clé de l'index aveugle (#473) : pendant le recalcul, une personne dont l'empreinte utilise encore
/// l'ancienne clé reste trouvée par toutes les recherches par NISS (API, identification pour Intégrations, import,
/// mutations du registre national, DIMONA), et aucun doublon n'est créé. Seule la clé courante calcule une empreinte écrite.
/// </summary>
public class RotationIndexAveugleTests
{
    private const string AncienneCle = "ancienne-cle-de-test-32-octets!!";
    private const string NouvelleCle = "nouvelle-cle-de-test-32-octets!!";

    private static readonly Guid Affilie = Guid.CreateVersion7();
    private static readonly Guid AutreAffilie = Guid.CreateVersion7();
    private static readonly DateOnly Debut = new(2026, 1, 1);

    private readonly InMemoryStore _store = new();
    private readonly FakeNissIndex _avant = new(AncienneCle);
    private readonly FakeNissIndex _pendant = new(NouvelleCle, AncienneCle);
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static string Niss(int ordre)
    {
        var corps = long.Parse($"850730{ordre:D3}", System.Globalization.CultureInfo.InvariantCulture);
        return $"850730{ordre:D3}{97 - (corps % 97):D2}";
    }

    private static IdentiteDto Identite(string nom = "Dupont") => new(nom, "Marie", new DateOnly(1985, 7, 30), Sexe.Feminin, Language.Fr);

    private static NouvelleOccupationDto Occupation() => new(Affilie, null, TypeTravailleur.Salarie, TypeContrat.DureeIndeterminee, Debut, null);

    private static (ICurrentUser User, PerimetreUtilisateur Perimetre) Comme(string role)
    {
        var user = new FakeUser(role);
        return (user, new PerimetreUtilisateur(user, new FakeContexte()));
    }

    /// <summary>Personne enregistrée avant la rotation : son empreinte utilise l'ancienne clé.</summary>
    private async Task<Guid> CreerAvantRotation(string niss)
    {
        var (user, perimetre) = Comme(Roles.GestionnaireDossiers);
        var creee = await new CreerPersonneHandler(_store, new EnregistrementTravailleurs(_store, _avant), _store, _store, user, perimetre)
            .HandleAsync(new CreerPersonne(niss, Identite(), null, Occupation()), _ct);
        _store.Personnes.Single(p => p.Id == creee.Value.PersonneId).NissHash.ShouldBe(_avant.Calculer(Domain.Personnes.Niss.Parse(niss)));
        return creee.Value.PersonneId;
    }

    private CreerPersonneHandler CreerPendant() =>
        new(_store, new EnregistrementTravailleurs(_store, _pendant), _store, _store, Comme(Roles.GestionnaireDossiers).User, Comme(Roles.GestionnaireDossiers).Perimetre);

    [Fact]
    public async Task La_recherche_et_l_identification_trouvent_une_personne_encore_indexee_avec_l_ancienne_cle()
    {
        var id = await CreerAvantRotation(Niss(42));
        var (cpmt, perimetre) = Comme(Roles.Cpmt);
        var (integrations, _) = Comme(Roles.Integrations);

        (await new RechercherParNissHandler(_store, _pendant, cpmt, perimetre).HandleAsync(new RechercherParNiss(Niss(42)), _ct)).Value.Id.ShouldBe(id);
        (await new IdentifierPersonneHandler(_store, _pendant, integrations).HandleAsync(new IdentifierPersonne(Niss(42)), _ct)).Value.PersonneId.ShouldBe(id);

        // Sans la clé précédente (rotation mal préparée), la même recherche échouerait : c'est ce que la double lecture évite.
        (await new RechercherParNissHandler(_store, new FakeNissIndex(NouvelleCle), cpmt, perimetre).HandleAsync(new RechercherParNiss(Niss(42)), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Un_doublon_est_refuse_pendant_la_transition_et_une_nouvelle_personne_recoit_l_empreinte_courante()
    {
        var id = await CreerAvantRotation(Niss(42));

        var reprise = await CreerPendant().HandleAsync(new CreerPersonne(Niss(42), Identite(), null, Occupation() with { AffilieId = AutreAffilie }), _ct);
        var conflit = await CreerPendant().HandleAsync(new CreerPersonne(Niss(42), Identite("Durand"), null, Occupation()), _ct);
        var nouvelle = await CreerPendant().HandleAsync(new CreerPersonne(Niss(43), Identite(), null, Occupation()), _ct);

        reprise.Value.Nouvelle.ShouldBeFalse();
        reprise.Value.PersonneId.ShouldBe(id);
        conflit.Error!.Kind.ShouldBe(ErrorKind.Conflict);
        nouvelle.Value.Nouvelle.ShouldBeTrue();
        _store.Personnes.Count.ShouldBe(2);
        _store.Personnes.Single(p => p.Id == nouvelle.Value.PersonneId).NissHash.ShouldBe(_pendant.Calculer(Domain.Personnes.Niss.Parse(Niss(43))));
    }

    [Fact]
    public async Task L_import_reprend_la_personne_indexee_avec_l_ancienne_cle_sans_doublon()
    {
        var id = await CreerAvantRotation(Niss(7));
        var (user, perimetre) = Comme(Roles.GestionnaireDossiers);
        var import = new ImporterTravailleursHandler(_store, new EnregistrementTravailleurs(_store, _pendant), _pendant, _store, _store, user, perimetre);
        var ligne = new LigneImport(2, new Dictionary<string, string>
        {
            ["niss"] = Niss(7),
            ["nom"] = "Dupont",
            ["prenom"] = "Marie",
            ["date_naissance"] = "30/07/1985",
            ["sexe"] = "F",
            ["langue"] = "fr",
            ["date_debut"] = "2026-01-01",
        });

        var rapport = (await import.HandleAsync(new ImporterTravailleurs(AutreAffilie, [.. ligne.Valeurs.Keys], [ligne], false), _ct)).Value;

        rapport.Lignes.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            l => l.Statut.ShouldBe(StatutLigneImport.OccupationAjoutee),
            l => l.PersonneId.ShouldBe(id));
        _store.Personnes.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Une_mutation_du_registre_national_et_une_entree_dimona_trouvent_la_personne_indexee_avec_l_ancienne_cle()
    {
        var id = await CreerAvantRotation(Niss(42));
        var (integrations, perimetre) = Comme(Roles.Integrations);
        var mutations = new EnregistrerMutationRegistreNationalHandler(_store, _pendant, _store, _store, integrations, perimetre);
        var mutation = new EnregistrerMutationRegistreNational("RN-1", Niss(42), TypeMutationRegistreNational.ChangementNom, new DateOnly(2026, 9, 1), null, "Martin", null, null);

        (await mutations.HandleAsync(mutation, _ct)).Value.ShouldBe(new MutationEnregistreeDto(id, StatutMutation.Appliquee));
        // Rejeu de la même référence : retrouvée par la référence, dont la personne porte l'ancienne empreinte (pas un conflit).
        (await mutations.HandleAsync(mutation, _ct)).Value.ShouldBe(new MutationEnregistreeDto(id, StatutMutation.DejaAppliquee));

        var dimona = new EnregistrerEntreeDimonaHandler(_store, new EnregistrementTravailleurs(_store, _pendant), _pendant, _store, _store, integrations, perimetre);
        var entree = new EnregistrerEntreeDimona("DIM0001", Niss(42), Identite("Martin"), AutreAffilie, null, TypeTravailleur.Etudiant, TypeContrat.Etudiant, Debut, null);
        var premiere = await dimona.HandleAsync(entree, _ct);
        var rejeu = await dimona.HandleAsync(entree, _ct);

        premiere.Value.PersonneId.ShouldBe(id);
        rejeu.Value.DejaEnregistree.ShouldBeTrue();
        _store.Personnes.ShouldHaveSingleItem();
    }
}
