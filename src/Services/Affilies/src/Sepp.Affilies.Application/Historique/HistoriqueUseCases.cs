using System.Text.Json;

using Sepp.Affilies.Application.Securite;
using Sepp.BuildingBlocks.Application;

namespace Sepp.Affilies.Application.Historique;

/// <summary>Entrée d'historique : qui, quand, quelle version, quelle action, valeurs avant / après (AFF-05).</summary>
public sealed record ModificationDto(int Version, DateTimeOffset Horodatage, string Auteur, string Action, JsonElement? Avant, JsonElement? Apres);

public sealed record ConsulterHistorique(Guid AffilieId);

public sealed class ConsulterHistoriqueHandler(IAffilieRepository affilies, IHistoriqueAffilieRepository historique, ControleAcces acces)
    : IQueryHandler<ConsulterHistorique, IReadOnlyList<ModificationDto>>
{
    public async Task<Result<IReadOnlyList<ModificationDto>>> HandleAsync(ConsulterHistorique query, CancellationToken cancellationToken)
    {
        if (acces.VerifierLecture(query.AffilieId) is { } refus)
        {
            return refus;
        }

        if (!await affilies.ExisteAsync(query.AffilieId, cancellationToken))
        {
            return Error.NotFound("affilie.inconnu", $"Affilié {query.AffilieId} inconnu.");
        }

        var modifications = await historique.ListAsync(query.AffilieId, cancellationToken);
        return modifications
            .OrderBy(m => m.NumeroVersion)
            .Select(m => new ModificationDto(m.NumeroVersion, m.Horodatage, m.Auteur, m.Action, Json(m.Avant), Json(m.Apres)))
            .ToList();
    }

    private static JsonElement? Json(string? valeur)
    {
        if (valeur is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(valeur);
        return document.RootElement.Clone();
    }
}
