using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Planification;
using Sepp.Planification.Application.PriseRendezVous;
using Sepp.Planification.Domain;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Convocations;

namespace Sepp.Planification.Application.Convocations;

public sealed record ConvocationDto(
    Guid Id,
    Guid RendezVousId,
    CanalConvocation Canal,
    bool Recommande,
    TypeConvocation Type,
    Guid? LotId,
    DateTimeOffset DateEmission,
    DateTimeOffset? DateEnvoi,
    string? MessageId);

public sealed record LotConvocationsDto(Guid LotId, int Emises, IReadOnlyList<Guid> Ignores);

/// <summary>
/// SAN-10 : convocation par lot des rendez-vous planifiés sans convocation (par exemple après la planification d'une
/// session sans convocation immédiate). Les convocations partagent un identifiant de lot.
/// </summary>
public sealed record ConvoquerParLot(IReadOnlyList<Guid> RendezVousIds, string? Canal);

public sealed class ConvoquerParLotHandler(
    IRendezVousRepository rendezVous,
    IConvocationRepository convocations,
    PriseDeRendezVous prise,
    IUnitOfWork unitOfWork,
    ICurrentUser user) : ICommandHandler<ConvoquerParLot, LotConvocationsDto>
{
    public async Task<Result<LotConvocationsDto>> HandleAsync(ConvoquerParLot command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationGerer) is { } interdit)
        {
            return interdit;
        }

        if (command.RendezVousIds is not { Count: > 0 and <= 1000 })
        {
            return Error.Validation("convocation.lot-invalide", "Un lot comporte de 1 à 1000 rendez-vous.");
        }

        var canal = Enumerations.Optionnel<CanalConvocation>(command.Canal, "Canal");
        if (!canal.IsSuccess)
        {
            return canal.Error!;
        }

        var lotId = Guid.CreateVersion7();
        var emises = 0;
        var ignores = new List<Guid>();
        foreach (var id in command.RendezVousIds.Distinct())
        {
            var rdv = await rendezVous.GetAsync(id, cancellationToken);
            if (rdv is not { Statut: StatutRendezVous.Planifie } || (await convocations.ListAsync(id, cancellationToken)).Count > 0)
            {
                ignores.Add(id);
                continue;
            }

            await prise.ConvoquerAsync(rdv, canal.Value, null, TypeConvocation.Convocation, lotId, cancellationToken);
            emises++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new LotConvocationsDto(lotId, emises, ignores);
    }
}

public sealed record ListerConvocations(Guid RendezVousId);

public sealed class ListerConvocationsHandler(IConvocationRepository convocations, ICurrentUser user) : IQueryHandler<ListerConvocations, IReadOnlyList<ConvocationDto>>
{
    public async Task<Result<IReadOnlyList<ConvocationDto>>> HandleAsync(ListerConvocations query, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationLire) is { } interdit)
        {
            return interdit;
        }

        return (await convocations.ListAsync(query.RendezVousId, cancellationToken))
            .OrderBy(c => c.DateEmission)
            .Select(c => new ConvocationDto(c.Id, c.RendezVousId, c.Canal, c.Recommande, c.Type, c.LotId, c.DateEmission, c.DateEnvoi, c.MessageId))
            .ToList();
    }
}

/// <summary>SAN-10 : canal préféré d'un affilié, défini par le planificateur ou par l'employeur pour ses propres affiliés.</summary>
public sealed record DefinirPreferenceConvocation(Guid AffilieId, string Canal);

public sealed class DefinirPreferenceConvocationHandler(
    IPreferenceConvocationRepository preferences,
    IPerimetreUtilisateur perimetre,
    IUnitOfWork unitOfWork,
    ICurrentUser user) : ICommandHandler<DefinirPreferenceConvocation, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DefinirPreferenceConvocation command, CancellationToken cancellationToken)
    {
        var employeur = user.HasPermission(Permissions.PlanificationReserver) && perimetre.PersonneId is null && perimetre.PeutAccederAffilie(command.AffilieId);
        if (!user.HasPermission(Permissions.PlanificationGerer) && !employeur)
        {
            return Error.Forbidden("planification.interdit", "Vous n'avez pas le droit de définir le canal de cet affilié.");
        }

        if (!Enumerations.TryParse<CanalConvocation>(command.Canal, out var canal))
        {
            return Error.Validation("convocation.canal-inconnu", $"Canal inconnu : '{command.Canal}'.");
        }

        var preference = await preferences.GetAsync(command.AffilieId, cancellationToken);
        if (preference is null)
        {
            preferences.Add(PreferenceConvocation.Creer(command.AffilieId, canal));
        }
        else
        {
            preference.Modifier(canal);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record RappelsDto(DateOnly Date, int PremiersRappels, int SecondsRappels);

/// <summary>
/// SAN-13 : rappels automatiques J-7 et J-1 (paramètres CONVOCATION.RAPPEL_1 et CONVOCATION.RAPPEL_2, jours calendrier).
/// Idempotent : un rappel émis est noté sur le rendez-vous et n'est jamais réémis ; un rappel manqué (service arrêté) est
/// rattrapé tant que le rendez-vous n'a pas eu lieu. Exécuté chaque jour par la tâche planifiée et à la demande.
/// </summary>
public sealed class EmissionRappels(
    IRendezVousRepository rendezVous,
    IConvocationRepository convocations,
    ParametresPlanification parametres,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider horloge)
{
    public async Task<RappelsDto> ExecuterAsync(DateOnly? date, CancellationToken cancellationToken)
    {
        var maintenant = horloge.GetUtcNow();
        var jour = date ?? HeureBelge.Jour(maintenant);
        var (delai1, delai2) = await parametres.DelaisRappelAsync(jour, cancellationToken);
        var horizon = Math.Max(delai1, delai2) + 1;
        var candidats = await rendezVous.RechercherAsync(
            new CritereRendezVous
            {
                Du = HeureBelge.VersUtc(jour.AddDays(1), TimeOnly.MinValue),
                Au = HeureBelge.VersUtc(jour.AddDays(horizon + 1), TimeOnly.MinValue),
                Statuts = [StatutRendezVous.Planifie],
            },
            cancellationToken);

        int premiers = 0, seconds = 0;
        foreach (var rdv in candidats.OrderBy(r => r.Debut))
        {
            if (rdv.RappelDu(jour, delai1, delai2) is not { } numero)
            {
                continue;
            }

            var derniere = (await convocations.ListAsync(rdv.Id, cancellationToken)).MaxBy(c => c.DateEmission);
            var canal = derniere?.Canal ?? parametres.Options.CanalParDefaut;
            rdv.MarquerRappel(numero, maintenant);
            outbox.Add(new RappelRendezVousDu(rdv.Id, rdv.PersonneId, rdv.AffilieId, rdv.LieuId, rdv.Debut, canal.ToString(), numero));
            if (numero == 1)
            {
                premiers++;
            }
            else
            {
                seconds++;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new RappelsDto(jour, premiers, seconds);
    }
}

public sealed record EmettreRappels(DateOnly? Date);

public sealed class EmettreRappelsHandler(EmissionRappels emission, ICurrentUser user) : ICommandHandler<EmettreRappels, RappelsDto>
{
    public async Task<Result<RappelsDto>> HandleAsync(EmettreRappels command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationGerer) is { } interdit)
        {
            return interdit;
        }

        return await emission.ExecuterAsync(command.Date, cancellationToken);
    }
}
