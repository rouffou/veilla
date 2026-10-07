using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Planification.Application.PriseRendezVous;
using Sepp.Planification.Domain;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Ressources;

namespace Sepp.Planification.Application.SalleAttente;

/// <summary>Personne en salle d'attente : identifiants et horaires uniquement (le nom s'affiche via le service Personnes).</summary>
public sealed record PersonneEnAttenteDto(int Position, Guid RendezVousId, Guid PersonneId, Guid RessourceId, DateTimeOffset HeurePrevue, DateTimeOffset ArriveeA);

public sealed record PersonneAppeleeDto(Guid RendezVousId, Guid PersonneId, Guid RessourceId, Guid? SalleId, DateTimeOffset AppeleA);

public sealed record SalleAttenteDto(
    Guid LieuId,
    DateOnly Date,
    IReadOnlyList<PersonneEnAttenteDto> EnAttente,
    IReadOnlyList<PersonneAppeleeDto> Appeles,
    int Attendus);

/// <summary>PLA-08 : enregistrement à l'arrivée (borne ou accueil), le jour du rendez-vous.</summary>
public sealed record EnregistrerArrivee(Guid RendezVousId);

public sealed class EnregistrerArriveeHandler(IRendezVousRepository rendezVous, IUnitOfWork unitOfWork, TimeProvider horloge, ICurrentUser user)
    : ICommandHandler<EnregistrerArrivee, Unit>
{
    public async Task<Result<Unit>> HandleAsync(EnregistrerArrivee command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationSalleAttente) is { } interdit)
        {
            return interdit;
        }

        var rdv = await rendezVous.GetAsync(command.RendezVousId, cancellationToken);
        if (rdv is null)
        {
            return RendezVousMapping.Inconnu();
        }

        try
        {
            rdv.EnregistrerArrivee(horloge.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Error.Conflict("rendez-vous.statut", ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>PLA-08 : appel en salle (salle ou cabine facultative).</summary>
public sealed record AppelerEnSalle(Guid RendezVousId, Guid? SalleId);

public sealed class AppelerEnSalleHandler(
    IRendezVousRepository rendezVous,
    IRessourceRepository ressources,
    IUnitOfWork unitOfWork,
    TimeProvider horloge,
    ICurrentUser user) : ICommandHandler<AppelerEnSalle, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AppelerEnSalle command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationSalleAttente) is { } interdit)
        {
            return interdit;
        }

        if (command.SalleId is { } salleId &&
            await ressources.GetAsync(salleId, cancellationToken) is not { Type: TypeRessource.Salle or TypeRessource.Cabine })
        {
            return Error.Validation("salle.inconnue", "La salle d'appel doit être une ressource de type salle ou cabine.");
        }

        var rdv = await rendezVous.GetAsync(command.RendezVousId, cancellationToken);
        if (rdv is null)
        {
            return RendezVousMapping.Inconnu();
        }

        try
        {
            rdv.Appeler(command.SalleId, horloge.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Error.Conflict("rendez-vous.statut", ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Fin de la consultation : la personne quitte la salle.</summary>
public sealed record TerminerRendezVous(Guid RendezVousId);

public sealed class TerminerRendezVousHandler(IRendezVousRepository rendezVous, IUnitOfWork unitOfWork, ICurrentUser user)
    : ICommandHandler<TerminerRendezVous, Unit>
{
    public async Task<Result<Unit>> HandleAsync(TerminerRendezVous command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationSalleAttente) is { } interdit)
        {
            return interdit;
        }

        var rdv = await rendezVous.GetAsync(command.RendezVousId, cancellationToken);
        if (rdv is null)
        {
            return RendezVousMapping.Inconnu();
        }

        if (Domaine.Essayer(rdv.Terminer) is { } erreur)
        {
            return erreur with { Kind = ErrorKind.Conflict };
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// PLA-08 : file d'attente d'un lieu pour une journée. Ordre d'appel : heure prévue du rendez-vous, puis heure d'arrivée
/// (une personne en avance ne passe pas devant celle dont le rendez-vous est plus tôt).
/// </summary>
public sealed record ConsulterSalleAttente(Guid LieuId, DateOnly? Date);

public sealed class ConsulterSalleAttenteHandler(IRendezVousRepository rendezVous, TimeProvider horloge, ICurrentUser user)
    : IQueryHandler<ConsulterSalleAttente, SalleAttenteDto>
{
    public async Task<Result<SalleAttenteDto>> HandleAsync(ConsulterSalleAttente query, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationSalleAttente) is { } interdit)
        {
            return interdit;
        }

        var jour = query.Date ?? HeureBelge.Jour(horloge.GetUtcNow());
        var rdvs = await rendezVous.RechercherAsync(
            new CritereRendezVous
            {
                LieuId = query.LieuId,
                Du = HeureBelge.VersUtc(jour, TimeOnly.MinValue),
                Au = HeureBelge.VersUtc(jour.AddDays(1), TimeOnly.MinValue),
            },
            cancellationToken);

        var enAttente = rdvs.Where(r => r.Statut == StatutRendezVous.Arrive)
            .OrderBy(r => r.Debut).ThenBy(r => r.ArriveeA).ThenBy(r => r.Id)
            .Select((r, i) => new PersonneEnAttenteDto(i + 1, r.Id, r.PersonneId, r.RessourceId, r.Debut, r.ArriveeA!.Value))
            .ToList();
        var appeles = rdvs.Where(r => r.Statut == StatutRendezVous.EnSalle)
            .OrderByDescending(r => r.AppeleA)
            .Select(r => new PersonneAppeleeDto(r.Id, r.PersonneId, r.RessourceId, r.SalleId, r.AppeleA!.Value))
            .ToList();
        return new SalleAttenteDto(query.LieuId, jour, enAttente, appeles, rdvs.Count(r => r.Statut == StatutRendezVous.Planifie));
    }
}
