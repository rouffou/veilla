using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Domain.Calendar;
using Sepp.Contracts.Referentiels;
using Sepp.Referentiels.Domain.Calendrier;

namespace Sepp.Referentiels.Application.Calendrier;

public sealed record JourFerieDto(DateOnly Date, string Code, string Libelle, bool Legal);

public sealed record ListerJoursFeries(int Annee, Language Langue);

public sealed class ListerJoursFeriesHandler(ICalendrierRepository repository)
    : IQueryHandler<ListerJoursFeries, IReadOnlyList<JourFerieDto>>
{
    public async Task<Result<IReadOnlyList<JourFerieDto>>> HandleAsync(ListerJoursFeries query, CancellationToken cancellationToken)
    {
        if (query.Annee is < 1900 or > 2200)
        {
            return Error.Validation("calendrier.annee-invalide", "L'année doit être comprise entre 1900 et 2200.");
        }

        var calendrier = await repository.GetAsync(query.Annee, cancellationToken) ?? CalendrierAnnuel.Creer(query.Annee);
        var legaux = BelgianPublicHolidays.For(query.Annee).Select(h => h.Code).ToHashSet(StringComparer.Ordinal);
        return calendrier.JoursFeries()
            .Select(j => new JourFerieDto(j.Date, j.Code, j.Label.In(query.Langue), legaux.Contains(j.Code)))
            .ToList();
    }
}

public sealed record EcheanceDto(DateOnly Depart, int JoursOuvrables, DateOnly Echeance);

/// <summary>Calcule une échéance en jours ouvrables belges (DAT-08), par ex. examen de reprise à J+10 ouvrables.</summary>
public sealed record CalculerEcheance(DateOnly Depart, int JoursOuvrables);

public sealed class CalculerEcheanceHandler(ICalendrierRepository repository)
    : IQueryHandler<CalculerEcheance, EcheanceDto>
{
    public async Task<Result<EcheanceDto>> HandleAsync(CalculerEcheance query, CancellationToken cancellationToken)
    {
        if (query.JoursOuvrables is < 0 or > 1000)
        {
            return Error.Validation("calendrier.duree-invalide", "Le nombre de jours ouvrables doit être compris entre 0 et 1000.");
        }

        // 1000 jours ouvrables couvrent au plus 5 années calendrier.
        var annees = Enumerable.Range(query.Depart.Year, 6).ToList();
        var calendriers = (await repository.ListAsync(annees[0], annees[^1], cancellationToken)).ToDictionary(c => c.Annee);
        var feries = annees.SelectMany(a => (calendriers.GetValueOrDefault(a) ?? CalendrierAnnuel.Creer(a)).JoursFeries()).Select(j => j.Date);
        var echeance = new BusinessCalendar(feries).AddBusinessDays(query.Depart, query.JoursOuvrables);
        return new EcheanceDto(query.Depart, query.JoursOuvrables, echeance);
    }
}

public sealed record AjouterJourFerie(DateOnly Date, string Code, string LibelleFr, string LibelleNl, string LibelleDe, string? LibelleEn);

public sealed class AjouterJourFerieHandler(
    ICalendrierRepository repository,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    ICurrentUser currentUser) : ICommandHandler<AjouterJourFerie, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AjouterJourFerie command, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.ReferentielsAdministrer))
        {
            return Error.Forbidden("referentiels.interdit", "Seul l'administrateur fonctionnel peut modifier le calendrier.");
        }

        var calendrier = await repository.GetAsync(command.Date.Year, cancellationToken);
        if (calendrier is null)
        {
            calendrier = CalendrierAnnuel.Creer(command.Date.Year);
            repository.Add(calendrier);
        }

        try
        {
            calendrier.AjouterJour(command.Date, command.Code, new LocalizedLabel(command.LibelleFr, command.LibelleNl, command.LibelleDe, command.LibelleEn));
        }
        catch (DomainException ex)
        {
            return Error.Validation("calendrier.jour-invalide", ex.Message);
        }

        outbox.Add(new JoursFeriesModifies(command.Date.Year, calendrier.JoursSupplementaires.Select(j => j.Date).Order().ToList()));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
