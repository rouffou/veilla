using Sepp.Audit.Domain.Journal;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;

namespace Sepp.Audit.Application.Journal;

public sealed record EntreeAuditDto(
    Guid Id,
    string Zone,
    long Numero,
    DateTimeOffset Horodatage,
    string UtilisateurId,
    string Role,
    string Service,
    string Action,
    string ObjetType,
    Guid ObjetId,
    string? Motif,
    bool BrisDeGlace,
    string EmpreintePrecedente,
    string Empreinte);

public sealed record PageDto<T>(IReadOnlyList<T> Elements, int Page, int Taille, int Total);

internal static class JournalMapping
{
    public static EntreeAuditDto ToDto(this EntreeAudit e) => new(
        e.Id, e.Zone.Code(), e.Numero, e.Horodatage, e.UtilisateurId, e.Role, e.Service, e.Action.Code(),
        e.ObjetType, e.ObjetId, e.Motif, e.BrisDeGlace, e.EmpreintePrecedente, e.Empreinte);
}

/// <summary>
/// Périmètre de consultation du journal (§3.3, PSY-20) : <see cref="Permissions.AuditLire"/> puis une permission
/// par zone. Le DPO voit toutes les zones, le CPMT dirigeant la zone médicale, le CPAP dirigeant la zone psychosociale.
/// </summary>
public static class PerimetreAudit
{
    private static readonly (Zone Zone, string Permission)[] ParZone =
    [
        (Zone.Standard, Permissions.AuditZoneStandard),
        (Zone.Medicale, Permissions.AuditZoneMedicale),
        (Zone.Psychosociale, Permissions.AuditZonePsychosociale),
    ];

    public static IReadOnlyList<Zone> ZonesVisibles(ICurrentUser user) =>
        user.HasPermission(Permissions.AuditLire)
            ? ParZone.Where(z => user.HasPermission(z.Permission)).Select(z => z.Zone).ToList()
            : [];

    /// <summary>Zones à interroger : la zone demandée si elle est visible, sinon toutes les zones visibles.</summary>
    public static Result<IReadOnlyList<Zone>> Resoudre(ICurrentUser user, string? zoneDemandee)
    {
        var visibles = ZonesVisibles(user);
        if (visibles.Count == 0)
        {
            return Error.Forbidden("audit.interdit", "La consultation du journal d'audit est réservée au DPO et aux conseillers dirigeants.");
        }

        if (string.IsNullOrWhiteSpace(zoneDemandee))
        {
            return Result<IReadOnlyList<Zone>>.Success(visibles);
        }

        if (!CodesAudit.TryParse<Zone>(zoneDemandee, out var zone))
        {
            return Error.Validation("audit.zone-inconnue", $"Zone inconnue : '{zoneDemandee}' (standard, medicale, psychosociale).");
        }

        return visibles.Contains(zone)
            ? Result<IReadOnlyList<Zone>>.Success([zone])
            : Error.Forbidden("audit.zone-interdite", $"Le journal de la zone {zone.Code()} ne vous est pas accessible (PSY-20).");
    }
}

/// <summary>Recherche dans le journal (NF-04) : par utilisateur, objet, période, zone ; les plus récentes d'abord.</summary>
public sealed record RechercherEntrees(
    string? Zone = null,
    string? UtilisateurId = null,
    string? ObjetType = null,
    Guid? ObjetId = null,
    DateTimeOffset? Du = null,
    DateTimeOffset? Au = null,
    bool? BrisDeGlace = null,
    int Page = 1,
    int Taille = 50);

public sealed class RechercherEntreesHandler(IJournalAuditRepository journal, ICurrentUser currentUser)
    : IQueryHandler<RechercherEntrees, PageDto<EntreeAuditDto>>
{
    public const int TailleMaximale = 200;

    public async Task<Result<PageDto<EntreeAuditDto>>> HandleAsync(RechercherEntrees query, CancellationToken cancellationToken)
    {
        var zones = PerimetreAudit.Resoudre(currentUser, query.Zone);
        if (!zones.IsSuccess)
        {
            return zones.Error!;
        }

        if (query.Page < 1 || query.Taille is < 1 or > TailleMaximale)
        {
            return Error.Validation("audit.pagination-invalide", $"Page à partir de 1, taille de 1 à {TailleMaximale}.");
        }

        if (query.Du is { } du && query.Au is { } au && au < du)
        {
            return Error.Validation("audit.periode-invalide", "La fin de la période précède son début.");
        }

        var page = await journal.RechercherAsync(
            new CritereRecherche(zones.Value, Normaliser(query.UtilisateurId), Normaliser(query.ObjetType), query.ObjetId,
                query.Du, query.Au, query.BrisDeGlace, query.Page, query.Taille),
            cancellationToken);
        return new PageDto<EntreeAuditDto>(page.Entrees.Select(e => e.ToDto()).ToList(), query.Page, query.Taille, page.Total);
    }

    private static string? Normaliser(string? valeur) => string.IsNullOrWhiteSpace(valeur) ? null : valeur.Trim();
}

public sealed record ObtenirEntree(Guid Id);

public sealed class ObtenirEntreeHandler(IJournalAuditRepository journal, ICurrentUser currentUser)
    : IQueryHandler<ObtenirEntree, EntreeAuditDto>
{
    public async Task<Result<EntreeAuditDto>> HandleAsync(ObtenirEntree query, CancellationToken cancellationToken)
    {
        var zones = PerimetreAudit.Resoudre(currentUser, null);
        if (!zones.IsSuccess)
        {
            return zones.Error!;
        }

        // Une entrée hors périmètre est présentée comme inexistante : son existence même n'est pas divulguée.
        var entree = await journal.ObtenirAsync(query.Id, cancellationToken);
        return entree is not null && zones.Value.Contains(entree.Zone)
            ? entree.ToDto()
            : Error.NotFound("audit.entree-inconnue", $"Entrée d'audit {query.Id} inconnue.");
    }
}

public sealed record AnomalieDto(long Numero, string Type, string Message);

public sealed record IntegriteDto(
    string Zone,
    bool Integre,
    long EntreesVerifiees,
    long NumeroDepart,
    long DernierNumero,
    string DerniereEmpreinte,
    AnomalieDto? Anomalie);

/// <summary>
/// Vérifie l'intégrité de la chaîne d'une zone (NF-04) : chaque ligne est recalculée et rattachée à la précédente.
/// Le dernier maillon renvoyé peut être consigné hors du système (ancrage) pour détecter une troncature de fin de chaîne.
/// </summary>
public sealed record VerifierIntegrite(string Zone);

public sealed class VerifierIntegriteHandler(IJournalAuditRepository journal, ICurrentUser currentUser)
    : IQueryHandler<VerifierIntegrite, IntegriteDto>
{
    public async Task<Result<IntegriteDto>> HandleAsync(VerifierIntegrite query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Zone))
        {
            return Error.Validation("audit.zone-obligatoire", "La zone à vérifier est obligatoire.");
        }

        var zones = PerimetreAudit.Resoudre(currentUser, query.Zone);
        if (!zones.IsSuccess)
        {
            return zones.Error!;
        }

        var zone = zones.Value.Single();
        var depart = (await journal.DernierSceauAsync(zone, cancellationToken))?.Maillon ?? MaillonChaine.Origine;
        var verification = new VerificationChaine(zone, depart);
        await foreach (var entree in journal.ParcourirChaineAsync(zone, depart.Numero, cancellationToken))
        {
            if (!verification.Verifier(entree))
            {
                break;
            }
        }

        return new IntegriteDto(
            zone.Code(),
            verification.EstIntegre,
            verification.EntreesVerifiees,
            depart.Numero,
            verification.Dernier.Numero,
            verification.Dernier.Empreinte,
            verification.Anomalie is { } a ? new AnomalieDto(a.Numero, a.Type.ToString(), a.Message) : null);
    }
}
