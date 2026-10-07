using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Obligations;
using Sepp.Contracts.Referentiels;
using Sepp.Planification.Application.Urgences;
using Sepp.Planification.Domain;
using Sepp.Planification.Domain.Projections;

namespace Sepp.Planification.Application.Projections;

/// <summary>
/// PLA-04, PLA-06 : projection locale des obligations (obligations.obligation-creee), idempotente (inbox ARC-31 et
/// écriture par clé). Saga de reprise (§14.6, étape 3) : une obligation d'un type urgent (examen de reprise, consultation
/// spontanée, pré-reprise) déclenche immédiatement la réservation d'un créneau d'urgence, qui publie RendezVousPlanifie
/// et la convocation — ou l'alerte UrgenceNonCouverte.
/// </summary>
public sealed class ObligationCreeeHandler(
    IObligationRepository obligations,
    ReservationUrgence reservation,
    ParametresPlanification parametres,
    IUnitOfWork unitOfWork) : IIntegrationEventHandler<ObligationCreee>
{
    public async Task HandleAsync(ObligationCreee integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var typeExamen = Normaliser(e.TypeExamen);
        var obligation = await obligations.GetAsync(e.ObligationId, cancellationToken);
        if (obligation is null)
        {
            obligation = new ObligationAPlanifier(e.ObligationId, e.PersonneId, e.AffilieId, typeExamen, e.DateDue, e.DateLimite, e.OccurredAt);
            obligations.Add(obligation);
        }
        else if (!obligation.Appliquer(e.PersonneId, e.AffilieId, typeExamen, e.DateDue, e.DateLimite, e.OccurredAt))
        {
            return;
        }

        if (obligation.EstAPlanifier && !obligation.UrgenceNonCouverte && parametres.EstUrgence(typeExamen))
        {
            var resultat = await reservation.ReserverAsync(e.PersonneId, e.AffilieId, typeExamen, [e.ObligationId], e.DateDue, e.DateLimite, cancellationToken);
            if (resultat.IsSuccess && resultat.Value.RendezVous is { } rdv)
            {
                obligation.Couvrir(rdv.Id);
            }
            else
            {
                // L'obligation vient peut-être d'être ajoutée : le signalement fait par la réservation ne la voit pas encore.
                obligation.SignalerUrgenceNonCouverte();
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Le type d'examen sert de type d'acte ; un code hors format est conservé tel quel (il ne correspondra à aucun créneau).</summary>
    internal static string Normaliser(string typeExamen)
    {
        try
        {
            return CodeMetier.Normaliser(typeExamen, "Type d'examen");
        }
        catch (DomainException)
        {
            return typeExamen.Trim();
        }
    }
}

/// <summary>PLA-04 : une obligation dépasse sa date limite (obligations.obligation-echue) ; elle passe en tête des propositions.</summary>
public sealed class ObligationEchueHandler(IObligationRepository obligations, IUnitOfWork unitOfWork) : IIntegrationEventHandler<ObligationEchue>
{
    public async Task HandleAsync(ObligationEchue integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var obligation = await obligations.GetAsync(e.ObligationId, cancellationToken);
        if (obligation is null)
        {
            // Livraison désordonnée : l'échéance arrive avant la création.
            obligation = new ObligationAPlanifier(e.ObligationId, e.PersonneId, e.AffilieId, ObligationCreeeHandler.Normaliser(e.TypeExamen), e.DateLimite,
                e.DateLimite, DateTimeOffset.MinValue);
            obligations.Add(obligation);
        }

        obligation.MarquerEchue(e.DateLimite);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>ARC-21 : copie locale des paramètres légaux utilisés (délais d'urgence, rappels J-7 / J-1).</summary>
public sealed class ParametreLegalModifieHandler(IParametreLocalRepository parametresLocaux, OptionsPlanification options, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<ParametreLegalModifie>
{
    public async Task HandleAsync(ParametreLegalModifie integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        if (e.Code is not (ParametresPlanification.Rappel1 or ParametresPlanification.Rappel2) && !options.TypesUrgence.ContainsValue(e.Code))
        {
            return;
        }

        var parametre = await parametresLocaux.GetAsync(e.Code, e.ValideDu, cancellationToken);
        if (parametre is null)
        {
            parametresLocaux.Add(new ParametreLegalLocal(e.Code, e.ValideDu, e.ValideJusquAu, e.Valeur, e.Unite));
        }
        else
        {
            parametre.Appliquer(e.ValideJusquAu, e.Valeur, e.Unite);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
