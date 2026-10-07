using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Planification;
using Sepp.Planification.Application.PriseRendezVous;
using Sepp.Planification.Application.Ressources;
using Sepp.Planification.Domain;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Convocations;
using Sepp.Planification.Domain.Ressources;

namespace Sepp.Planification.Application.Replanification;

public sealed record ReplanificationDto(Guid AbsenceId, int RendezVousDeplaces, int RendezVousHorsDelai, int RendezVousAnnules, int CreneauxBloques);

/// <summary>
/// PLA-07 : replanification en masse après l'absence d'une ressource (conseiller, mais aussi salle ou appareil) sur
/// [Debut, Fin[. Pour chaque rendez-vous à venir qui mobilise la ressource, on cherche, dans le même lieu et en évitant
/// la ressource absente, le premier créneau libre du même type d'acte : d'abord avant la date limite des obligations
/// couvertes, sinon dans l'horizon de recherche (signalé « hors délai »). Le rendez-vous est déplacé (même identifiant,
/// événement RendezVousReplanifie et convocation de type Replanification pour notifier la personne) ; sans solution, il
/// est annulé (motif AbsenceRessource) et ses obligations redeviennent à planifier. Les créneaux libérés et les créneaux
/// libres de la ressource sur la période sont bloqués.
/// </summary>
public sealed record ReplanifierAbsence(Guid RessourceId, DateTimeOffset Debut, DateTimeOffset Fin);

public sealed class ReplanifierAbsenceHandler(
    IRessourceRepository ressources,
    IAbsenceRepository absences,
    ICreneauRepository creneaux,
    IRendezVousRepository rendezVous,
    IObligationRepository obligations,
    PriseDeRendezVous prise,
    Annulation annulation,
    Indisponibilites indisponibilites,
    ParametresPlanification parametres,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider horloge,
    ICurrentUser user) : ICommandHandler<ReplanifierAbsence, ReplanificationDto>
{
    public async Task<Result<ReplanificationDto>> HandleAsync(ReplanifierAbsence command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationGerer) is { } interdit)
        {
            return interdit;
        }

        if (command.Fin <= command.Debut || command.Fin - command.Debut > TimeSpan.FromDays(92))
        {
            return Error.Validation("absence.periode-invalide", "L'absence couvre une période non vide d'au plus 92 jours.");
        }

        var ressource = await ressources.GetAsync(command.RessourceId, cancellationToken);
        if (ressource is null)
        {
            return Error.NotFound("ressource.inconnue", "Ressource inconnue.");
        }

        var absence = Absence.Creer(ressource.Id, command.Debut, command.Fin, SourceAbsence.Saisie, null);
        absences.Add(absence);

        var maintenant = horloge.GetUtcNow();
        var reserves = (await creneaux.ChevauchantsAsync([ressource.Id], command.Debut, command.Fin, cancellationToken))
            .Where(c => c.Statut == StatutCreneau.Reserve && c.Debut > maintenant)
            .ToDictionary(c => c.Id);
        var aDeplacer = reserves.Count == 0
            ? []
            : await rendezVous.RechercherAsync(
                new CritereRendezVous { CreneauIds = reserves.Keys.ToList(), Statuts = [StatutRendezVous.Planifie] }, cancellationToken);

        int deplaces = 0, horsDelai = 0, annules = 0;
        foreach (var rdv in aDeplacer.OrderBy(r => r.Debut))
        {
            var ancien = reserves[rdv.CreneauId];
            var limite = (await obligations.ListAsync(rdv.ObligationIds, cancellationToken))
                .Select(o => o.DateLimite).OfType<DateOnly>().DefaultIfEmpty(DateOnly.MaxValue).Min();
            var horizon = maintenant.AddDays(parametres.Options.HorizonRechercheJours);
            var recherche = new RechercheCreneau(rdv.TypeActe, maintenant, horizon, rdv.Urgent)
            {
                LieuId = rdv.LieuId,
                SaufRessource = ressource.Id,
                PersonneId = rdv.PersonneId,
                SaufRendezVousId = rdv.Id,
            };

            Creneau? nouveau = null;
            if (limite != DateOnly.MaxValue)
            {
                var finLimite = HeureBelge.VersUtc(limite.AddDays(1), TimeOnly.MinValue);
                nouveau = await prise.TrouverCreneauAsync(recherche with { Au = finLimite < horizon ? finLimite : horizon }, cancellationToken);
            }

            nouveau ??= await prise.TrouverCreneauAsync(recherche, cancellationToken);
            if (nouveau is null)
            {
                if (await annulation.AnnulerAsync(rdv, MotifAnnulation.AbsenceRessource, cancellationToken) is null)
                {
                    annules++;
                }

                continue;
            }

            var ancienDebut = rdv.Deplacer(ancien, nouveau, maintenant);
            outbox.Add(new RendezVousReplanifie(rdv.Id, rdv.PersonneId, rdv.AffilieId, ancienDebut, rdv.Debut, MotifAnnulation.AbsenceRessource.ToString()));
            await prise.ConvoquerAsync(rdv, null, null, TypeConvocation.Replanification, null, cancellationToken);
            deplaces++;
            if (!DelaiLegal.RespecteEcheance(rdv.Debut, limite))
            {
                horsDelai++;
            }
        }

        var (bloques, _) = await indisponibilites.BloquerAsync(ressource.Id, command.Debut, command.Fin, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ReplanificationDto(absence.Id, deplaces, horsDelai, annules, bloques);
    }
}
