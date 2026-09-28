using Sepp.Audit.Domain.Journal;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;

namespace Sepp.Audit.Application.Journal;

/// <summary>
/// Purge légale du journal au-delà de la durée de conservation (NF-04, au moins 10 ans). Traitement technique
/// périodique, sans API : il supprime par zone le préfixe échu de la chaîne ; la base scelle la purge.
/// </summary>
public sealed record PurgerJournal(int AnneesConservation);

public sealed record ResultatPurge(IReadOnlyDictionary<string, int> EntreesPurgeesParZone);

public sealed class PurgerJournalHandler(IJournalAuditRepository journal, TimeProvider timeProvider)
    : ICommandHandler<PurgerJournal, ResultatPurge>
{
    public async Task<Result<ResultatPurge>> HandleAsync(PurgerJournal command, CancellationToken cancellationToken)
    {
        DateTimeOffset limite;
        try
        {
            limite = PolitiqueConservation.LimitePurge(timeProvider.GetUtcNow(), command.AnneesConservation);
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
