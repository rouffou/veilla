using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Planification;
using Sepp.Planification.Application.PriseRendezVous;
using Sepp.Planification.Domain;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Convocations;

namespace Sepp.Planification.Application.Urgences;

public sealed record UrgenceDto(bool Couverte, Guid? RendezVousId, DateTimeOffset? Debut, DateOnly Echeance);

/// <summary>
/// PLA-06 : réservation d'un créneau pour une urgence légale (examen de reprise, consultation spontanée, pré-reprise).
/// L'échéance est la date limite communiquée par Obligations, sinon le départ + le délai légal en jours ouvrables belges
/// (paramètre SANTE.*.DELAI, 10 par défaut). On retient le premier créneau libre jusqu'à la fin du jour de l'échéance :
/// créneaux réservés aux urgences (ressource compétente) ou créneaux ordinaires du même type d'acte. Sans créneau, les
/// obligations sont signalées « urgence non couverte » (événement d'alerte) et restent à planifier.
/// </summary>
public sealed class ReservationUrgence(
    PriseDeRendezVous prise,
    ParametresPlanification parametres,
    IObligationRepository obligations,
    IIntegrationEventOutbox outbox,
    TimeProvider horloge)
{
    public Task<Result<(RendezVous? RendezVous, DateOnly Echeance)>> ReserverAsync(Guid personneId, Guid affilieId, string typeActe,
        IReadOnlyList<Guid> obligationIds, DateOnly depart, DateOnly? dateLimite, CancellationToken cancellationToken) =>
        ReserverAsync(personneId, affilieId, typeActe, obligationIds, depart, dateLimite, null, cancellationToken);

    /// <summary>
    /// Comme <c>ReserverAsync</c> ; avec <paramref name="reconvocationDeId"/>, le rendez-vous remplace un rendez-vous manqué
    /// (SAN-13) : origine « reconvocation » et convocation de type <c>Reconvocation</c>.
    /// </summary>
    public async Task<Result<(RendezVous? RendezVous, DateOnly Echeance)>> ReserverAsync(Guid personneId, Guid affilieId, string typeActe,
        IReadOnlyList<Guid> obligationIds, DateOnly depart, DateOnly? dateLimite, Guid? reconvocationDeId, CancellationToken cancellationToken)
    {
        var delai = await parametres.DelaiUrgenceAsync(typeActe, depart, cancellationToken);
        if (dateLimite is null && delai is null)
        {
            return Error.Validation("urgence.type-non-urgent",
                $"{typeActe} n'est pas une urgence légale ({string.Join(", ", parametres.Options.TypesUrgence.Keys)}) : précisez la date limite.");
        }

        var echeance = dateLimite ?? DelaiLegal.Echeance(depart, delai!.Value, await parametres.CalendrierAsync(depart, depart.AddYears(1), cancellationToken));
        var maintenant = horloge.GetUtcNow();
        var finFenetre = HeureBelge.VersUtc(echeance.AddDays(1), TimeOnly.MinValue);
        var creneau = finFenetre > maintenant
            ? await prise.TrouverCreneauAsync(new RechercheCreneau(typeActe, maintenant, finFenetre, Urgent: true) { PersonneId = personneId }, cancellationToken)
            : null;

        if (creneau is null)
        {
            foreach (var obligation in await obligations.ListAsync(obligationIds, cancellationToken))
            {
                obligation.SignalerUrgenceNonCouverte();
            }

            foreach (var obligationId in obligationIds.Distinct())
            {
                outbox.Add(new UrgenceNonCouverte(obligationId, personneId, affilieId, typeActe, echeance));
            }

            return Result<(RendezVous? RendezVous, DateOnly Echeance)>.Success((null, echeance));
        }

        var demande = reconvocationDeId is { } manque
            ? new DemandeRendezVous(personneId, affilieId, obligationIds, OrigineRendezVous.Reconvocation, true)
            {
                TypeConvocation = TypeConvocation.Reconvocation,
                ReconvocationDeId = manque,
            }
            : new DemandeRendezVous(personneId, affilieId, obligationIds, OrigineRendezVous.Urgence, true);
        var rdv = await prise.PlanifierAsync(creneau, demande, cancellationToken);
        return rdv.IsSuccess
            ? Result<(RendezVous? RendezVous, DateOnly Echeance)>.Success((rdv.Value, echeance))
            : rdv.Error!;
    }
}

/// <summary>PLA-06 : le planificateur réserve un créneau d'urgence (hors saga automatique).</summary>
public sealed record ReserverUrgence(Guid PersonneId, Guid AffilieId, string TypeActe, IReadOnlyList<Guid>? ObligationIds, DateOnly DateDepart, DateOnly? DateLimite);

public sealed class ReserverUrgenceHandler(ReservationUrgence reservation, IObligationRepository obligations, IUnitOfWork unitOfWork, ICurrentUser user)
    : ICommandHandler<ReserverUrgence, UrgenceDto>
{
    public async Task<Result<UrgenceDto>> HandleAsync(ReserverUrgence command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationGerer) is { } interdit)
        {
            return interdit;
        }

        var typeActe = Domaine.Essayer(() => CodeMetier.Normaliser(command.TypeActe, "Type d'acte"));
        if (!typeActe.IsSuccess)
        {
            return typeActe.Error!;
        }

        var obligationIds = command.ObligationIds ?? [];
        if (await PlanifierRendezVousHandler.VerifierObligationsAsync(obligations, obligationIds, command.PersonneId, command.AffilieId, cancellationToken) is { } invalide)
        {
            return invalide;
        }

        var resultat = await reservation.ReserverAsync(command.PersonneId, command.AffilieId, typeActe.Value, obligationIds, command.DateDepart,
            command.DateLimite, cancellationToken);
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var (rdv, echeance) = resultat.Value;
        return rdv is null
            ? Error.Conflict("urgence.non-couverte", $"Aucun créneau disponible avant l'échéance légale du {echeance:yyyy-MM-dd}.")
            : new UrgenceDto(true, rdv.Id, rdv.Debut, echeance);
    }
}
