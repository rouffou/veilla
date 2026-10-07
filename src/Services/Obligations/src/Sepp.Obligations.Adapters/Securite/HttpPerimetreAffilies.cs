using Microsoft.AspNetCore.Http;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.Obligations.Application;

namespace Sepp.Obligations.Adapters.Securite;

/// <summary>
/// Périmètre de l'utilisateur issu du jeton (ADR 0005 : règle de périmètre évaluée par le service propriétaire).
/// Un utilisateur sans aucun rôle interne est externe (employeur, SIPP) : il n'accède qu'aux affiliés listés
/// dans la revendication <c>affilie_id</c> (répétée, ou valeurs séparées par des virgules), alimentée par la
/// gestion des accès de la sécurité sociale via CSAM (POR-01).
/// </summary>
internal sealed class HttpPerimetreAffilies(IHttpContextAccessor accessor, ICurrentUser currentUser) : IPerimetreAffilies
{
    public const string ClaimAffilie = "affilie_id";

    private static readonly HashSet<string> RolesExternes = new(StringComparer.Ordinal) { Roles.Employeur, Roles.Sipp, Roles.Travailleur };

    public bool EstExterne => !currentUser.Roles.Any(r => !RolesExternes.Contains(r));

    public bool PeutAcceder(Guid affilieId) => !EstExterne || AffiliesAutorises().Contains(affilieId);

    private HashSet<Guid> AffiliesAutorises() =>
        (accessor.HttpContext?.User.FindAll(ClaimAffilie) ?? [])
            .SelectMany(c => c.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(v => Guid.TryParse(v, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
}
