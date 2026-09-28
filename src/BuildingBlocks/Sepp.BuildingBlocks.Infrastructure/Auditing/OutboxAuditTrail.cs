using System.Diagnostics;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Options;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Audit;

namespace Sepp.BuildingBlocks.Infrastructure.Auditing;

/// <summary>Identité du service émetteur des traces d'audit.</summary>
public sealed partial class AuditTrailOptions
{
    /// <summary>Nom du service en kebab-case, par ex. <c>surveillance-medicale</c>.</summary>
    public string Service { get; set; } = string.Empty;

    /// <summary>Zone de sensibilité du service (<see cref="ZonesSensibilite"/>).</summary>
    public string Zone { get; set; } = string.Empty;

    internal void Valider()
    {
        if (!NomService().IsMatch(Service))
        {
            throw new InvalidOperationException($"Nom de service d'audit invalide : '{Service}' (kebab-case attendu).");
        }

        if (!ZonesSensibilite.Toutes.Contains(Zone))
        {
            throw new InvalidOperationException($"Zone de sensibilité inconnue : '{Zone}'.");
        }
    }

    [GeneratedRegex("^[a-z]+(-[a-z]+)*$")]
    private static partial Regex NomService();
}

/// <summary>
/// Journal d'audit par l'outbox du service (NF-04, ARC-32) : la trace <see cref="AccesDonneeSensible"/> est publiée
/// sur la rubrique <c>audit</c> et consommée par le service Audit, qui la chaîne dans son journal.
/// </summary>
public sealed class OutboxAuditTrail(
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    IOptions<AuditTrailOptions> options) : IAuditTrail
{
    public void Enregistrer(ActionAudit action, string objetType, Guid objetId, string? motif = null, bool brisDeGlace = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objetType);
        if (objetId == Guid.Empty)
        {
            throw new ArgumentException("L'identifiant de l'objet accédé est obligatoire.", nameof(objetId));
        }

        if (MotifAcces.Verifier(motif, brisDeGlace) is { } erreur)
        {
            throw new ArgumentException(erreur.Message, nameof(motif));
        }

        var service = options.Value;
        outbox.Add(new AccesDonneeSensible(
            service.Service,
            service.Zone,
            currentUser.UserId,
            string.Join(',', currentUser.Roles.Order(StringComparer.Ordinal)),
            Code(action),
            objetType.Trim(),
            objetId,
            MotifAcces.Normaliser(motif),
            brisDeGlace)
        {
            OccurredAt = timeProvider.GetUtcNow(),
            CorrelationId = Activity.Current?.TraceId.ToString(),
        });
    }

    public async Task EnregistrerLectureAsync(string objetType, Guid objetId, string? motif, bool brisDeGlace, CancellationToken cancellationToken)
    {
        Enregistrer(ActionAudit.Lecture, objetType, objetId, motif, brisDeGlace);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Code de l'action dans le contrat : <c>lecture</c>, <c>creation</c>, <c>modification</c>, <c>suppression</c>, <c>export</c>.</summary>
    public static string Code(ActionAudit action) => action switch
    {
        ActionAudit.Lecture => "lecture",
        ActionAudit.Creation => "creation",
        ActionAudit.Modification => "modification",
        ActionAudit.Suppression => "suppression",
        ActionAudit.Export => "export",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };
}
