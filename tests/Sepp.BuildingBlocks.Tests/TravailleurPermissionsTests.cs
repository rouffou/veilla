using Sepp.BuildingBlocks.Application.Security;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>Permissions du portail travailleur (POR-11, POR-12) : dédiées, limitées au rôle travailleur (§3.3).</summary>
public class TravailleurPermissionsTests
{
    private static readonly string[] TousLesRoles =
    [
        Roles.Employeur, Roles.Sipp, Roles.GestionnaireDossiers, Roles.Planificateur, Roles.Cpmt, Roles.CpmtDirigeant,
        Roles.Infirmier, Roles.AssistantMedical, Roles.Travailleur, Roles.Cpap, Roles.CpapDirigeant, Roles.ResponsableCentre,
        Roles.AdministrateurFonctionnel, Roles.Dpo, Roles.ConseillerSecurite,
    ];

    private static string[] RolesAvec(string permission) =>
        [.. TousLesRoles.Where(r => RolePermissions.For([r]).Contains(permission))];

    [Theory]
    [InlineData(Permissions.ObligationDemander)]
    [InlineData(Permissions.ObligationLirePropres)]
    [InlineData(Permissions.PersonneLirePropres)]
    [InlineData(Permissions.AffilieLireEmployeurs)]
    public void Les_permissions_du_portail_sont_reservees_au_travailleur(string permission) =>
        RolesAvec(permission).ShouldBe([Roles.Travailleur]);

    [Fact]
    public void Le_travailleur_n_a_aucune_permission_de_gestion_ni_de_lecture_des_obligations_et_des_fiches()
    {
        var permissions = RolePermissions.For([Roles.Travailleur]);

        permissions.ShouldNotContain(Permissions.ObligationGerer);
        permissions.ShouldNotContain(Permissions.ObligationLire);
        permissions.ShouldNotContain(Permissions.PersonneLire);
        permissions.ShouldNotContain(Permissions.PersonneEcrire);
        permissions.ShouldNotContain(Permissions.AffilieLire);
        permissions.ShouldNotContain(Permissions.AffilieEcrire);
    }

    [Fact]
    public void Les_profils_internes_gardent_obligation_gerer()
    {
        RolesAvec(Permissions.ObligationGerer).ShouldBe(
            [Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier, Roles.AssistantMedical, Roles.Planificateur, Roles.GestionnaireDossiers],
            ignoreOrder: true);
    }
}
