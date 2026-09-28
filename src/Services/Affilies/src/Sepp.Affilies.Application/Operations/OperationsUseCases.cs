using Sepp.Affilies.Application.Affilies;
using Sepp.Affilies.Application.Securite;
using Sepp.Affilies.Domain.Affilies;
using Sepp.BuildingBlocks.Application;

namespace Sepp.Affilies.Application.Operations;

// AFF-06 — Fusion, scission et transfert entre SEPP (gestionnaire de dossiers). Chaque changement de statut publie
// OperationAffilieModifiee ; la réalisation publie aussi AffilieModifie (nouveau statut de l'affilié).
// Le transfert encadré des dossiers de santé est hors de ce service : la Surveillance médicale (SAN-42) réagit à l'événement.

/// <summary>
/// Projette une opération : <c>AffilieAbsorbantId</c> pour une fusion, <c>AffiliesBeneficiaires</c> pour une scission,
/// <c>SeppDestination</c> pour un transfert sortant.
/// </summary>
public sealed record ProjeterOperation(
    Guid AffilieId,
    TypeOperation Type,
    DateOnly DateEffet,
    Guid? AffilieAbsorbantId,
    IReadOnlyList<Guid>? AffiliesBeneficiaires,
    string? SeppDestination);

public sealed class ProjeterOperationHandler(ModificateurAffilie modificateur, IAffilieRepository affilies) : ICommandHandler<ProjeterOperation, Guid>
{
    public Task<Result<Guid>> HandleAsync(ProjeterOperation command, CancellationToken cancellationToken) =>
        modificateur.ModifierEtVerifierAsync(command.AffilieId, PartieFiche.Fiche, "operation.projetee", async affilie =>
        {
            OperationAffilie operation;
            switch (command.Type)
            {
                case TypeOperation.Fusion when command.AffilieAbsorbantId is { } absorbant:
                    if (absorbant != affilie.Id && !await affilies.ExisteAsync(absorbant, cancellationToken))
                    {
                        return Error.Validation("operation.absorbant-inconnu", $"Affilié absorbant {absorbant} inconnu.");
                    }

                    operation = affilie.ProjeterFusion(absorbant, command.DateEffet);
                    break;
                case TypeOperation.Scission when command.AffiliesBeneficiaires is { Count: > 0 } beneficiaires:
                    foreach (var beneficiaire in beneficiaires.Distinct().Where(b => b != affilie.Id))
                    {
                        if (!await affilies.ExisteAsync(beneficiaire, cancellationToken))
                        {
                            return Error.Validation("operation.beneficiaire-inconnu", $"Affilié bénéficiaire {beneficiaire} inconnu.");
                        }
                    }

                    operation = affilie.ProjeterScission(beneficiaires.ToList(), command.DateEffet);
                    break;
                case TypeOperation.TransfertSortant when !string.IsNullOrWhiteSpace(command.SeppDestination):
                    operation = affilie.ProjeterTransfertSortant(command.SeppDestination, command.DateEffet);
                    break;
                case TypeOperation.TransfertEntrant:
                    return Error.Validation("operation.transfert-entrant", "Un transfert entrant s'enregistre à l'affiliation (SEPP d'origine).");
                default:
                    return Error.Validation("operation.incomplete",
                        "Fusion : affilié absorbant requis ; scission : affiliés bénéficiaires requis ; transfert sortant : SEPP de destination requis.");
            }

            modificateur.Publier(ModificateurAffilie.OperationModifiee(affilie, operation));
            return Result<Guid>.Success(operation.Id);
        }, cancellationToken);
}

/// <summary>Réalise l'opération (à partir de sa date d'effet) : l'affiliation prend fin la veille.</summary>
public sealed record RealiserOperation(Guid AffilieId, Guid OperationId);

public sealed class RealiserOperationHandler(ModificateurAffilie modificateur) : ICommandHandler<RealiserOperation, Unit>
{
    public Task<Result<Unit>> HandleAsync(RealiserOperation command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Fiche, "operation.realisee", affilie =>
        {
            affilie.RealiserOperation(command.OperationId, modificateur.Aujourdhui);
            modificateur.Publier(ModificateurAffilie.OperationModifiee(affilie, affilie.Operations.Single(o => o.Id == command.OperationId)));
            return Unit.Value;
        }, cancellationToken);
}

public sealed record AnnulerOperation(Guid AffilieId, Guid OperationId);

public sealed class AnnulerOperationHandler(ModificateurAffilie modificateur) : ICommandHandler<AnnulerOperation, Unit>
{
    public Task<Result<Unit>> HandleAsync(AnnulerOperation command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Fiche, "operation.annulee", affilie =>
        {
            affilie.AnnulerOperation(command.OperationId);
            modificateur.Publier(ModificateurAffilie.OperationModifiee(affilie, affilie.Operations.Single(o => o.Id == command.OperationId)));
            return Unit.Value;
        }, cancellationToken);
}
