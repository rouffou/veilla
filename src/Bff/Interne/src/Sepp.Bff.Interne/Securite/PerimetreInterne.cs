using System.Security.Claims;

using Sepp.BuildingBlocks.Application.Security;

namespace Sepp.Bff.Interne.Securite;

/// <summary>
/// Canal de l'application interne (§3.1, ARC-43, ADR 0013) : seuls les profils internes du SEPP l'utilisent. Les profils
/// externes (employeur, SIPP, travailleur) ont leur propre BFF et reçoivent 403 ici ; les rôles techniques des comptes de
/// service (integrations, documents, communications…) et le rôle d'exploitation n'y ont pas accès non plus. Le BFF ne fait
/// que ce premier filtre : chaque service revalide le jeton et applique sa matrice de permissions (défense en profondeur).
/// </summary>
public static class PerimetreInterne
{
    public const string Politique = "application-interne";

    /// <summary>Profils internes du §3.1 (liste fermée, en miroir des rôles qui lisent les référentiels dans <see cref="RolePermissions"/>).</summary>
    public static readonly string[] RolesInternes =
    [
        Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier, Roles.AssistantMedical, Roles.ConseillerSecurite,
        Roles.ConseillerErgonome, Roles.ConseillerHygieniste, Roles.Cpap, Roles.CpapDirigeant, Roles.GestionnaireDossiers,
        Roles.Planificateur, Roles.ResponsableCentre, Roles.Direction, Roles.AdministrateurFonctionnel, Roles.Dpo,
    ];

    /// <summary>Profils externes, servis par les BFF employeur et travailleur.</summary>
    public static readonly string[] RolesExternes = [Roles.Employeur, Roles.Sipp, Roles.Travailleur];

    /// <summary>
    /// Au moins un rôle interne et aucun rôle externe : un compte mal configuré qui cumulerait les deux est refusé plutôt
    /// que d'ouvrir un canal à un utilisateur externe.
    /// </summary>
    public static bool EstInterne(ClaimsPrincipal utilisateur)
    {
        ArgumentNullException.ThrowIfNull(utilisateur);
        var roles = utilisateur.FindAll("roles").Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        return roles.Overlaps(RolesInternes) && !roles.Overlaps(RolesExternes);
    }
}
