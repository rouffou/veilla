using Sepp.BuildingBlocks.Application.Security;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>Permissions du service Réintégration (§3.3, zone médicale) ; répartition à valider.</summary>
public class ReintegrationPermissionsTests
{
    private static readonly string[] TousLesRoles =
    [
        Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier, Roles.AssistantMedical, Roles.ConseillerSecurite, Roles.ConseillerErgonome,
        Roles.ConseillerHygieniste, Roles.Cpap, Roles.CpapDirigeant, Roles.GestionnaireDossiers, Roles.Planificateur, Roles.ResponsableCentre,
        Roles.Direction, Roles.AdministrateurFonctionnel, Roles.Dpo, Roles.Employeur, Roles.Sipp, Roles.Travailleur, Roles.Integrations,
        Roles.Documents, Roles.Communications, Roles.Affilies,
    ];

    private static string[] RolesAvec(string permission) =>
        [.. TousLesRoles.Where(r => RolePermissions.For([r]).Contains(permission))];

    [Fact]
    public void Le_dossier_de_reintegration_est_reserve_au_cpmt_et_a_l_infirmier()
    {
        RolesAvec(Permissions.ReintegrationLire).ShouldBe([Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier], ignoreOrder: true);
        RolesAvec(Permissions.ReintegrationEcrire).ShouldBe([Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier], ignoreOrder: true);
    }

    [Fact]
    public void L_evaluation_de_reintegration_est_reservee_au_cpmt() =>
        RolesAvec(Permissions.ReintegrationEvaluer).ShouldBe([Roles.Cpmt, Roles.CpmtDirigeant], ignoreOrder: true);

    [Fact]
    public void Le_suivi_sans_donnee_medicale_et_le_depot_du_plan_vont_a_l_employeur_au_sipp_et_au_gestionnaire()
    {
        RolesAvec(Permissions.ReintegrationSuiviLire).ShouldBe([Roles.Employeur, Roles.Sipp, Roles.GestionnaireDossiers], ignoreOrder: true);
        RolesAvec(Permissions.ReintegrationPlanDeposer).ShouldBe([Roles.Employeur, Roles.Sipp, Roles.GestionnaireDossiers], ignoreOrder: true);
    }

    [Fact]
    public void Seul_le_travailleur_repond_lui_meme_au_plan() =>
        RolesAvec(Permissions.ReintegrationReponseEnregistrer).ShouldBe([Roles.Travailleur]);

    [Fact]
    public void Le_compte_technique_integrations_identifie_une_personne_sans_lire_sa_fiche()
    {
        RolesAvec(Permissions.PersonneIdentifier).ShouldBe([Roles.Integrations]);
        RolePermissions.For([Roles.Integrations]).ShouldNotContain(Permissions.PersonneLire);
    }
}
