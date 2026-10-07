using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.Planification.Application;

namespace Sepp.Planification.Adapters.Securite;

/// <summary>
/// Périmètre de l'utilisateur issu du jeton (ADR 0005, SAN-12), vérifié par le service avant toute réservation en ligne :
/// <list type="bullet">
/// <item><b>Employeur</b> (rôles <c>employeur</c>, <c>sipp</c>) : affiliés du claim multivalué <c>affilie_id</c> (claim répété,
/// tableau JSON ou valeurs séparées par des virgules) ;</item>
/// <item><b>Travailleur</b> (rôle <c>travailleur</c>) : la personne qu'il est, claim <c>personne_id</c> (UUID du service
/// Personnes), émis par le fournisseur d'identité lors du rapprochement du compte avec la fiche — <b>à confirmer avec
/// l'analyse du fournisseur d'identité</b> (voir README du service) ; sans ce claim, un travailleur ne peut rien réserver.</item>
/// </list>
/// Un utilisateur interne (planificateur, assistant…) n'est pas borné ici : ses droits viennent de la matrice des permissions.
/// </summary>
public sealed class PerimetreHttp(IHttpContextAccessor accessor) : IPerimetreUtilisateur
{
    public const string ClaimAffilie = "affilie_id";
    public const string ClaimPersonne = "personne_id";

    private static readonly string[] RolesExternes = [Roles.Employeur, Roles.Sipp, Roles.Travailleur];

    private ClaimsPrincipal? Utilisateur => accessor.HttpContext?.User;

    public bool EstExterne =>
        Utilisateur is not { } u || u.FindAll("roles").Select(c => c.Value).All(r => RolesExternes.Contains(r, StringComparer.Ordinal));

    public Guid? PersonneId =>
        Utilisateur?.FindFirst(ClaimPersonne)?.Value is { } valeur && Guid.TryParse(valeur.Trim(), out var id) && id != Guid.Empty ? id : null;

    public bool PeutAccederAffilie(Guid affilieId) => !EstExterne || Affilies().Contains(affilieId);

    private HashSet<Guid> Affilies() =>
        (Utilisateur?.FindAll(ClaimAffilie) ?? [])
            .SelectMany(c => c.Value.Split([',', '[', ']', '"', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(v => Guid.TryParse(v, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
}
