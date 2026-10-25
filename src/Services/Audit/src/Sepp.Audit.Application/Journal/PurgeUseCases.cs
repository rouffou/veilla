using Sepp.Audit.Domain.Journal;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;

namespace Sepp.Audit.Application.Journal;

/// <summary>
/// Purge légale du journal au-delà de la durée de conservation (NF-04, au moins 10 ans). Traitement technique
/// périodique, sans API : il supprime par zone le préfixe échu de la chaîne ; la base scelle la purge. Sans durée
/// explicite, la durée est celle du paramètre <c>AUDIT.CONSERVATION</c> de Référentiels (ARC-21).
/// </summary>
public sealed record PurgerJournal(int? AnneesConservation = null);

public sealed record ResultatPurge(IReadOnlyDictionary<string, int> EntreesPurgeesParZone);

public sealed class PurgerJournalHandler(IJournalAuditRepository journal, ConservationApplicable conservation, TimeProvider timeProvider)
    : ICommandHandler<PurgerJournal, ResultatPurge>
{
    public async Task<Result<ResultatPurge>> HandleAsync(PurgerJournal command, CancellationToken cancellationToken)
    {
        DateTimeOffset limite;
        try
        {
            var maintenant = timeProvider.GetUtcNow();
            var annees = command.AnneesConservation
                ?? await conservation.AnneesAsync(DateOnly.FromDateTime(maintenant.UtcDateTime), cancellationToken);
            limite = PolitiqueConservation.LimitePurge(maintenant, annees);
        }
        catch (DomainException ex)
        {
            return Error.Validation("audit.conservation-invalide", ex.Message);
        }

        var resultat = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var zone in Enum.GetValues<Zone>())
        {
            resultat[zone.Code()] = await journal.PurgerAvantAsync(zone, limite, cancellationToken);
        }

        return new ResultatPurge(resultat);
    }
}
