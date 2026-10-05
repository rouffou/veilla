using System.Security.Cryptography;
using System.Text;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.SurveillanceMedicale.Application.Consultation;
using Sepp.SurveillanceMedicale.Domain.Dossiers;
using Sepp.SurveillanceMedicale.Domain.Transferts;

namespace Sepp.SurveillanceMedicale.Application.Transferts;

// SAN-42 : transfert sécurisé du dossier de santé vers / depuis un autre SEPP ou SIPP. Le paquet est l'export structuré
// (SAN-43), chiffré au repos avec les clés de la zone médicale et contrôlé par son empreinte SHA-256. Le canal réel
// (eHealthBox ou équivalent entre services de prévention) est à confirmer : port ICanalTransfertDossier + simulateur.

public sealed record TransfertDto(
    Guid Id, Guid DossierId, Guid PersonneId, DirectionTransfert Direction, string Contrepartie, MotifTransfert Motif, StatutTransfert Statut,
    DateOnly DateDemande, string DemandePar, string? Empreinte, string? ReferenceCanal, DateOnly? DateTransmission, DateOnly? DateIntegration);

internal static class Transferts
{
    public static TransfertDto Dto(this TransfertDossier t) => new(
        t.Id, t.DossierId, t.PersonneId, t.Direction, t.Contrepartie, t.Motif, t.Statut, t.DateDemande, t.DemandePar, t.Empreinte, t.ReferenceCanal,
        t.DateTransmission, t.DateIntegration);

    public static string Empreinte(string paquet) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(paquet)));
}

public sealed record DemanderTransfert(Guid DossierId, string Contrepartie, MotifTransfert Motif);

public sealed class DemanderTransfertHandler(GardeDossier garde, ITransfertRepository transferts, ICurrentUser user, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<DemanderTransfert, Guid>
{
    public async Task<Result<Guid>> HandleAsync(DemanderTransfert command, CancellationToken cancellationToken)
    {
        var dossier = await garde.EcrireAsync(command.DossierId, PartiesDossier.Transfert, ActionAudit.Creation, cancellationToken);
        if (!dossier.IsSuccess)
        {
            return dossier.Error!;
        }

        var transfert = Regles.Appliquer("transfert.invalide",
            () => TransfertDossier.Demander(dossier.Value.Id, dossier.Value.PersonneId, command.Contrepartie, command.Motif, horloge.Aujourdhui(), user.UserId));
        if (!transfert.IsSuccess)
        {
            return transfert.Error!;
        }

        transferts.Add(transfert.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return transfert.Value.Id;
    }
}

/// <summary>Export du dossier dans le paquet chiffré, puis transmission par le canal sécurisé (accès journalisé comme export).</summary>
public sealed record TransmettreTransfert(Guid TransfertId);

public sealed class TransmettreTransfertHandler(
    ITransfertRepository transferts, GardeDossier garde, ExportDossiers export, ICanalTransfertDossier canal, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<TransmettreTransfert, TransfertDto>
{
    public async Task<Result<TransfertDto>> HandleAsync(TransmettreTransfert command, CancellationToken cancellationToken)
    {
        if (garde.VerifierPermission(ActionAudit.Modification) is { } interdit)
        {
            return interdit;
        }

        var transfert = await transferts.GetAsync(command.TransfertId, cancellationToken);
        if (transfert is null)
        {
            return Error.NotFound("transfert.inconnu", "Transfert inconnu.");
        }

        var dossier = await garde.EcrireAsync(transfert.DossierId, PartiesDossier.Transfert, ActionAudit.Export, cancellationToken);
        if (!dossier.IsSuccess)
        {
            return dossier.Error!;
        }

        var (paquet, empreinte) = ExportDossiers.Serialiser(await export.ConstruireAsync(dossier.Value, cancellationToken));
        var exporte = Regles.Appliquer("transfert.invalide", () => transfert.Exporter(paquet, empreinte));
        if (!exporte.IsSuccess)
        {
            return exporte.Error!;
        }

        var reference = await canal.TransmettreAsync(transfert.Contrepartie, paquet, empreinte, cancellationToken);
        transfert.MarquerTransmis(reference, horloge.Aujourdhui());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return transfert.Dto();
    }
}

/// <summary>
/// Réception d'un dossier entrant (déposé par l'adaptateur du canal) : contrôle d'intégrité par l'empreinte, ouverture
/// du dossier local si nécessaire, conservation du paquet chiffré en attente d'intégration par le CPMT.
/// </summary>
public sealed record RecevoirDossier(Guid PersonneId, string Contrepartie, MotifTransfert Motif, string Paquet, string Empreinte, string ReferenceCanal)
{
    public override string ToString() => $"RecevoirDossier {{ PersonneId = {PersonneId}, Contrepartie = {Contrepartie} }}";
}

public sealed class RecevoirDossierHandler(
    IDossierSanteRepository dossiers, ITransfertRepository transferts, GardeDossier garde, ICurrentUser user,
    IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<RecevoirDossier, Guid>
{
    public async Task<Result<Guid>> HandleAsync(RecevoirDossier command, CancellationToken cancellationToken)
    {
        if (garde.VerifierPermission(ActionAudit.Creation) is { } interdit)
        {
            return interdit;
        }

        if (string.IsNullOrWhiteSpace(command.Paquet) || !string.Equals(Transferts.Empreinte(command.Paquet), command.Empreinte?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation("transfert.integrite", "L'empreinte du paquet reçu ne correspond pas à son contenu.");
        }

        var date = horloge.Aujourdhui();
        var dossier = await dossiers.GetParPersonneAsync(command.PersonneId, cancellationToken);
        if (dossier is null)
        {
            dossier = DossierSante.Ouvrir(command.PersonneId, null, date);
            dossiers.Add(dossier);
            garde.Journaliser(ActionAudit.Creation, PartiesDossier.Dossier, dossier.Id);
        }

        var transfert = Regles.Appliquer("transfert.invalide", () => TransfertDossier.Recevoir(
            dossier.Id, command.PersonneId, command.Contrepartie, command.Motif, date, user.UserId, command.Paquet, command.Empreinte!.Trim().ToUpperInvariant(), command.ReferenceCanal));
        if (!transfert.IsSuccess)
        {
            return transfert.Error!;
        }

        transferts.Add(transfert.Value);
        garde.Journaliser(ActionAudit.Creation, PartiesDossier.Transfert, dossier.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return transfert.Value.Id;
    }
}

public sealed record IntegrerTransfert(Guid TransfertId);

public sealed class IntegrerTransfertHandler(ITransfertRepository transferts, GardeDossier garde, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<IntegrerTransfert, TransfertDto>
{
    public async Task<Result<TransfertDto>> HandleAsync(IntegrerTransfert command, CancellationToken cancellationToken)
    {
        if (garde.VerifierPermission(ActionAudit.Modification) is { } interdit)
        {
            return interdit;
        }

        var transfert = await transferts.GetAsync(command.TransfertId, cancellationToken);
        if (transfert is null)
        {
            return Error.NotFound("transfert.inconnu", "Transfert inconnu.");
        }

        var dossier = await garde.EcrireAsync(transfert.DossierId, PartiesDossier.Transfert, ActionAudit.Modification, cancellationToken);
        if (!dossier.IsSuccess)
        {
            return dossier.Error!;
        }

        var resultat = Regles.Appliquer("transfert.invalide", () => transfert.Integrer(horloge.Aujourdhui()));
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return transfert.Dto();
    }
}

public sealed record ObtenirTransfert(Guid TransfertId);

public sealed class ObtenirTransfertHandler(ITransfertRepository transferts, GardeDossier garde) : IQueryHandler<ObtenirTransfert, TransfertDto>
{
    public async Task<Result<TransfertDto>> HandleAsync(ObtenirTransfert query, CancellationToken cancellationToken)
    {
        if (garde.VerifierPermission(ActionAudit.Lecture) is { } interdit)
        {
            return interdit;
        }

        var transfert = await transferts.GetAsync(query.TransfertId, cancellationToken);
        if (transfert is null)
        {
            return Error.NotFound("transfert.inconnu", "Transfert inconnu.");
        }

        var dossier = await garde.LireAsync(transfert.DossierId, PartiesDossier.Transfert, cancellationToken);
        return dossier.IsSuccess ? transfert.Dto() : dossier.Error!;
    }
}
