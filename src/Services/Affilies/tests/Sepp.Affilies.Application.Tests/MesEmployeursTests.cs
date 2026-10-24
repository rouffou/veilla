using Sepp.Affilies.Application.Occupations;
using Sepp.Affilies.Domain.Affilies;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Personnes;

using Shouldly;

namespace Sepp.Affilies.Application.Tests;

/// <summary>POR-11, #340 — Dénomination des employeurs de la personne du jeton, limitée à ses occupations actives.</summary>
public class MesEmployeursTests
{
    private static readonly DateOnly Jour = new(2026, 10, 24);

    private readonly InMemoryStore _store = new();
    private readonly FakeOccupations _occupations = new();
    private readonly Guid _personne = Guid.CreateVersion7();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private Affilie Affilie(string bce, string denomination, Language langue = Language.Fr)
    {
        var affilie = Domain.Affilies.Affilie.Creer(
            new NumeroBce(bce),
            new DonneesFiche(denomination, "SRL", "10.711", "118.03", CategorieTarifaire.B, langue, RegimeLinguistique.Francais),
            new DateOnly(2020, 1, 1));
        _store.Affilies.Add(affilie);
        return affilie;
    }

    private async Task Debuter(Guid personne, Guid affilie, DateOnly debut, Guid? occupationId = null) =>
        await new OccupationDebuteeHandler(_occupations, _store)
            .HandleAsync(new OccupationDebutee(occupationId ?? Guid.CreateVersion7(), personne, affilie, debut), _ct);

    private Task<Result<IReadOnlyList<MonEmployeurDto>>> ListerAsync(string role = Roles.Travailleur, Guid? personne = null, bool sansClaim = false) =>
        new ListerMesEmployeursHandler(_occupations, _store, new FakeUser([role]), new FakeContextePersonne(sansClaim ? null : personne ?? _personne))
            .HandleAsync(new ListerMesEmployeurs(Jour), _ct);

    [Fact]
    public async Task Le_travailleur_voit_la_denomination_et_la_langue_de_ses_employeurs_actifs()
    {
        var dupont = Affilie("0202.239.951", "Boulangerie Dupont", Language.Nl);
        var durand = Affilie("0403.170.701", "Atelier Durand");
        await Debuter(_personne, dupont.Id, new DateOnly(2025, 1, 1));
        await Debuter(_personne, durand.Id, new DateOnly(2024, 6, 1));

        var resultat = await ListerAsync();

        resultat.IsSuccess.ShouldBeTrue();
        resultat.Value.Select(e => e.Denomination).ShouldBe(["Atelier Durand", "Boulangerie Dupont"]);
        resultat.Value.Single(e => e.AffilieId == dupont.Id).Langue.ShouldBe(Language.Nl);
    }

    [Fact]
    public async Task Un_affilie_chez_qui_la_personne_n_a_pas_d_occupation_n_est_pas_rendu()
    {
        var employeur = Affilie("0202.239.951", "Boulangerie Dupont");
        Affilie("0403.170.701", "Atelier Durand");
        await Debuter(_personne, employeur.Id, new DateOnly(2025, 1, 1));

        var resultat = await ListerAsync();

        resultat.Value.ShouldHaveSingleItem().AffilieId.ShouldBe(employeur.Id);
    }

    [Fact]
    public async Task L_occupation_d_une_autre_personne_ne_fait_pas_connaitre_son_employeur()
    {
        var autreEmployeur = Affilie("0403.170.701", "Atelier Durand");
        await Debuter(Guid.CreateVersion7(), autreEmployeur.Id, new DateOnly(2025, 1, 1));

        var resultat = await ListerAsync();

        resultat.IsSuccess.ShouldBeTrue();
        resultat.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_occupation_terminee_ou_a_venir_n_est_pas_active()
    {
        var termine = Affilie("0202.239.951", "Boulangerie Dupont");
        var avenir = Affilie("0403.170.701", "Atelier Durand");
        var occupationTerminee = Guid.CreateVersion7();
        await Debuter(_personne, termine.Id, new DateOnly(2024, 1, 1), occupationTerminee);
        await new OccupationTermineeHandler(_occupations, _store)
            .HandleAsync(new OccupationTerminee(occupationTerminee, _personne, termine.Id, Jour.AddDays(-1)), _ct);
        await Debuter(_personne, avenir.Id, Jour.AddDays(1));

        (await ListerAsync()).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Le_dernier_jour_d_occupation_l_employeur_est_encore_rendu()
    {
        var employeur = Affilie("0202.239.951", "Boulangerie Dupont");
        var occupation = Guid.CreateVersion7();
        await Debuter(_personne, employeur.Id, new DateOnly(2024, 1, 1), occupation);
        await new OccupationTermineeHandler(_occupations, _store)
            .HandleAsync(new OccupationTerminee(occupation, _personne, employeur.Id, Jour), _ct);

        (await ListerAsync()).Value.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Deux_occupations_chez_le_meme_affilie_ne_donnent_qu_un_employeur()
    {
        var employeur = Affilie("0202.239.951", "Boulangerie Dupont");
        await Debuter(_personne, employeur.Id, new DateOnly(2024, 1, 1));
        await Debuter(_personne, employeur.Id, new DateOnly(2025, 1, 1));

        (await ListerAsync()).Value.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Une_occupation_pour_un_affilie_inconnu_est_ignoree()
    {
        await Debuter(_personne, Guid.CreateVersion7(), new DateOnly(2025, 1, 1));

        (await ListerAsync()).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sans_claim_personne_id_l_acces_est_refuse_meme_avec_des_occupations()
    {
        var employeur = Affilie("0202.239.951", "Boulangerie Dupont");
        await Debuter(_personne, employeur.Id, new DateOnly(2025, 1, 1));

        var resultat = await ListerAsync(sansClaim: true);

        resultat.IsSuccess.ShouldBeFalse();
        resultat.Error!.Code.ShouldBe("perimetre.interdit");
    }

    [Theory]
    [InlineData(Roles.Employeur)]
    [InlineData(Roles.Sipp)]
    [InlineData(Roles.Planificateur)]
    public async Task Seul_le_travailleur_a_la_permission_dediee(string role)
    {
        var employeur = Affilie("0202.239.951", "Boulangerie Dupont");
        await Debuter(_personne, employeur.Id, new DateOnly(2025, 1, 1));

        var resultat = await ListerAsync(role);

        resultat.IsSuccess.ShouldBeFalse();
        resultat.Error!.Code.ShouldBe("affilie.lecture-interdite");
    }

    [Fact]
    public void Le_travailleur_n_a_toujours_pas_la_lecture_des_fiches()
    {
        RolePermissions.For([Roles.Travailleur]).ShouldNotContain(Permissions.AffilieLire);
        RolePermissions.For([Roles.Travailleur]).ShouldContain(Permissions.AffilieLireEmployeurs);
    }

    [Fact]
    public async Task Une_occupation_rejouee_ou_recue_dans_le_desordre_n_est_projetee_qu_une_fois()
    {
        var employeur = Affilie("0202.239.951", "Boulangerie Dupont");
        var occupation = Guid.CreateVersion7();
        var fin = new OccupationTerminee(occupation, _personne, employeur.Id, Jour.AddDays(30));

        await new OccupationTermineeHandler(_occupations, _store).HandleAsync(fin, _ct);
        await Debuter(_personne, employeur.Id, new DateOnly(2025, 1, 1), occupation);
        await Debuter(_personne, employeur.Id, new DateOnly(2025, 1, 1), occupation);

        _occupations.Occupations.ShouldHaveSingleItem();
        (await ListerAsync()).Value.ShouldHaveSingleItem();
    }
}
