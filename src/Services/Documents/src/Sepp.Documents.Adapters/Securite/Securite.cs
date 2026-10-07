using System.Diagnostics;

using Microsoft.AspNetCore.Http;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Auditing;
using Sepp.Contracts.Audit;
using Sepp.Documents.Application;
using Sepp.Documents.Domain.Commun;

namespace Sepp.Documents.Adapters.Securite;

/// <summary>
/// Périmètre des externes lu dans le jeton (ADR 0005) : <c>affilie_id</c> (répété ou séparé par des virgules) pour
/// l'employeur et le SIPP, <c>personne_id</c> pour le travailleur (claim à fournir par le fournisseur d'identité lors de
/// la fédération CSAM du portail travailleur).
/// </summary>
internal sealed class PerimetreJeton(IHttpContextAccessor accessor) : IPerimetreExterne
{
    public const string ClaimAffilie = "affilie_id";
    public const string ClaimPersonne = "personne_id";

    public IReadOnlyCollection<Guid> Affilies =>
        accessor.HttpContext?.User.FindAll(ClaimAffilie)
            .SelectMany(c => c.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(v => Guid.TryParse(v, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet() ?? [];

    public Guid? PersonneId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(ClaimPersonne)?.Value, out var id) ? id : null;
}

/// <summary>
/// Traces d'accès aux documents (NF-04) écrites dans l'outbox du service, dans la zone du document : même contrat et même
/// rubrique que <see cref="OutboxAuditTrail"/> du socle, qui ne connaît qu'une zone par service.
/// </summary>
internal sealed class JournalAccesOutbox(IIntegrationEventOutbox outbox, IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider horloge) : IJournalAcces
{
    public const string Service = "documents";
    public const string ObjetType = "document";

    public void Enregistrer(ActionAudit action, ZoneDocument zone, Guid documentId, string? motif = null)
    {
        if (MotifAcces.Verifier(motif, brisDeGlace: false) is { } erreur)
        {
            throw new ArgumentException(erreur.Message, nameof(motif));
        }

        outbox.Add(new AccesDonneeSensible(
            Service,
            zone.Code(),
            currentUser.UserId,
            string.Join(',', currentUser.Roles.Order(StringComparer.Ordinal)),
            OutboxAuditTrail.Code(action),
            ObjetType,
            documentId,
            MotifAcces.Normaliser(motif),
            BrisDeGlace: false)
        {
            OccurredAt = horloge.GetUtcNow(),
            CorrelationId = Activity.Current?.TraceId.ToString(),
        });
    }

    public async Task EnregistrerLectureAsync(ZoneDocument zone, Guid documentId, string? motif, CancellationToken cancellationToken)
    {
        Enregistrer(ActionAudit.Lecture, zone, documentId, motif);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
