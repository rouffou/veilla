using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.Reintegration.Domain.Echanges;

namespace Sepp.Reintegration.Application.Echanges;

public sealed record EchangeDto(
    Guid Id, Guid IncapaciteId, Guid? TrajetId, DestinataireEHealth Destinataire, SensEchange Sens, ObjetEchange Objet, string Contenu,
    string? ReferenceDestinataire, bool AccordTravailleur, StatutEchange Statut, string? ReferenceEHealth, Guid? EnReponseA, int Tentatives,
    string? CodeErreur, DateTimeOffset CreeLe, DateTimeOffset? EnvoyeLe);

public static class EchangeProjections
{
    public static EchangeDto Dto(this EchangeEHealth e) => new(
        e.Id, e.IncapaciteId, e.TrajetId, e.Destinataire, e.Sens, e.Objet, e.Contenu, e.ReferenceDestinataire, e.AccordTravailleur, e.Statut,
        e.ReferenceEHealth, e.EnReponseA, e.Tentatives, e.CodeErreur, e.CreeLe, e.EnvoyeLe);
}

/// <summary>SAN-64 : message à un correspondant eHealth, envoyé par le service d'arrière-plan (en attente → envoyé ou en erreur).</summary>
public sealed record PreparerEchange(
    Guid IncapaciteId, Guid? TrajetId, DestinataireEHealth Destinataire, ObjetEchange Objet, string Contenu, string? ReferenceDestinataire, bool AccordTravailleur);

public sealed class PreparerEchangeHandler(GardeReintegration garde, ITrajetRepository trajets, IEchangeEHealthRepository echanges, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<PreparerEchange, Guid>
{
    public async Task<Result<Guid>> HandleAsync(PreparerEchange c, CancellationToken cancellationToken)
    {
        var acces = await garde.EcrireAsync(c.IncapaciteId, PartiesDossier.EHealth, ActionAudit.Creation, cancellationToken);
        if (!acces.IsSuccess)
        {
            return acces.Error!;
        }

        if (c.TrajetId is { } trajetId && (await trajets.GetAsync(trajetId, cancellationToken))?.IncapaciteId != c.IncapaciteId)
        {
            return GardeReintegration.TrajetInconnu;
        }

        var echange = Regles.Appliquer("reintegration.echange-invalide", () => EchangeEHealth.Preparer(
            c.IncapaciteId, c.TrajetId, acces.Value.PersonneId, c.Destinataire, c.Objet, c.Contenu, c.ReferenceDestinataire, c.AccordTravailleur, horloge.GetUtcNow()));
        if (!echange.IsSuccess)
        {
            return echange.Error!;
        }

        echanges.Add(echange.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return echange.Value.Id;
    }
}

public sealed record ListerEchanges(Guid IncapaciteId);

public sealed class ListerEchangesHandler(GardeReintegration garde, IEchangeEHealthRepository echanges) : IQueryHandler<ListerEchanges, IReadOnlyList<EchangeDto>>
{
    public async Task<Result<IReadOnlyList<EchangeDto>>> HandleAsync(ListerEchanges query, CancellationToken cancellationToken)
    {
        var acces = await garde.LireAsync(query.IncapaciteId, PartiesDossier.EHealth, cancellationToken);
        if (!acces.IsSuccess)
        {
            return acces.Error!;
        }

        return (await echanges.ListerParIncapaciteAsync(query.IncapaciteId, cancellationToken)).OrderBy(e => e.CreeLe).Select(e => e.Dto()).ToList();
    }
}

/// <summary>Relance manuelle d'un envoi en erreur ou abandonné (après correction).</summary>
public sealed record RelancerEchange(Guid EchangeId);

public sealed class RelancerEchangeHandler(GardeReintegration garde, IEchangeEHealthRepository echanges, IUnitOfWork unitOfWork) : ICommandHandler<RelancerEchange, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RelancerEchange c, CancellationToken cancellationToken)
    {
        if (garde.VerifierPermission(ActionAudit.Modification) is { } interdit)
        {
            return interdit;
        }

        var echange = await echanges.GetAsync(c.EchangeId, cancellationToken);
        if (echange is null)
        {
            return Error.NotFound("reintegration.echange-inconnu", "Échange eHealth inconnu.");
        }

        var acces = await garde.EcrireAsync(echange.IncapaciteId, PartiesDossier.EHealth, ActionAudit.Modification, cancellationToken);
        if (!acces.IsSuccess)
        {
            return acces.Error!;
        }

        var resultat = Regles.Appliquer("reintegration.echange-invalide", echange.Relancer);
        if (!resultat.IsSuccess)
        {
            return resultat;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return resultat;
    }
}

public sealed record ResultatEnvois(int Envoyes, int EnErreur);

/// <summary>
/// SAN-64 : envoi des échanges en attente par le port eHealth (service d'arrière-plan, compte « system »). Chaque lot est réservé
/// (<c>FOR UPDATE SKIP LOCKED</c>) dans la transaction de l'appelant. Le contenu n'est jamais journalisé ; un échec ne garde que
/// le code d'erreur et se retente jusqu'au maximum configuré (abandon : relance manuelle).
/// </summary>
public sealed class EnvoiEchangesEHealth(IEchangeEHealthRepository echanges, ICanalEHealth canal, ParametresReintegration parametres, IUnitOfWork unitOfWork, TimeProvider horloge)
{
    public const int TailleLot = 50;

    public async Task<ResultatEnvois> ExecuterLotAsync(CancellationToken cancellationToken)
    {
        var lot = await echanges.ReserverAEnvoyerAsync(TailleLot, cancellationToken);
        var (envoyes, erreurs) = (0, 0);
        foreach (var echange in lot)
        {
            ResultatEnvoiEHealth resultat;
            try
            {
                resultat = await canal.EnvoyerAsync(new MessageEHealthSortant(echange.Id, echange.Destinataire, echange.ReferenceDestinataire, echange.Objet, echange.PersonneId, echange.Contenu), cancellationToken);
            }
            catch (NotImplementedException)
            {
                resultat = ResultatEnvoiEHealth.Echec("ehealth.adaptateur-non-implemente");
            }
#pragma warning disable CA1031 // Échec technique du canal : consigné par son seul type (le message pourrait reprendre le contenu), retenté.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                resultat = ResultatEnvoiEHealth.Echec($"ehealth.erreur-technique.{ex.GetType().Name}");
            }

            if (resultat.Envoye)
            {
                echange.MarquerEnvoye(resultat.ReferenceEHealth!, horloge.GetUtcNow());
                envoyes++;
            }
            else
            {
                echange.MarquerEchec(resultat.CodeErreur ?? "ehealth.echec", Math.Max(parametres.Suivi.TentativesEnvoiMaximum, 1));
                erreurs++;
            }
        }

        if (lot.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new ResultatEnvois(envoyes, erreurs);
    }
}

/// <summary>SAN-64 : réception des réponses eHealth (idempotente par référence eHealth), rattachées à l'échange d'origine.</summary>
public sealed class ReceptionEchangesEHealth(IEchangeEHealthRepository echanges, ICanalEHealth canal, IUnitOfWork unitOfWork)
{
    public async Task<int> ExecuterAsync(CancellationToken cancellationToken)
    {
        var recus = 0;
        foreach (var message in await canal.RecevoirAsync(cancellationToken))
        {
            if (await echanges.GetParReferenceAsync(message.ReferenceEHealth, cancellationToken) is not null
                || await echanges.GetParReferenceAsync(message.ReferenceOrigine, cancellationToken) is not { Sens: SensEchange.Sortant } origine)
            {
                continue;
            }

            echanges.Add(EchangeEHealth.Recevoir(origine, message.ReferenceEHealth, message.Contenu, message.RecuLe));
            recus++;
        }

        if (recus > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return recus;
    }
}

/// <summary>Réception à la demande (en plus du passage périodique), par un professionnel de la zone médicale.</summary>
public sealed record RecevoirEchanges;

public sealed class RecevoirEchangesHandler(GardeReintegration garde, ReceptionEchangesEHealth reception) : ICommandHandler<RecevoirEchanges, int>
{
    public async Task<Result<int>> HandleAsync(RecevoirEchanges command, CancellationToken cancellationToken) =>
        garde.VerifierPermission(ActionAudit.Creation) is { } interdit ? interdit : await reception.ExecuterAsync(cancellationToken);
}
