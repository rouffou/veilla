using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;
using Sepp.Communications.Domain.Messages;

namespace Sepp.Communications.Application.Expedition;

/// <summary>
/// Envoi des messages échus (DOC-03, DOC-05) : réservation du message (une seule instance l'envoie), lecture des
/// coordonnées auprès de l'annuaire, remise au port du canal, puis preuve d'envoi ou échec avec reprise. Chaque message est
/// validé séparément : la preuve d'un envoi réussi n'est jamais perdue à cause d'un autre message.
/// </summary>
public sealed class ExpediteurMessages(
    IMessageRepository messages,
    IAnnuaireDestinataires annuaire,
    IEnumerable<ICanalEnvoi> canaux,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    TimeProvider horloge)
{
    /// <summary>Durée pendant laquelle un message en cours d'envoi est réservé ; au-delà, un crash est présumé et il repart.</summary>
    public static readonly TimeSpan DureeReservation = TimeSpan.FromMinutes(5);

    public const int TailleLot = 50;

    public async Task<int> ExpedierEchusAsync(CancellationToken cancellationToken) =>
        await ExpedierEchusAsync(PolitiqueReprise.Defaut, cancellationToken);

    public async Task<int> ExpedierEchusAsync(PolitiqueReprise politique, CancellationToken cancellationToken)
    {
        var traites = 0;
        foreach (var message in await messages.ListerEchusAsync(horloge.GetUtcNow(), TailleLot, cancellationToken))
        {
            if (!await messages.ReserverAsync(message, horloge.GetUtcNow() + DureeReservation, cancellationToken))
            {
                continue;
            }

            await ExpedierAsync(message, politique, cancellationToken);
            traites++;
        }

        return traites;
    }

    private async Task ExpedierAsync(Message message, PolitiqueReprise politique, CancellationToken cancellationToken)
    {
        try
        {
            var resultat = await EnvoyerAsync(message, cancellationToken);
            message.EnregistrerEnvoi(resultat.Date, resultat.TypePreuve, resultat.Reference);
        }
        catch (ErreurEnvoiException ex)
        {
            message.EnregistrerEchec(horloge.GetUtcNow(), ex.Code, ex.Definitive, politique);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Le type de l'exception est le seul détail conservé : son message peut contenir une coordonnée du destinataire.
            message.EnregistrerEchec(horloge.GetUtcNow(), $"erreur-technique:{ex.GetType().Name}", definitif: false, politique);
        }

        // DOC-05, ARC-32 : message-envoye ou message-abandonne rejoint l'outbox dans la transaction de la preuve d'envoi.
        PublicationEvenementsMessage.Publier(message, outbox);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<ResultatEnvoi> EnvoyerAsync(Message message, CancellationToken cancellationToken)
    {
        var destinataire = await annuaire.ObtenirAsync(message.TypeDestinataire, message.DestinataireId, cancellationToken)
            ?? throw new ErreurEnvoiException("destinataire-inconnu", definitive: true);
        var canal = canaux.FirstOrDefault(c => c.Canal == message.Canal)
            ?? throw new ErreurEnvoiException("canal-non-configure", definitive: true);
        return await canal.EnvoyerAsync(
            new Envoi(message.Id, message.Canal, message.Recommande, message.Langue, message.Sujet, message.Corps, destinataire),
            cancellationToken);
    }
}
