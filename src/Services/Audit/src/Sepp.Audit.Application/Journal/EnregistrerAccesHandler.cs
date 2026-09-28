using Sepp.Audit.Domain.Journal;
using Sepp.BuildingBlocks.Application;
using Sepp.Contracts.Audit;

namespace Sepp.Audit.Application.Journal;

/// <summary>
/// Consommateur des traces d'accès publiées par les services (NF-04). Exécuté au plus une fois par message
/// grâce à l'inbox, dans la transaction de l'inbox (ARC-31) : l'entrée, son chaînage et l'éventuelle alerte
/// « bris de glace » sont validés ensemble.
/// </summary>
/// <remarks>
/// Une trace invalide (zone ou action inconnue, bris de glace sans motif) lève une exception : le message est
/// représenté puis placé en lettre morte pour analyse, jamais ignoré silencieusement.
/// </remarks>
public sealed class EnregistrerAccesHandler(IJournalAuditRepository journal, IIntegrationEventOutbox outbox)
    : IIntegrationEventHandler<AccesDonneeSensible>
{
    public async Task HandleAsync(AccesDonneeSensible integrationEvent, CancellationToken cancellationToken)
    {
        if (await journal.ExisteEvenementAsync(integrationEvent.EventId, cancellationToken))
        {
            return;
        }

        var trace = new TraceAcces(
            integrationEvent.EventId,
            integrationEvent.OccurredAt,
            CodesAudit.Parse<Zone>(integrationEvent.Zone),
            integrationEvent.Service,
            integrationEvent.UtilisateurId,
            integrationEvent.Role,
            CodesAudit.Parse<ActionAudit>(integrationEvent.Action),
            integrationEvent.ObjetType,
            integrationEvent.ObjetId,
            integrationEvent.Motif,
            integrationEvent.BrisDeGlace);

        var dernier = await journal.VerrouillerDernierMaillonAsync(trace.Zone, cancellationToken);
        var entree = EntreeAudit.Enregistrer(dernier, trace);
        journal.Ajouter(entree);

        if (entree.BrisDeGlace)
        {
            // §3.3 : alerte au CPMT dirigeant (zone médicale) ou au CPAP dirigeant (zone psychosociale).
            outbox.Add(new BrisDeGlaceSignale(entree.Id, entree.Zone.Code(), entree.Service, entree.UtilisateurId, entree.ObjetType, entree.ObjetId)
            {
                CorrelationId = integrationEvent.CorrelationId,
            });
        }
    }
}
