using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;
using Sepp.Communications.Domain.Messages;

namespace Sepp.Communications.Application.Expedition;

/// <summary>Demande de message issue d'un événement ou d'un envoi manuel.</summary>
/// <param name="CleBase">Identifie le fait métier : un même fait ne produit qu'un message, même s'il est annoncé par deux événements.</param>
/// <param name="CanalSouhaite">Canal demandé par l'émetteur (par ex. la Planification), s'il est joignable.</param>
/// <param name="RecommandeRequis">La loi impose un recommandé (SAN-11) : un envoi recommandé s'ajoute à l'envoi simple.</param>
/// <param name="Debut">Début du rendez-vous, pour les convocations et rappels.</param>
/// <param name="Resolu">Destinataire déjà résolu (alertes) ; sinon il est lu dans l'annuaire.</param>
public sealed record DemandeMessage(
    TypeMessage Type,
    TypeDestinataire TypeDestinataire,
    Guid DestinataireId,
    string ObjetType,
    Guid ObjetId,
    string CleBase,
    Canal? CanalSouhaite = null,
    bool RecommandeRequis = false,
    DateTimeOffset? Debut = null,
    Destinataire? Resolu = null);

/// <summary>
/// Création des messages d'un fait métier (DOC-03) : lecture du destinataire, choix du canal (souhait de l'émetteur,
/// préférence, cascade), gabarit sans donnée sensible pour l'e-mail et le SMS, création idempotente. Un destinataire
/// introuvable ou un recommandé impossible laisse un message abandonné, visible dans le journal, plutôt qu'une perte
/// silencieuse. L'unité de travail est validée par l'appelant.
/// </summary>
public sealed class CreateurMessages(IMessageRepository messages, IAnnuaireDestinataires annuaire, OptionsLiens liens, TimeProvider horloge)
{
    public async Task<IReadOnlyList<Message>> CreerAsync(DemandeMessage demande, CancellationToken cancellationToken)
    {
        var maintenant = horloge.GetUtcNow();
        var destinataire = demande.Resolu ?? await annuaire.ObtenirAsync(demande.TypeDestinataire, demande.DestinataireId, cancellationToken);
        if (destinataire is null)
        {
            return await EchecAsync(demande, Language.Fr, demande.CanalSouhaite ?? Canal.Portail, recommande: false, "destinataire-inconnu", cancellationToken);
        }

        var choisis = ChoixCanal.Determiner(demande.TypeDestinataire, destinataire.Joignabilite, demande.CanalSouhaite, destinataire.CanalPrefere, demande.RecommandeRequis);
        var crees = new List<Message>();
        if (choisis.Count == 0)
        {
            return await EchecAsync(demande, destinataire.Langue, demande.CanalSouhaite ?? Canal.Portail, demande.RecommandeRequis, "aucun-canal-joignable", cancellationToken);
        }

        foreach (var choix in choisis)
        {
            var cle = choix.Recommande ? $"{demande.CleBase}:recommande" : demande.CleBase;
            if (await messages.ExisteParCleAsync(cle, cancellationToken))
            {
                continue;
            }

            crees.Add(Creer(demande, destinataire.Langue, choix.Canal, choix.Recommande, cle, maintenant));
        }

        // SAN-11 : si le recommandé légal n'a pu être programmé (ni recommandé électronique ni adresse postale), on le signale.
        if (demande.RecommandeRequis && !ChoixCanal.RecommandeAssure(choisis))
        {
            var cle = $"{demande.CleBase}:recommande";
            if (!await messages.ExisteParCleAsync(cle, cancellationToken))
            {
                var echec = Creer(demande, destinataire.Langue, Canal.Courrier, recommande: true, cle, maintenant);
                echec.EnregistrerEchec(maintenant, "recommande-impossible", definitif: true, PolitiqueReprise.Defaut);
                crees.Add(echec);
            }
        }

        return crees;
    }

    private Message Creer(DemandeMessage demande, Language langue, Canal canal, bool recommande, string cle, DateTimeOffset maintenant)
    {
        // L'identifiant est connu avant la création : le lien de la notification contient cet identifiant opaque, jamais de donnée.
        var id = Message.NouvelIdentifiant();
        var contenu = Gabarits.Construire(demande.Type, canal, langue, demande.Debut, liens.Pour(demande.TypeDestinataire, id));
        var message = Message.Creer(new NouveauMessage(id, demande.Type, canal, recommande, demande.TypeDestinataire, demande.DestinataireId, langue,
            demande.ObjetType, demande.ObjetId, cle, contenu), maintenant);
        messages.Add(message);
        return message;
    }

    private async Task<IReadOnlyList<Message>> EchecAsync(DemandeMessage demande, Language langue, Canal canal, bool recommande, string code, CancellationToken cancellationToken)
    {
        var cle = demande.CleBase;
        if (await messages.ExisteParCleAsync(cle, cancellationToken))
        {
            return [];
        }

        var maintenant = horloge.GetUtcNow();
        var canalEffectif = recommande ? Canal.Courrier : canal;
        var message = Creer(demande, langue, canalEffectif, recommande, cle, maintenant);
        message.EnregistrerEchec(maintenant, code, definitif: true, PolitiqueReprise.Defaut);
        return [message];
    }
}
