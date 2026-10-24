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
        Roles.Travailleur, Roles.Integrations, Roles.Documents, Roles.DocumentsMedicale, Roles.Communications, Roles.Affilies,
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
    public void Seul_le_compte_technique_de_documents_medicale_recoit_le_contenu_a_publier() =>
        RolesAvec(Permissions.DossierSanteTransmettreDocuments).ShouldBe([Roles.DocumentsMedicale]);

    [Fact]
    public void Le_compte_technique_de_documents_standard_ne_recoit_aucun_contenu_clinique()
    {
        // Décision #356, ARC-06 : l'instance standard n'a plus dossier-sante:transmettre-documents.
        RolePermissions.For([Roles.Documents]).ShouldNotContain(Permissions.DossierSanteTransmettreDocuments);
        RolePermissions.For([Roles.Documents]).ShouldBe([Permissions.AffilieLire, Permissions.PersonneLire], ignoreOrder: true);
    }

    [Fact]
    public void Le_compte_technique_de_documents_medicale_n_a_que_le_droit_de_transmission()
    {
        // Moindre privilège : ni lecture du dossier, ni accès à Affiliés ni à Personnes (interdits par le pare-feu de la zone).
        RolePermissions.For([Roles.DocumentsMedicale]).ShouldBe([Permissions.DossierSanteTransmettreDocuments]);
    }

    [Fact]
    public void Ni_le_travailleur_ni_le_compte_technique_de_documents_ne_lisent_le_dossier()
    {
        foreach (var role in new[] { Roles.Travailleur, Roles.Documents, Roles.DocumentsMedicale })
        {
            RolePermissions.For([role]).ShouldNotContain(Permissions.DossierSanteLire);
            RolePermissions.For([role]).ShouldNotContain(Permissions.DossierSanteEcrire);
        }
    }
}
