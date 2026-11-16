using Sepp.BuildingBlocks.Application.Security;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>Permission technique d'alimentation de Personnes (DIMONA, registre national), réservée à Intégrations (#491).</summary>
public class PersonneAlimenterTests
{
    private static readonly string[] TousLesRoles =
    [
        Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier, Roles.AssistantMedical, Roles.ConseillerSecurite, Roles.ConseillerErgonome,
        Roles.ConseillerHygieniste, Roles.Cpap, Roles.CpapDirigeant, Roles.GestionnaireDossiers, Roles.Planificateur, Roles.ResponsableCentre,
        Roles.Direction, Roles.AdministrateurFonctionnel, Roles.Dpo, Roles.Employeur, Roles.Sipp, Roles.Travailleur, Roles.Integrations,
        Roles.Documents, Roles.DocumentsMedicale, Roles.Communications, Roles.Affilies,
    ];

    [Fact]
    public void Seul_le_compte_technique_integrations_alimente_personnes() =>
        TousLesRoles.Where(r => RolePermissions.For([r]).Contains(Permissions.PersonneAlimenter)).ShouldBe([Roles.Integrations]);

    [Fact]
    public void Le_compte_integrations_n_a_plus_d_ecriture_generique_sur_les_personnes() =>
        RolePermissions.For([Roles.Integrations]).ShouldNotContain(Permissions.PersonneEcrire);
}
