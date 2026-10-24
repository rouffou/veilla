using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts;
using Sepp.Personnes.Application.Occupations;
using Sepp.Personnes.Domain.Personnes;

using Shouldly;

namespace Sepp.Personnes.Application.Tests;

/// <summary>POR-11 : « mes occupations actives » du travailleur, limitées à la personne du jeton (claim personne_id).</summary>
public class MesOccupationsTests
{
    private static readonly Guid Affilie = Guid.CreateVersion7();
    private static readonly Guid Ancien = Guid.CreateVersion7();
    private static readonly Guid Agence = Guid.CreateVersion7();
    private static readonly DateOnly Aujourdhui = new(2026, 6, 15);

    private readonly InMemoryStore _store = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private sealed class ContexteFixe(Guid? personne) : IContextePersonne
    {
        public Guid? PersonneId => personne;
    }

    private static string NissValide(int ordre)
    {
        var corps = long.Parse($"850730{ordre:D3}");
        return $"850730{ordre:D3}{97 - (corps % 97):D2}";
    }

    private Personne Ajouter(int ordre, params NouvelleOccupation[] occupations)
    {
        var niss = Niss.Parse(NissValide(ordre));
        var personne = Personne.Creer(
            niss, new FakeNissIndex().Calculer(niss),
            new Identite("Dupont", "Marie", new DateOnly(1985, 7, 30), Sexe.Feminin, Language.Fr),
            CoordonneesDto.Aucune.ToCoordonnees());
        foreach (var occupation in occupations)
        {
            personne.DebuterOccupation(occupation);
        }

        _store.Add(personne);
        return personne;
    }

    private static NouvelleOccupation Occupation(Guid affilie, string debut, string? fin = null, string? dimona = null) =>
        new(affilie, null, TypeTravailleur.Salarie, TypeContrat.DureeIndeterminee, DateOnly.Parse(debut), fin is null ? null : DateOnly.Parse(fin), dimona);

    private ListerMesOccupationsHandler Handler(string role, Guid? personneDuJeton) =>
        new(_store, new FakeUser(role), new ContexteFixe(personneDuJeton));

    [Fact]
    public async Task Le_travailleur_voit_ses_occupations_actives_sans_les_terminees_ni_les_futures_ni_la_reference_dimona()
    {
        var moi = Ajouter(1, Occupation(Affilie, "2025-01-01", dimona: "DIMONA1"), Occupation(Ancien, "2020-01-01", "2024-12-31"),
            Occupation(Agence, "2027-01-01"));

        var resultat = await Handler(Roles.Travailleur, moi.Id).HandleAsync(new ListerMesOccupations(Aujourdhui), _ct);

        var occupation = resultat.Value.ShouldHaveSingleItem();
        occupation.AffilieId.ShouldBe(Affilie);
        occupation.DateDebut.ShouldBe(new DateOnly(2025, 1, 1));
        occupation.DateFin.ShouldBeNull();
        typeof(MonOccupationDto).GetProperties().Select(p => p.Name).ShouldNotContain("ReferenceDimona");
    }

    [Fact]
    public async Task Une_occupation_se_termine_le_dernier_jour_inclus()
    {
        var moi = Ajouter(1, Occupation(Affilie, "2025-01-01", "2026-06-15"));

        (await Handler(Roles.Travailleur, moi.Id).HandleAsync(new ListerMesOccupations(Aujourdhui), _ct)).Value.ShouldHaveSingleItem();
        (await Handler(Roles.Travailleur, moi.Id).HandleAsync(new ListerMesOccupations(Aujourdhui.AddDays(1)), _ct)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Le_travailleur_ne_voit_jamais_les_occupations_d_une_autre_personne()
    {
        var autre = Ajouter(1, Occupation(Affilie, "2025-01-01"));
        var moi = Ajouter(2, Occupation(Ancien, "2025-03-01"));

        var miennes = (await Handler(Roles.Travailleur, moi.Id).HandleAsync(new ListerMesOccupations(Aujourdhui), _ct)).Value;
        var siennes = (await Handler(Roles.Travailleur, autre.Id).HandleAsync(new ListerMesOccupations(Aujourdhui), _ct)).Value;
        var inconnue = await Handler(Roles.Travailleur, Guid.CreateVersion7()).HandleAsync(new ListerMesOccupations(Aujourdhui), _ct);

        miennes.ShouldHaveSingleItem().AffilieId.ShouldBe(Ancien);
        siennes.ShouldHaveSingleItem().AffilieId.ShouldBe(Affilie);
        inconnue.Error!.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Sans_personne_dans_le_jeton_la_lecture_est_refusee()
    {
        Ajouter(1, Occupation(Affilie, "2025-01-01"));

        var resultat = await Handler(Roles.Travailleur, null).HandleAsync(new ListerMesOccupations(Aujourdhui), _ct);

        resultat.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        resultat.Error.Code.ShouldBe("perimetre.interdit");
    }

    [Theory]
    [InlineData(Roles.Employeur)]
    [InlineData(Roles.Sipp)]
    [InlineData(Roles.Cpmt)]
    [InlineData(Roles.GestionnaireDossiers)]
    public async Task Seul_le_travailleur_a_la_permission_de_lire_ses_occupations(string role)
    {
        var personne = Ajouter(1, Occupation(Affilie, "2025-01-01"));

        var resultat = await Handler(role, personne.Id).HandleAsync(new ListerMesOccupations(Aujourdhui), _ct);

        resultat.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public void Le_travailleur_n_a_pas_la_permission_de_lire_les_fiches_des_personnes()
    {
        var travailleur = new FakeUser(Roles.Travailleur);

        travailleur.HasPermission(Permissions.PersonneLire).ShouldBeFalse();
        travailleur.HasPermission(Permissions.PersonneEcrire).ShouldBeFalse();
        travailleur.HasPermission(Permissions.PersonneLirePropres).ShouldBeTrue();
    }
}
