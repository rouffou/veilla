using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;

namespace Sepp.Affilies.Application.Securite;

/// <summary>Partie de la fiche touchée par une écriture.</summary>
public enum PartieFiche
{
    /// <summary>Fiche, hiérarchie, résiliation, opérations : réservées au gestionnaire de dossiers.</summary>
    Fiche,

    /// <summary>Contacts (AFF-03) et organes de concertation (AFF-04) : ouverts à l'employeur et au SIPP sur leur affilié.</summary>
    Concertation,
}

/// <summary>
/// §3.3 — Données de l'affilié : lecture pour les internes, lecture / écriture pour le gestionnaire,
/// lecture et écriture <em>partielle</em> pour l'employeur et le SIPP, limitées à leur propre affilié
/// (claim <c>affilie_id</c>, ADR 0005 : le périmètre est évalué par le service propriétaire).
/// </summary>
public sealed class ControleAcces(ICurrentUser utilisateur, IPerimetreUtilisateur perimetre)
{
    private static readonly HashSet<string> RolesExternes = new(StringComparer.Ordinal)
    {
        Roles.Employeur, Roles.Sipp, Roles.Travailleur,
    };

    /// <summary>
    /// Affiliés visibles : <c>null</c> pour un interne (tous), sinon ceux du jeton (ensemble vide si aucun).
    /// </summary>
    public IReadOnlySet<Guid>? PerimetreLecture =>
        ParRoleInterne(Permissions.AffilieLire) ? null : perimetre.AffiliesAutorises;

    public Error? VerifierPermissionLecture() =>
        utilisateur.HasPermission(Permissions.AffilieLire)
            ? null
            : Error.Forbidden("affilie.lecture-interdite", "Vous n'avez pas accès aux données des affiliés.");

    public Error? VerifierLecture(Guid affilieId)
    {
        if (VerifierPermissionLecture() is { } refus)
        {
            return refus;
        }

        return ParRoleInterne(Permissions.AffilieLire) || perimetre.AffiliesAutorises.Contains(affilieId)
            ? null
            : Error.Forbidden("affilie.hors-perimetre", "Cet affilié n'est pas dans votre périmètre.");
    }

    public Error? VerifierEcriture(Guid affilieId, PartieFiche partie)
    {
        if (!utilisateur.HasPermission(Permissions.AffilieEcrire))
        {
            return Error.Forbidden("affilie.ecriture-interdite", "Vous ne pouvez pas modifier les données des affiliés.");
        }

        if (ParRoleInterne(Permissions.AffilieEcrire))
        {
            return null;
        }

        // Écriture partielle des externes (§3.3) : contacts et organes de concertation de leur affilié.
        if (partie != PartieFiche.Concertation)
        {
            return Error.Forbidden("affilie.reserve-gestionnaire", "Cette partie de la fiche est gérée par le gestionnaire de dossiers du SEPP.");
        }

        return perimetre.AffiliesAutorises.Contains(affilieId)
            ? null
            : Error.Forbidden("affilie.hors-perimetre", "Cet affilié n'est pas dans votre périmètre.");
    }

    /// <summary>Création d'affiliés et gestion des groupes : gestionnaire de dossiers uniquement.</summary>
    public Error? VerifierGestion() =>
        ParRoleInterne(Permissions.AffilieEcrire)
            ? null
            : Error.Forbidden("affilie.reserve-gestionnaire", "Réservé au gestionnaire de dossiers du SEPP.");

    /// <summary>La permission est-elle accordée par un rôle interne (sans restriction de périmètre) ?</summary>
    private bool ParRoleInterne(string permission) =>
        utilisateur.HasPermission(permission) &&
        RolePermissions.For(utilisateur.Roles.Where(r => !RolesExternes.Contains(r))).Contains(permission);
}
