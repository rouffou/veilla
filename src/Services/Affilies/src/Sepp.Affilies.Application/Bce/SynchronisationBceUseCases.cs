using Sepp.Affilies.Application.Affilies;
using Sepp.Affilies.Application.Securite;
using Sepp.Affilies.Domain.Affilies;
using Sepp.Affilies.Domain.Ecarts;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Integrations;

namespace Sepp.Affilies.Application.Bce;

/// <summary>
/// AFF-01, AFF-02, INT-04 — Consommateur de <c>integrations.donnees-bce-recues.v1</c> : met à jour l'affilié du numéro BCE
/// (dénomination, forme juridique, NACE, unités d'établissement) avec les dernières données lues chez Intégrations, l'événement
/// ne portant que des identifiants (ARC-06). L'historique AFF-05 est écrit sous l'identité « system » et les périodes de
/// validité des unités suivent DAT-04.
/// <para>
/// Idempotent : des données identiques ne modifient rien. Ce qui ne se résout pas automatiquement (affilié inconnu, unité
/// d'un autre affilié, donnée invalide…) est consigné comme écart du gestionnaire de dossiers et ne fait pas échouer le message ;
/// seule une panne technique de la lecture chez Intégrations relève le message pour reprise.
/// </para>
/// </summary>
public sealed class DonneesBceRecuesHandler(
    IAffilieRepository affilies,
    IEntrepriseBceClient entreprisesBce,
    IEcartSynchronisationRepository ecarts,
    ModificateurAffilie modificateur,
    IUnitOfWork unitOfWork,
    TimeProvider horloge) : IIntegrationEventHandler<DonneesBceRecues>
{
    public const string ActionHistorique = "affilie.donnees-bce-appliquees";

    public async Task HandleAsync(DonneesBceRecues integrationEvent, CancellationToken cancellationToken)
    {
        if (!NumeroBce.TryParse(integrationEvent.NumeroBce, out var numero) || numero is null)
        {
            var invalide = new EcartBce(CodesEcartBce.NumeroInvalide, Tronquer(integrationEvent.NumeroBce), "Numéro d'entreprise BCE invalide dans les données reçues.");
            Rapprocher(await ecarts.OuvertsParBceAsync(Tronquer(integrationEvent.NumeroBce), cancellationToken), Tronquer(integrationEvent.NumeroBce), null, [invalide], integrationEvent.DateExtraction);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        var ouverts = await ecarts.OuvertsParBceAsync(numero.Value, cancellationToken);
        var affilie = await affilies.GetParBceAsync(numero, cancellationToken);
        if (affilie is null)
        {
            Rapprocher(ouverts, numero.Value, null,
                [new EcartBce(CodesEcartBce.AffilieInconnu, numero.Value, $"Aucun affilié pour le numéro BCE {numero.Formate} : données BCE reçues non appliquées.")],
                integrationEvent.DateExtraction);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        // Dernières données (adresses comprises) ; les valeurs de l'événement ne servent que si Intégrations n'en a plus.
        var entreprise = await entreprisesBce.LireAsync(numero.Value, cancellationToken);
        var donnees = entreprise is null
            ? new DonneesBce(integrationEvent.Denomination, integrationEvent.FormeJuridique, integrationEvent.CodeNace, null)
            : new DonneesBce(entreprise.Denomination, entreprise.FormeJuridique, entreprise.CodeNace, entreprise.Unites);
        var dateExtraction = entreprise?.DateExtraction ?? integrationEvent.DateExtraction;
        var autres = await UnitesAutresAffiliesAsync(affilie, donnees.Unites, cancellationToken);

        var resultat = await modificateur.ModifierParSystemeAsync(affilie.Id, ActionHistorique, a =>
        {
            var detectes = a.AppliquerDonneesBce(donnees, dateExtraction, autres).ToList();
            if (entreprise is null)
            {
                detectes.Add(new EcartBce(CodesEcartBce.DetailIndisponible, numero.Value,
                    "Intégrations ne renvoie plus les données de l'entreprise : unités d'établissement non mises à jour."));
            }

            // Écarts et modification dans la même transaction (le modificateur valide quand la fiche a changé).
            Rapprocher(ouverts, numero.Value, a.Id, detectes, dateExtraction);
            return detectes.Count;
        }, cancellationToken);

        if (!resultat.IsSuccess)
        {
            throw new InvalidOperationException($"Mise à jour BCE de l'affilié {affilie.Id} impossible : {resultat.Error!.Message}");
        }

        // Sans changement de la fiche, le modificateur n'a rien validé : les écarts le sont ici (idempotent).
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlySet<string>> UnitesAutresAffiliesAsync(Affilie affilie, IReadOnlyList<UniteBce>? unites, CancellationToken cancellationToken)
    {
        var autres = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unite in unites ?? [])
        {
            NumeroUniteEtablissement numero;
            try
            {
                numero = new NumeroUniteEtablissement(unite.Numero);
            }
            catch (DomainException)
            {
                continue; // numéro invalide : relevé comme écart par le domaine
            }

            if (affilie.UnitesEtablissement.All(u => u.Numero != numero) && await affilies.UniteEtablissementUtiliseeAsync(numero, cancellationToken))
            {
                autres.Add(numero.Value);
            }
        }

        return autres;
    }

    /// <summary>Consigne les écarts détectés (sans doublon) et clôt ceux qui ne se présentent plus.</summary>
    private void Rapprocher(IReadOnlyList<EcartSynchronisation> ouverts, string numeroBce, Guid? affilieId, IReadOnlyList<EcartBce> detectes, DateOnly dateExtraction)
    {
        var maintenant = horloge.GetUtcNow();
        var rencontres = new HashSet<Guid>();
        foreach (var ecart in detectes.Select(e => e with { Reference = e.Reference.Length <= 100 ? e.Reference : e.Reference[..100] }).DistinctBy(e => (e.Code, e.Reference)))
        {
            var existant = ouverts.FirstOrDefault(o => o.Code == ecart.Code && o.Reference == ecart.Reference);
            if (existant is null)
            {
                ecarts.Add(EcartSynchronisation.Ouvrir(numeroBce, affilieId, ecart.Code, ecart.Reference, ecart.Detail, dateExtraction, maintenant));
            }
            else
            {
                existant.Redetecter(affilieId, ecart.Detail, dateExtraction, maintenant);
                rencontres.Add(existant.Id);
            }
        }

        foreach (var disparu in ouverts.Where(o => !rencontres.Contains(o.Id)))
        {
            disparu.Resoudre("system", maintenant);
        }
    }

    private static string Tronquer(string valeur) => valeur.Length <= 20 ? valeur : valeur[..20];
}

public sealed record EcartBceDto(
    Guid Id,
    string NumeroBce,
    Guid? AffilieId,
    string Code,
    string Reference,
    string Detail,
    DateOnly DateExtraction,
    DateTimeOffset DetecteLe,
    DateTimeOffset DerniereDetectionLe,
    StatutEcart Statut,
    DateTimeOffset? ResoluLe,
    string? ResoluPar)
{
    public static EcartBceDto De(EcartSynchronisation e) =>
        new(e.Id, e.NumeroBce, e.AffilieId, e.Code, e.Reference, e.Detail, e.DateExtraction, e.DetecteLe, e.DerniereDetectionLe, e.Statut, e.ResoluLe, e.ResoluPar);
}

/// <summary>Écarts de synchronisation BCE, les plus récents d'abord ; ouverts seulement par défaut.</summary>
public sealed record ListerEcartsBce(bool OuvertsSeulement = true);

public sealed class ListerEcartsBceHandler(IEcartSynchronisationRepository ecarts, ControleAcces acces) : IQueryHandler<ListerEcartsBce, IReadOnlyList<EcartBceDto>>
{
    public async Task<Result<IReadOnlyList<EcartBceDto>>> HandleAsync(ListerEcartsBce query, CancellationToken cancellationToken)
    {
        if (acces.VerifierGestion() is { } refus)
        {
            return refus;
        }

        var liste = await ecarts.ListAsync(query.OuvertsSeulement, cancellationToken);
        return liste.OrderByDescending(e => e.DerniereDetectionLe).Select(EcartBceDto.De).ToList();
    }
}

/// <summary>Le gestionnaire a traité l'écart (corrigé la fiche ou décidé de l'ignorer) : il n'est plus à signaler.</summary>
public sealed record ResoudreEcartBce(Guid EcartId);

public sealed class ResoudreEcartBceHandler(IEcartSynchronisationRepository ecarts, IUnitOfWork unitOfWork, ControleAcces acces, ICurrentUser utilisateur, TimeProvider horloge)
    : ICommandHandler<ResoudreEcartBce, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ResoudreEcartBce command, CancellationToken cancellationToken)
    {
        if (acces.VerifierGestion() is { } refus)
        {
            return refus;
        }

        var ecart = await ecarts.GetAsync(command.EcartId, cancellationToken);
        if (ecart is null)
        {
            return Error.NotFound("ecart-bce.inconnu", $"Écart BCE {command.EcartId} inconnu.");
        }

        ecart.Resoudre(utilisateur.UserId, horloge.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
