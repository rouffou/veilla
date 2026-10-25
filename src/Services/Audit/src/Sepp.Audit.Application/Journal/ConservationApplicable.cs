using Sepp.Audit.Domain.Journal;
using Sepp.Audit.Domain.Parametres;
using Sepp.BuildingBlocks.Application;
using Sepp.Contracts.Referentiels;

namespace Sepp.Audit.Application.Journal;

/// <summary>Projection locale des paramètres légaux de Référentiels utilisés par Audit (ARC-21).</summary>
public interface IParametreLegalRepository
{
    Task<ParametreLegalLocal?> GetAsync(string code, DateOnly valideDu, CancellationToken cancellationToken);

    /// <summary>Valeur en vigueur à la date, la plus récemment entrée en vigueur si plusieurs se chevauchent.</summary>
    Task<ParametreLegalLocal?> ApplicableAsync(string code, DateOnly date, CancellationToken cancellationToken);

    void Add(ParametreLegalLocal parametre);
}

/// <summary>
/// ARC-21, NF-04 : copie locale du paramètre <c>AUDIT.CONSERVATION</c>. Les autres paramètres légaux, publiés sur la même
/// rubrique, n'intéressent pas Audit. Écriture par clé ; la transaction est celle de l'inbox (comme les traces d'accès).
/// </summary>
public sealed class ParametreLegalModifieHandler(IParametreLegalRepository parametres) : IIntegrationEventHandler<ParametreLegalModifie>
{
    public async Task HandleAsync(ParametreLegalModifie integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        if (e.Code != CodesParametresLegaux.AuditConservation)
        {
            return;
        }

        var parametre = await parametres.GetAsync(e.Code, e.ValideDu, cancellationToken);
        if (parametre is null)
        {
            parametres.Add(new ParametreLegalLocal(e.Code, e.ValideDu, e.ValideJusquAu, e.Valeur, e.Unite));
        }
        else
        {
            parametre.Appliquer(e.ValideJusquAu, e.Valeur, e.Unite);
        }
    }
}

/// <summary>
/// Durée de conservation du journal en vigueur : paramètre <c>AUDIT.CONSERVATION</c> reçu de Référentiels, sinon sa valeur
/// initiale (<see cref="ParametresLegauxParDefaut"/>, source unique). Jamais en dessous du minimum légal de 10 ans (NF-04) :
/// un paramètre plus court est ignoré.
/// </summary>
public sealed class ConservationApplicable(IParametreLegalRepository parametres)
{
    public async Task<int> AnneesAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var parametre = await parametres.ApplicableAsync(CodesParametresLegaux.AuditConservation, date, cancellationToken);
        var annees = parametre?.Unite switch
        {
            UnitesParametreLegal.Annees => (int)Math.Ceiling(parametre.Valeur),
            UnitesParametreLegal.Mois => (int)Math.Ceiling(parametre.Valeur / 12),
            _ => ParametresLegauxParDefaut.Obtenir(CodesParametresLegaux.AuditConservation).Valeur,
        };
        return Math.Max(annees, PolitiqueConservation.MinimumAnnees);
    }
}
