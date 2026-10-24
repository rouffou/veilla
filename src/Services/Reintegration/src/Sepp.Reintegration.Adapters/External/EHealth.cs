using System.Collections.Concurrent;

using Sepp.Reintegration.Application;
using Sepp.Reintegration.Domain.Echanges;

namespace Sepp.Reintegration.Adapters.External;

// SAN-64 : canal eHealth (eHealthBox ou service retenu) — formats et services réels à confirmer (INT-03, INT-04).
// Sélection par configuration : Reintegration:Adaptateurs:EHealth = Simulateur | Reel.

/// <summary>Documentation et autorisations à obtenir avant d'implémenter l'adaptateur eHealth réel.</summary>
public static class DocumentationAObtenir
{
    public const string EHealth =
        "Adaptateur eHealth réel non implémenté (INT-03, INT-04, SAN-64) : obtenir auprès de la plateforme eHealth la documentation " +
        "du service d'échange retenu (eHealthBox : version, schéma des messages, adressage du médecin traitant, du médecin-conseil " +
        "et du coordinateur Retour au travail), la délibération du Comité de sécurité de l'information, le certificat eHealth du SEPP " +
        "(Key Vault, jamais dans la configuration) et les règles d'accord du travailleur, puis traduire MessageEHealthSortant / " +
        "MessageEHealthRecu sans journaliser le contenu.";
}

/// <summary>Squelette de l'adaptateur réel : chaque appel lève <see cref="NotImplementedException"/> documentée (l'envoi reste en erreur, relançable).</summary>
public sealed class CanalEHealthReel : ICanalEHealth
{
    public Task<ResultatEnvoiEHealth> EnvoyerAsync(MessageEHealthSortant message, CancellationToken cancellationToken) =>
        throw new NotImplementedException(DocumentationAObtenir.EHealth);

    public Task<IReadOnlyList<MessageEHealthRecu>> RecevoirAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException(DocumentationAObtenir.EHealth);
}

/// <summary>
/// Simulateur eHealth déterministe (NF-14 : aucune donnée réelle) : tout envoi réussit (référence <c>SIM-EH-&lt;échange&gt;</c>),
/// sauf vers la référence de destinataire <see cref="DestinataireEnPanne"/> (échec technique reproductible). Chaque demande
/// d'information envoyée reçoit une réponse fictive, livrée une seule fois à la réception suivante.
/// </summary>
public sealed class CanalEHealthSimule(TimeProvider horloge) : ICanalEHealth
{
    public const string DestinataireEnPanne = "SIM-INDISPONIBLE";

    private readonly ConcurrentQueue<MessageEHealthRecu> _reponses = new();

    public Task<ResultatEnvoiEHealth> EnvoyerAsync(MessageEHealthSortant message, CancellationToken cancellationToken)
    {
        if (message.ReferenceDestinataire == DestinataireEnPanne)
        {
            return Task.FromResult(ResultatEnvoiEHealth.Echec("ehealth.destinataire-indisponible"));
        }

        var reference = $"SIM-EH-{message.EchangeId:N}";
        if (message.Objet == ObjetEchange.DemandeInformation)
        {
            _reponses.Enqueue(new MessageEHealthRecu($"SIM-EH-REP-{message.EchangeId:N}", reference, "Réponse simulée du correspondant eHealth.", horloge.GetUtcNow()));
        }

        return Task.FromResult(ResultatEnvoiEHealth.Succes(reference));
    }

    public Task<IReadOnlyList<MessageEHealthRecu>> RecevoirAsync(CancellationToken cancellationToken)
    {
        var recus = new List<MessageEHealthRecu>();
        while (_reponses.TryDequeue(out var message))
        {
            recus.Add(message);
        }

        return Task.FromResult<IReadOnlyList<MessageEHealthRecu>>(recus);
    }
}
