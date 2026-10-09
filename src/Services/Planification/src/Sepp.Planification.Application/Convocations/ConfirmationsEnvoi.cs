using Sepp.BuildingBlocks.Application;
using Sepp.Contracts.Communications;
using Sepp.Contracts.Planification;

namespace Sepp.Planification.Application.Convocations;

/// <summary>
/// Retour de l'envoi effectif des convocations (SAN-10, SAN-11, ARC-33) : Communications annonce l'envoi
/// (<c>message-envoye</c>) ou l'abandon (<c>message-abandonne</c>) d'un message dont la référence d'origine est le
/// <c>ConvocationId</c>. Seuls les messages de convocation de rendez-vous et une convocation connue sont traités.
/// </summary>
internal static class RetourEnvoi
{
    public const string TypeMessageConvocation = "ConvocationRendezVous";

    public static bool Concerne(string typeMessage, Guid? referenceOrigineId) =>
        referenceOrigineId is not null && string.Equals(typeMessage, TypeMessageConvocation, StringComparison.Ordinal);
}

/// <summary>
/// La convocation est partie : elle est datée et <c>planification.convocation-envoyee</c> est publié pour Obligations
/// (obligations « convoquées »). Idempotent : un événement rejoué, ou le second message d'une convocation recommandée, ne
/// publie rien de plus.
/// </summary>
public sealed class MessageEnvoyeHandler(
    IConvocationRepository convocations,
    IRendezVousRepository rendezVous,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork) : IIntegrationEventHandler<MessageEnvoye>
{
    public async Task HandleAsync(MessageEnvoye integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        if (!RetourEnvoi.Concerne(e.TypeMessage, e.ReferenceOrigineId))
        {
            return;
        }

        var convocation = await convocations.GetAsync(e.ReferenceOrigineId!.Value, cancellationToken);
        if (convocation is null || !convocation.EnregistrerEnvoi(e.MessageId.ToString("D"), e.EnvoyeLe))
        {
            return;
        }

        var rdv = await rendezVous.GetAsync(convocation.RendezVousId, cancellationToken);
        outbox.Add(new ConvocationEnvoyee(convocation.Id, convocation.RendezVousId, convocation.PersonneId, convocation.AffilieId,
            rdv?.ObligationIds ?? [], e.Canal, e.Recommande, e.EnvoyeLe));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// La convocation n'a pas pu être remise : <c>planification.convocation-non-remise</c> est publié (alerte ou
/// replanification côté Obligations). Idempotent ; sans effet si la convocation est déjà partie par un autre message.
/// </summary>
public sealed class MessageAbandonneHandler(
    IConvocationRepository convocations,
    IRendezVousRepository rendezVous,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork) : IIntegrationEventHandler<MessageAbandonne>
{
    public async Task HandleAsync(MessageAbandonne integrationEvent, CancellationToken cancellationToken)
    {
        var e = integrationEvent;
        if (!RetourEnvoi.Concerne(e.TypeMessage, e.ReferenceOrigineId))
        {
            return;
        }

        var convocation = await convocations.GetAsync(e.ReferenceOrigineId!.Value, cancellationToken);
        if (convocation is null || !convocation.MarquerNonRemise(e.Date))
        {
            return;
        }

        var rdv = await rendezVous.GetAsync(convocation.RendezVousId, cancellationToken);
        outbox.Add(new ConvocationNonRemise(convocation.Id, convocation.RendezVousId, convocation.PersonneId, convocation.AffilieId,
            rdv?.ObligationIds ?? [], e.Canal, e.Recommande, e.Date));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
