using Sepp.BuildingBlocks.Application;
using Sepp.Contracts.Obligations;
using Sepp.Contracts.Personnes;
using Sepp.Contracts.Planification;
using Sepp.Contracts.PostesRisques;
using Sepp.Contracts.Prevention;
using Sepp.Contracts.Referentiels;
using Sepp.SurveillanceMedicale.Domain.Conservation;
using Sepp.SurveillanceMedicale.Domain.Examens;
using Sepp.SurveillanceMedicale.Domain.Projections;

namespace Sepp.SurveillanceMedicale.Application.Projections;

// Projections idempotentes (ARC-31) : l'inbox écarte un message déjà traité, et chaque projection est une écriture par
// clé, si bien qu'un événement rejoué ou republié (état courant) ne duplique rien.

/// <summary>SAN-20 : examens dus (obligations.obligation-creee).</summary>
public sealed class ObligationCreeeHandler(IProjectionRepository projections, IUnitOfWork unitOfWork) : IIntegrationEventHandler<ObligationCreee>
{
    public async Task HandleAsync(ObligationCreee integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var obligation = await projections.GetObligationAsync(e.ObligationId, cancellationToken);
        if (obligation is null)
        {
            projections.Add(new ObligationDue(e.ObligationId, e.PersonneId, e.AffilieId, e.TypeExamen, e.DateDue, e.DateLimite));
        }
        else
        {
            obligation.Appliquer(e.PersonneId, e.AffilieId, e.TypeExamen, e.DateDue, e.DateLimite);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Rendez-vous planifiés (planification.rendez-vous-planifie) : convocation, base de l'ouverture d'examen.</summary>
public sealed class RendezVousPlanifieHandler(IProjectionRepository projections, IUnitOfWork unitOfWork) : IIntegrationEventHandler<RendezVousPlanifie>
{
    public async Task HandleAsync(RendezVousPlanifie integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var rendezVous = await projections.GetRendezVousAsync(e.RendezVousId, cancellationToken);
        if (rendezVous is null)
        {
            projections.Add(new RendezVousPrevu(e.RendezVousId, e.PersonneId, e.AffilieId, e.Debut, e.ObligationIds));
        }
        else
        {
            rendezVous.Appliquer(e.PersonneId, e.AffilieId, e.Debut, e.ObligationIds);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>SAN-20 : postes de la personne (personnes.affectation-modifiee) ; un événement plus ancien que l'état connu est ignoré.</summary>
public sealed class AffectationModifieeHandler(IProjectionRepository projections, IUnitOfWork unitOfWork) : IIntegrationEventHandler<AffectationModifiee>
{
    public async Task HandleAsync(AffectationModifiee integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var affectation = await projections.GetAffectationAsync(e.AffectationId, cancellationToken);
        if (affectation is null)
        {
            projections.Add(new AffectationPersonne(e.AffectationId, e.PersonneId, e.PosteId, e.DateDebut, e.DateFin, e.OccurredAt));
        }
        else if (!affectation.Appliquer(e.PersonneId, e.PosteId, e.DateDebut, e.DateFin, e.OccurredAt))
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>SAN-20 : risques des postes (postes-risques.profil-risque-poste-modifie), clé (poste, date d'effet).</summary>
public sealed class ProfilRisquePosteModifieHandler(IProjectionRepository projections, IUnitOfWork unitOfWork) : IIntegrationEventHandler<ProfilRisquePosteModifie>
{
    public async Task HandleAsync(ProfilRisquePosteModifie integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var profil = await projections.GetProfilAsync(e.PosteId, e.ValideDu, cancellationToken);
        if (profil is null)
        {
            projections.Add(new ProfilRisquePoste(e.PosteId, e.AffilieId, e.ValideDu, e.CodesRisques));
        }
        else
        {
            profil.Appliquer(e.AffilieId, e.CodesRisques);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// SAN-40 : mesurage d'exposition d'un groupe (prevention.mesurage-enregistre) ; versé aux données d'exposition des
/// dossiers des personnes rattachées au groupe à la date du mesurage (un mesurage n'est versé qu'une fois par dossier).
/// </summary>
public sealed class MesurageEnregistreHandler(IProjectionRepository projections, IDossierSanteRepository dossiers, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<MesurageEnregistre>
{
    public async Task HandleAsync(MesurageEnregistre integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        if (await projections.GetMesurageAsync(e.MesurageId, cancellationToken) is null)
        {
            projections.Add(new MesurageExposition(e.MesurageId, e.GroupeExpositionId, e.AffilieId, e.Agent, e.Niveau, e.Date));
        }

        foreach (var dossier in await dossiers.ListerRattachesAuGroupeAsync(e.GroupeExpositionId, cancellationToken))
        {
            if (dossier.EstExposeeViaGroupe(e.GroupeExpositionId, e.Date))
            {
                dossier.EnregistrerExposition(e.Agent, e.Niveau, e.Date, e.Date, e.MesurageId, e.GroupeExpositionId);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>ARC-21 : paramètres légaux utilisés par ce service (conservation du dossier, délai de l'examen de reprise).</summary>
public sealed class ParametreLegalModifieHandler(IProjectionRepository projections, IUnitOfWork unitOfWork) : IIntegrationEventHandler<ParametreLegalModifie>
{
    public static readonly IReadOnlySet<string> ParametresSuivis = new HashSet<string>(StringComparer.Ordinal)
    {
        PolitiqueConservationDossier.CodeParametreMinimum,
        PolitiqueDelaiReprise.CodeParametreDelai,
    };

    public async Task HandleAsync(ParametreLegalModifie integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        if (!ParametresSuivis.Contains(e.Code))
        {
            return;
        }

        var parametre = await projections.GetParametreAsync(e.Code, e.ValideDu, cancellationToken);
        if (parametre is null)
        {
            projections.Add(new ParametreLegalLocal(e.Code, e.ValideDu, e.ValideJusquAu, e.Valeur, e.Unite));
        }
        else
        {
            parametre.Appliquer(e.ValideJusquAu, e.Valeur, e.Unite);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
