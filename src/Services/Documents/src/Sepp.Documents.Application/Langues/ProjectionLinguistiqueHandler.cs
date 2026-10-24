using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Affilies;
using Sepp.Contracts.Personnes;
using Sepp.Documents.Domain.Langues;

namespace Sepp.Documents.Application.Langues;

/// <summary>
/// NF-41, ADR 0009 : alimente la projection <c>contexte_linguistique</c> à partir des champs facultatifs v1 des événements
/// d'Affiliés (<c>RegimeLinguistique</c>, <c>Langue</c>) et de Personnes (<c>Langue</c>). Idempotent : l'inbox écarte les
/// messages déjà traités (ARC-31) et la projection est une écriture par clé qui ignore un événement plus ancien que l'état
/// connu (<c>OccurredAt</c>). Un événement sans ces champs (producteur antérieur) ou avec une valeur inconnue ne change rien.
/// Enregistré par chaque instance (abonnements <c>affilies</c> et <c>personnes</c> dans <c>infra/variables.tf</c>), qu'elle lise
/// la projection ou non (<c>Documents:SourceLinguistique</c>) : la table est prête si l'instance bascule.
/// </summary>
public sealed class ProjectionLinguistiqueHandler(IContexteLinguistiqueRepository contextes, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<AffilieCree>,
      IIntegrationEventHandler<AffilieModifie>,
      IIntegrationEventHandler<OccupationDebutee>,
      IIntegrationEventHandler<LangueModifiee>
{
    public Task HandleAsync(AffilieCree integrationEvent, CancellationToken cancellationToken) =>
        AffilieAsync(integrationEvent.AffilieId, integrationEvent.RegimeLinguistique, integrationEvent.Langue, integrationEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(AffilieModifie integrationEvent, CancellationToken cancellationToken) =>
        AffilieAsync(integrationEvent.AffilieId, integrationEvent.RegimeLinguistique, integrationEvent.Langue, integrationEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(OccupationDebutee integrationEvent, CancellationToken cancellationToken) =>
        PersonneAsync(integrationEvent.PersonneId, integrationEvent.Langue, integrationEvent.OccurredAt, cancellationToken);

    public Task HandleAsync(LangueModifiee integrationEvent, CancellationToken cancellationToken) =>
        PersonneAsync(integrationEvent.PersonneId, integrationEvent.Langue, integrationEvent.OccurredAt, cancellationToken);

    private async Task AffilieAsync(Guid affilieId, string? regimeCode, string? langueCode, DateTimeOffset du, CancellationToken cancellationToken)
    {
        var regime = Enumeration<RegimeLinguistique>(regimeCode);
        var langue = Enumeration<Language>(langueCode);
        if (regime is null && langue is null)
        {
            return;
        }

        var contexte = await contextes.GetAsync(SujetLinguistique.Affilie, affilieId, cancellationToken);
        if (contexte is null)
        {
            contextes.Add(ContexteLinguistiqueProjete.Affilie(affilieId, regime, langue, du));
        }
        else if (!contexte.Appliquer(regime, langue, du))
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task PersonneAsync(Guid personneId, string? langueCode, DateTimeOffset du, CancellationToken cancellationToken)
    {
        if (Enumeration<Language>(langueCode) is not { } langue)
        {
            return;
        }

        var contexte = await contextes.GetAsync(SujetLinguistique.Personne, personneId, cancellationToken);
        if (contexte is null)
        {
            contextes.Add(ContexteLinguistiqueProjete.Personne(personneId, langue, du));
        }
        else if (!contexte.Appliquer(null, langue, du))
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Nom d'énumération, sans tenir compte de la casse ; une valeur numérique ou inconnue est ignorée.</summary>
    public static T? Enumeration<T>(string? code)
        where T : struct, Enum =>
        !string.IsNullOrWhiteSpace(code) && !char.IsAsciiDigit(code.Trim()[0]) && Enum.TryParse<T>(code.Trim(), ignoreCase: true, out var valeur) && Enum.IsDefined(valeur)
            ? valeur
            : null;
}
