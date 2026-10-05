using Microsoft.AspNetCore.Http;

using Sepp.Affilies.Application;

namespace Sepp.Affilies.Adapters.Securite;

/// <summary>
/// Périmètre de l'utilisateur externe lu dans le jeton OIDC (ADR 0005).
/// <para>
/// Claim <c>affilie_id</c> : identifiant (UUID) de l'affilié que représente l'employeur ou le conseiller en prévention
/// interne (SIPP). Une valeur par affilié représenté (claim répété ou tableau JSON). Il est émis par le fournisseur
/// d'identité (Keycloak en local : attribut utilisateur <c>affilie_id</c> multivalué exposé par un « User Attribute
/// mapper » ; Entra ID / CSAM en production : revendication ajoutée par le BFF employeur à partir du mandat).
/// Les valeurs qui ne sont pas des UUID sont ignorées. Sans ce claim, un employeur ou un SIPP n'accède à aucun affilié.
/// Les utilisateurs internes n'en ont pas besoin : leur accès n'est pas restreint par affilié.
/// </para>
/// </summary>
public sealed class PerimetreJeton(IHttpContextAccessor accessor) : IPerimetreUtilisateur
{
    public const string ClaimAffilie = "affilie_id";

    public IReadOnlySet<Guid> AffiliesAutorises =>
        accessor.HttpContext?.User.FindAll(ClaimAffilie)
            .Select(c => Guid.TryParse(c.Value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet() ?? [];
}
