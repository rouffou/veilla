using Sepp.BuildingBlocks.Application.Security;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>Permissions de la saga « examen de reprise » (POR-04) ; répartition à valider (§3.3).</summary>
public class ReprisePermissionsTests
{
    private static readonly string[] TousLesRoles =
    [
        Roles.Employeur, Roles.Sipp, Roles.GestionnaireDossiers, Roles.Planificateur, Roles.Cpmt, Roles.CpmtDirigeant,
        Roles.Infirmier, Roles.AssistantMedical, Roles.Travailleur, Roles.Cpap, Roles.CpapDirigeant, Roles.ResponsableCentre,
        Roles.AdministrateurFonctionnel, Roles.Dpo, Roles.ConseillerSecurite,
    ];

    private static string[] RolesAvec(string permission) =>
        [.. TousLesRoles.Where(r => RolePermissions.For([r]).Contains(permission))];

    [Fact]
    public void Annoncer_une_reprise_est_reserve_a_l_employeur_au_sipp_au_gestionnaire_et_au_planificateur() =>
        RolesAvec(Permissions.RepriseAnnoncer).ShouldBe(
            [Roles.Employeur, Roles.Sipp, Roles.GestionnaireDossiers, Roles.Planificateur], ignoreOrder: true);

    [Fact]
    public void Lire_le_suivi_s_ouvre_aussi_au_cpmt_a_l_infirmier_et_a_l_assistant_medical() =>
        RolesAvec(Permissions.RepriseLire).ShouldBe(
            [
                Roles.Employeur, Roles.Sipp, Roles.GestionnaireDossiers, Roles.Planificateur,
                Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier, Roles.AssistantMedical,
            ],
            ignoreOrder: true);

    [Fact]
    public void Gerer_une_reprise_est_reserve_au_gestionnaire_et_au_planificateur() =>
        RolesAvec(Permissions.RepriseGerer).ShouldBe([Roles.GestionnaireDossiers, Roles.Planificateur], ignoreOrder: true);
}
