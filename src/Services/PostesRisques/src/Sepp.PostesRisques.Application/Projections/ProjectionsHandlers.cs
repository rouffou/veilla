using Sepp.BuildingBlocks.Application;
using Sepp.Contracts.Personnes;
using Sepp.Contracts.Referentiels;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.PostesRisques.Domain.Projections;

namespace Sepp.PostesRisques.Application.Projections;

/// <summary>
/// AFF-30 : projection locale des affectations travailleur ↔ poste (personnes.affectation-modifiee).
/// Idempotente : l'inbox écarte les messages déjà traités (ARC-31) et la projection est une écriture par clé
/// (identifiant d'affectation) qui ignore un événement plus ancien que l'état connu.
/// </summary>
public sealed class AffectationModifieeHandler(IProjectionRepository projections, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<AffectationModifiee>
{
    public async Task HandleAsync(AffectationModifiee integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var affectation = await projections.GetAffectationAsync(e.AffectationId, cancellationToken);
        if (affectation is null)
        {
            projections.Add(new AffectationPoste(e.AffectationId, e.PersonneId, e.PosteId, e.DateDebut, e.DateFin, e.OccurredAt));
        }
        else if (!affectation.Appliquer(e.PersonneId, e.PosteId, e.DateDebut, e.DateFin, e.OccurredAt))
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// AFF-30 : date de la dernière évaluation, projetée depuis surveillance-medicale.examen-cloture
/// (type et date uniquement, ARC-06). Écriture par clé (identifiant d'examen) : rejouer ne duplique rien.
/// </summary>
public sealed class ExamenClotureHandler(IProjectionRepository projections, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<ExamenCloture>
{
    public async Task HandleAsync(ExamenCloture integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        var examen = await projections.GetExamenAsync(e.ExamenId, cancellationToken);
        if (examen is null)
        {
            projections.Add(new ExamenRealise(e.ExamenId, e.PersonneId, e.AffilieId, e.TypeExamen, e.Date));
        }
        else
        {
            examen.Appliquer(e.PersonneId, e.AffilieId, e.TypeExamen, e.Date);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// ARC-21 : copie locale des paramètres légaux utilisés par ce service (referentiels.parametre-legal-modifie),
/// par exemple la durée de conservation des listes nominatives (AFF-31).
/// </summary>
public sealed class ParametreLegalModifieHandler(IProjectionRepository projections, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<ParametreLegalModifie>
{
    public static readonly IReadOnlySet<string> ParametresSuivis = new HashSet<string>(StringComparer.Ordinal)
    {
        PolitiqueConservationListes.CodeParametre,
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
