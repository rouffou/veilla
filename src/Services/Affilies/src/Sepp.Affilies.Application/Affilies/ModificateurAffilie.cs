using Sepp.Affilies.Application.Historique;
using Sepp.Affilies.Application.Securite;
using Sepp.Affilies.Domain.Affilies;
using Sepp.Affilies.Domain.Historique;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts;
using Sepp.Contracts.Affilies;

namespace Sepp.Affilies.Application.Affilies;

/// <summary>
/// Déroulé commun des cas d'usage qui modifient un affilié : contrôle d'accès (§3.3), chargement, règle métier,
/// entrée d'historique « avant / après » (AFF-05) et événements d'intégration, validés dans une seule transaction (ARC-22, ARC-32).
/// </summary>
public sealed class ModificateurAffilie(
    IAffilieRepository affilies,
    IHistoriqueAffilieRepository historique,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    ICurrentUser utilisateur,
    ControleAcces acces,
    TimeProvider horloge)
{
    private static readonly TimeZoneInfo Bruxelles = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    /// <summary>Date du jour en Belgique.</summary>
    public DateOnly Aujourdhui => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(horloge.GetUtcNow(), Bruxelles).DateTime);

    public Task<Result<T>> ModifierAsync<T>(Guid affilieId, PartieFiche partie, string action, Func<Affilie, T> modification, CancellationToken cancellationToken) =>
        ModifierEtVerifierAsync(affilieId, partie, action, a => Task.FromResult<Result<T>>(modification(a)), cancellationToken);

    /// <summary>Variante dont la modification effectue des contrôles asynchrones (existence d'un autre affilié…).</summary>
    public Task<Result<T>> ModifierEtVerifierAsync<T>(Guid affilieId, PartieFiche partie, string action, Func<Affilie, Task<Result<T>>> modification, CancellationToken cancellationToken) =>
        ExecuterAsync(affilieId, partie, action, modification, cancellationToken);

    /// <summary>
    /// Modification par un traitement technique (consommateur d'événements, sans utilisateur) : pas de contrôle d'accès,
    /// l'historique AFF-05 est écrit sous l'identité « system ». Réservé aux gestionnaires d'événements du service.
    /// </summary>
    public Task<Result<T>> ModifierParSystemeAsync<T>(Guid affilieId, string action, Func<Affilie, T> modification, CancellationToken cancellationToken) =>
        ExecuterAsync(affilieId, null, action, a => Task.FromResult<Result<T>>(modification(a)), cancellationToken);

    private async Task<Result<T>> ExecuterAsync<T>(Guid affilieId, PartieFiche? partie, string action, Func<Affilie, Task<Result<T>>> modification, CancellationToken cancellationToken)
    {
        if (partie is { } partieModifiee && acces.VerifierEcriture(affilieId, partieModifiee) is { } refus)
        {
            return refus;
        }

        var affilie = await affilies.GetAsync(affilieId, cancellationToken);
        if (affilie is null)
        {
            return Error.NotFound("affilie.inconnu", $"Affilié {affilieId} inconnu.");
        }

        var avant = Instantane.De(affilie);
        Result<T> resultat;
        try
        {
            resultat = await modification(affilie);
        }
        catch (ElementIntrouvableException ex)
        {
            return Error.NotFound("affilie.element-inconnu", ex.Message);
        }
        catch (DomainException ex)
        {
            return Error.Validation("affilie.regle-metier", ex.Message);
        }

        if (!resultat.IsSuccess)
        {
            return resultat;
        }

        var apres = Instantane.De(affilie);
        if (Instantane.Difference(avant, apres) is not { } difference)
        {
            return resultat;
        }

        historique.Add(ModificationAffilie.Enregistrer(affilie.Id, affilie.NumeroVersion, action, utilisateur.UserId, horloge.GetUtcNow(),
            difference.Avant?.ToJsonString(Instantane.Options), difference.Apres?.ToJsonString(Instantane.Options)));

        // AffilieModifie : uniquement si la fiche elle-même (identité, catégorie, statut…) a changé.
        if (difference.Apres is System.Text.Json.Nodes.JsonObject changements && changements.ContainsKey("fiche"))
        {
            outbox.Add(AffilieModifie(affilie));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return resultat;
    }

    /// <summary>Première entrée d'historique d'un affilié créé (valeurs « après » uniquement).</summary>
    public void JournaliserCreation(Affilie affilie) =>
        historique.Add(ModificationAffilie.Enregistrer(affilie.Id, affilie.NumeroVersion, "affilie.cree", utilisateur.UserId, horloge.GetUtcNow(),
            null, Instantane.De(affilie).ToJsonString(Instantane.Options)));

    public void Publier(IntegrationEvent evenement) => outbox.Add(evenement);

    public static AffilieModifie AffilieModifie(Affilie a) =>
        new(a.Id, a.NumeroBce.Value, a.CategorieTarifaire.ToString(), a.Statut.ToString());

    public static OperationAffilieModifiee OperationModifiee(Affilie a, OperationAffilie o) =>
        new(o.Id, a.Id, o.Type.ToString(), o.Statut.ToString(), o.DateEffet, o.AffilieAbsorbantId, o.AffiliesBeneficiaires.ToList(), o.SeppContrepartie);
}
