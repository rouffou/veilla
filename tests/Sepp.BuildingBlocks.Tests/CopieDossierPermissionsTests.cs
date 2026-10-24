using Sepp.BuildingBlocks.Application.Security;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>Droit d'accès du travailleur à son dossier de santé (§3.3 « copie sur demande », SAN-43) et carnet de vaccination (SAN-50).</summary>
public class CopieDossierPermissionsTests
{
    private static readonly string[] TousLesRoles =
    [
        Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier, Roles.AssistantMedical, Roles.ConseillerSecurite, Roles.ConseillerErgonome,
        Roles.ConseillerHygieniste, Roles.Cpap, Roles.CpapDirigeant, Roles.GestionnaireDossiers, Roles.Planificateur,
        Roles.ResponsableCentre, Roles.Direction, Roles.AdministrateurFonctionnel, Roles.Dpo, Roles.Employeur, Roles.Sipp,
        Roles.Travailleur, Roles.Integrations, Roles.Documents, Roles.Communications, Roles.Affilies,
    ];

    private static string[] RolesAvec(string permission) =>
        [.. TousLesRoles.Where(r => RolePermissions.For([r]).Contains(permission))];

    [Fact]
    public void Seul_le_travailleur_depose_une_demande_de_copie() =>
        RolesAvec(Permissions.CopieDossierDemander).ShouldBe([Roles.Travailleur]);

    [Fact]
    public void Seul_le_cpmt_traite_une_demande_de_copie() =>
        RolesAvec(Permissions.CopieDossierTraiter).ShouldBe([Roles.Cpmt, Roles.CpmtDirigeant], ignoreOrder: true);

    [Fact]
    public void Seul_le_compte_technique_de_documents_recoit_le_contenu_a_publier() =>
        RolesAvec(Permissions.DossierSanteTransmettreDocuments).ShouldBe([Roles.Documents]);

    [Fact]
    public void Ni_le_travailleur_ni_le_compte_technique_de_documents_ne_lisent_le_dossier()
    {
        foreach (var role in new[] { Roles.Travailleur, Roles.Documents })
        {
            RolePermissions.For([role]).ShouldNotContain(Permissions.DossierSanteLire);
            RolePermissions.For([role]).ShouldNotContain(Permissions.DossierSanteEcrire);
        }
    }
}
