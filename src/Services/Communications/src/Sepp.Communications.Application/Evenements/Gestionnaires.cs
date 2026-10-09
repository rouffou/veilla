using System.Globalization;

using Sepp.BuildingBlocks.Application;
using Sepp.Communications.Application.Expedition;
using Sepp.Communications.Domain.Messages;
using Sepp.Contracts.Audit;
using Sepp.Contracts.Documents;
using Sepp.Contracts.Planification;

namespace Sepp.Communications.Application.Evenements;

/// <summary>
/// DOC-03 : un document est publié, le destinataire est averti par une notification et un lien, jamais par le document ni
/// son contenu. Le code du modèle et la zone ne figurent pas dans le message. L'exemplaire versé au dossier n'est jamais envoyé.
/// </summary>
public sealed class DocumentPublieHandler(CreateurMessages createur, IUnitOfWork unitOfWork) : IIntegrationEventHandler<DocumentPublie>
{
    public async Task HandleAsync(DocumentPublie integrationEvent, CancellationToken cancellationToken)
    {
        TypeDestinataire? type = integrationEvent.TypeDestinataire switch
        {
            "affilie" => TypeDestinataire.Affilie,
            "personne" => TypeDestinataire.Personne,
            _ => null,
        };
        if (type is null)
        {
            return;
        }

        await createur.CreerAsync(new DemandeMessage(TypeMessage.NotificationDocument, type.Value, integrationEvent.DestinataireId,
            "document", integrationEvent.DocumentId, $"document:{integrationEvent.DocumentId:D}"), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Clés d'idempotence des messages de rendez-vous : une convocation ne part qu'une fois, même si l'événement est rejoué.</summary>
internal static class ClesRendezVous
{
    public static string Convocation(Guid rendezVousId, DateTimeOffset debut, string type) =>
        $"rdv:{rendezVousId:D}:{debut.UtcDateTime.ToString("yyyyMMddTHHmm", CultureInfo.InvariantCulture)}:{type.Trim().ToLowerInvariant()}";
}

/// <summary>
/// SAN-10, SAN-11 : convocation émise par la Planification, par le canal qu'elle indique si le travailleur est joignable
/// par ce canal ; <c>Recommande</c> ajoute un envoi recommandé (recommandé électronique, à défaut courrier recommandé).
/// Seul événement qui crée une convocation : <c>planification.rendez-vous-planifie</c> n'en crée plus, car c'est la
/// Planification qui décide qui est convoqué (une réservation peut ne pas convoquer). L'identifiant de la convocation est
/// repris comme référence d'origine du message, pour que <c>message-envoye</c> et <c>message-abandonne</c> la rapprochent.
/// </summary>
public sealed class ConvocationEmiseHandler(CreateurMessages createur, IUnitOfWork unitOfWork) : IIntegrationEventHandler<ConvocationEmise>
{
    public async Task HandleAsync(ConvocationEmise integrationEvent, CancellationToken cancellationToken)
    {
        await createur.CreerAsync(new DemandeMessage(TypeMessage.ConvocationRendezVous, TypeDestinataire.Personne, integrationEvent.PersonneId,
            "rendez-vous", integrationEvent.RendezVousId,
            ClesRendezVous.Convocation(integrationEvent.RendezVousId, integrationEvent.Debut, integrationEvent.TypeConvocation),
            Canaux.Depuis(integrationEvent.Canal), integrationEvent.Recommande, integrationEvent.Debut,
            ReferenceOrigineId: integrationEvent.ConvocationId), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>SAN-13 : rappel automatique d'un rendez-vous (J-7, J-1), sauf si le rendez-vous a été annulé entre-temps.</summary>
public sealed class RappelRendezVousDuHandler(CreateurMessages createur, IMessageRepository messages, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<RappelRendezVousDu>
{
    public async Task HandleAsync(RappelRendezVousDu integrationEvent, CancellationToken cancellationToken)
    {
        var existants = await messages.ListerParObjetAsync("rendez-vous", integrationEvent.RendezVousId, cancellationToken);
        if (existants.Any(m => m.Type == TypeMessage.AnnulationRendezVous))
        {
            return;
        }

        await createur.CreerAsync(new DemandeMessage(TypeMessage.RappelRendezVous, TypeDestinataire.Personne, integrationEvent.PersonneId,
            "rendez-vous", integrationEvent.RendezVousId,
            $"rappel:{integrationEvent.RendezVousId:D}:{integrationEvent.Debut.UtcDateTime.ToString("yyyyMMddTHHmm", CultureInfo.InvariantCulture)}:{integrationEvent.NumeroRappel}",
            Canaux.Depuis(integrationEvent.Canal), RecommandeRequis: false, integrationEvent.Debut), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Un rendez-vous est annulé : les convocations et rappels qui ne sont pas encore partis sont annulés, et le travailleur
/// est prévenu. Le motif (un code) ne figure pas dans le message.
/// </summary>
public sealed class RendezVousAnnuleHandler(CreateurMessages createur, IMessageRepository messages, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<RendezVousAnnule>
{
    public async Task HandleAsync(RendezVousAnnule integrationEvent, CancellationToken cancellationToken)
    {
        var existants = await messages.ListerParObjetAsync("rendez-vous", integrationEvent.RendezVousId, cancellationToken);
        foreach (var message in existants.Where(m => m.Type is TypeMessage.ConvocationRendezVous or TypeMessage.RappelRendezVous
                                                      && m.Statut is StatutMessage.EnAttente or StatutMessage.EnEchec))
        {
            message.Annuler("rendez-vous-annule");
        }

        await createur.CreerAsync(new DemandeMessage(TypeMessage.AnnulationRendezVous, TypeDestinataire.Personne, integrationEvent.PersonneId,
            "rendez-vous", integrationEvent.RendezVousId, $"rdv-annule:{integrationEvent.RendezVousId:D}"), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// §3.3 : un accès « bris de glace » est signalé ; le CPMT dirigeant (zone médicale) ou le CPAP dirigeant (zone
/// psychosociale) est alerté par une notification générique et un lien vers le journal d'audit. Le message ne reprend ni
/// la zone, ni l'utilisateur, ni l'objet, ni le motif. Faute de dirigeant joignable, l'événement échoue pour être rejoué :
/// une alerte ne doit pas se perdre.
/// </summary>
public sealed class BrisDeGlaceSignaleHandler(CreateurMessages createur, IAnnuaireDestinataires annuaire, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<BrisDeGlaceSignale>
{
    public async Task HandleAsync(BrisDeGlaceSignale integrationEvent, CancellationToken cancellationToken)
    {
        var dirigeants = await annuaire.ResoudreDirigeantsAsync(integrationEvent.Zone, cancellationToken);
        if (dirigeants.Count == 0)
        {
            throw new InvalidOperationException($"Aucun dirigeant à alerter pour la zone {integrationEvent.Zone} (annuaire interne).");
        }

        foreach (var dirigeant in dirigeants)
        {
            await createur.CreerAsync(new DemandeMessage(TypeMessage.AlerteBrisDeGlace, TypeDestinataire.Interne, dirigeant.Id,
                "audit-entree", integrationEvent.EntreeAuditId, $"bris-de-glace:{integrationEvent.EntreeAuditId:D}:{dirigeant.Id:D}", Resolu: dirigeant), cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
