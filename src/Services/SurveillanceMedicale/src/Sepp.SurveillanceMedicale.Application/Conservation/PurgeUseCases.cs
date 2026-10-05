using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.SurveillanceMedicale.Application.Consultation;
using Sepp.SurveillanceMedicale.Application.Dossiers;
using Sepp.SurveillanceMedicale.Domain.Conservation;
using Sepp.SurveillanceMedicale.Domain.Dossiers;

namespace Sepp.SurveillanceMedicale.Application.Conservation;

// SAN-44, NF-22 : purge contrôlée — liste proposée, validation humaine par le responsable du traitement (CPMT dirigeant,
// permission dossier-sante:purger), destruction physique tracée et preuve de destruction. Aucune destruction automatique.

public sealed record PreuveDestructionDto(Guid Id, Guid DossierId, Guid PersonneId, DateOnly DatePurgePrevue, DateTimeOffset DetruitLe, string ValideePar, string MotifValidation, int NombreElements, string Empreinte);

internal static class AccesPurge
{
    public static Error? Verifier(ICurrentUser user) =>
        Regles.Permission(user, Permissions.DossierSantePurger, "La purge des dossiers de santé est validée par le responsable du traitement (CPMT dirigeant).");
}

/// <summary>Propose à la purge les dossiers archivés dont la date de conservation est atteinte (aucune destruction).</summary>
public sealed record ProposerPurges(DateOnly? Date);

public sealed class ProposerPurgesHandler(IDossierSanteRepository dossiers, ICurrentUser user, GardeDossier garde, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<ProposerPurges, IReadOnlyList<DossierResumeDto>>
{
    public async Task<Result<IReadOnlyList<DossierResumeDto>>> HandleAsync(ProposerPurges command, CancellationToken cancellationToken)
    {
        if (AccesPurge.Verifier(user) is { } interdit)
        {
            return interdit;
        }

        var date = command.Date ?? horloge.Aujourdhui();
        var proposes = (await dossiers.ListerParStatutAsync(StatutArchivage.Archive, cancellationToken)).Where(d => d.PeutEtreProposeeALaPurge(date)).ToList();
        foreach (var dossier in proposes)
        {
            dossier.ProposerPurge(date);
            garde.Journaliser(ActionAudit.Modification, PartiesDossier.Conservation, dossier.Id);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return proposes.Select(d => d.Resume()).ToList();
    }
}

public sealed record ListerPurgesProposees;

public sealed class ListerPurgesProposeesHandler(IDossierSanteRepository dossiers, ICurrentUser user)
    : IQueryHandler<ListerPurgesProposees, IReadOnlyList<DossierResumeDto>>
{
    public async Task<Result<IReadOnlyList<DossierResumeDto>>> HandleAsync(ListerPurgesProposees query, CancellationToken cancellationToken)
    {
        if (AccesPurge.Verifier(user) is { } interdit)
        {
            return interdit;
        }

        return (await dossiers.ListerParStatutAsync(StatutArchivage.PurgeProposee, cancellationToken)).OrderBy(d => d.DatePurgePrevue).Select(d => d.Resume()).ToList();
    }
}

/// <summary>
/// Validation humaine de la destruction d'un dossier proposé à la purge : le dossier et tous les éléments qui le
/// référencent sont supprimés physiquement, une preuve de destruction (empreinte SHA-256 du contenu, volume, auteur)
/// est conservée et l'accès est journalisé (suppression).
/// </summary>
public sealed record ValiderPurge(Guid DossierId, string Motif);

public sealed class ValiderPurgeHandler(
    IDossierSanteRepository dossiers, IPurgeDossiers purge, ExportDossiers export, ICurrentUser user, IAuditTrail audit, TimeProvider horloge)
    : ICommandHandler<ValiderPurge, PreuveDestructionDto>
{
    public async Task<Result<PreuveDestructionDto>> HandleAsync(ValiderPurge command, CancellationToken cancellationToken)
    {
        if (AccesPurge.Verifier(user) is { } interdit)
        {
            return interdit;
        }

        if (MotifAcces.Verifier(command.Motif, brisDeGlace: true) is { } motifInvalide)
        {
            return Error.Validation("purge.motif-obligatoire", motifInvalide.Message.Replace("« bris de glace »", "de destruction", StringComparison.Ordinal));
        }

        var dossier = await dossiers.GetAsync(command.DossierId, cancellationToken);
        if (dossier is null)
        {
            return GardeDossier.Inconnu;
        }

        var verification = Regles.Appliquer("purge.impossible", dossier.VerifierDestructionPossible);
        if (!verification.IsSuccess)
        {
            return verification.Error!;
        }

        var contenu = await export.ConstruireAsync(dossier, cancellationToken);
        var (_, empreinte) = ExportDossiers.Serialiser(contenu);
        var preuve = Regles.Appliquer("purge.validation", () => PreuveDestruction.Etablir(
            dossier.Id, dossier.PersonneId, dossier.DatePurgePrevue!.Value, horloge.GetUtcNow(), user.UserId, command.Motif.Trim(),
            ExportDossiers.CompterElements(contenu), empreinte));
        if (!preuve.IsSuccess)
        {
            return preuve.Error!;
        }

        audit.Enregistrer(ActionAudit.Suppression, PartiesDossier.TypeObjet(PartiesDossier.Conservation), dossier.Id, command.Motif);
        await purge.DetruireAsync(dossier, preuve.Value, cancellationToken);
        var p = preuve.Value;
        return new PreuveDestructionDto(p.Id, p.DossierId, p.PersonneId, p.DatePurgePrevue, p.DetruitLe, p.ValideePar, p.MotifValidation, p.NombreElements, p.Empreinte);
    }
}

/// <summary>Refus de la destruction (par ex. procédure en cours) : la conservation est prolongée.</summary>
public sealed record RefuserPurge(Guid DossierId, int ProlongationAnnees, string Motif);

public sealed class RefuserPurgeHandler(IDossierSanteRepository dossiers, ICurrentUser user, IAuditTrail audit, IUnitOfWork unitOfWork)
    : ICommandHandler<RefuserPurge, DossierResumeDto>
{
    public async Task<Result<DossierResumeDto>> HandleAsync(RefuserPurge command, CancellationToken cancellationToken)
    {
        if (AccesPurge.Verifier(user) is { } interdit)
        {
            return interdit;
        }

        if (command.ProlongationAnnees is < 1 or > 50 || MotifAcces.Verifier(command.Motif, true) is not null)
        {
            return Error.Validation("purge.refus-invalide", "Précisez une prolongation (1 à 50 ans) et un motif (300 caractères au plus).");
        }

        var dossier = await dossiers.GetAsync(command.DossierId, cancellationToken);
        if (dossier is null)
        {
            return GardeDossier.Inconnu;
        }

        if (dossier.StatutArchivage != StatutArchivage.PurgeProposee)
        {
            return Error.Validation("purge.non-proposee", "Ce dossier n'est pas proposé à la purge.");
        }

        dossier.ReporterPurge(dossier.DatePurgePrevue!.Value.AddYears(command.ProlongationAnnees));
        audit.Enregistrer(ActionAudit.Modification, PartiesDossier.TypeObjet(PartiesDossier.Conservation), dossier.Id, command.Motif);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return dossier.Resume();
    }
}

public sealed record ListerPreuvesDestruction;

public sealed class ListerPreuvesDestructionHandler(IPurgeDossiers purge, ICurrentUser user) : IQueryHandler<ListerPreuvesDestruction, IReadOnlyList<PreuveDestructionDto>>
{
    public async Task<Result<IReadOnlyList<PreuveDestructionDto>>> HandleAsync(ListerPreuvesDestruction query, CancellationToken cancellationToken)
    {
        if (AccesPurge.Verifier(user) is { } interdit && !user.HasPermission(Permissions.AuditZoneMedicale))
        {
            return interdit;
        }

        return (await purge.ListerPreuvesAsync(cancellationToken)).OrderByDescending(p => p.DetruitLe)
            .Select(p => new PreuveDestructionDto(p.Id, p.DossierId, p.PersonneId, p.DatePurgePrevue, p.DetruitLe, p.ValideePar, p.MotifValidation, p.NombreElements, p.Empreinte))
            .ToList();
    }
}
