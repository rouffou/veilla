using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.PostesRisques;
using Sepp.PostesRisques.Domain.Postes;
using Sepp.PostesRisques.Domain.Surcharges;

namespace Sepp.PostesRisques.Application.Surcharges;

public sealed record SurchargeDto(
    Guid Id,
    Guid AffilieId,
    CibleSurcharge CibleType,
    Guid CibleId,
    string RisqueCode,
    int FrequenceMois,
    string Motif,
    string CpmtId,
    DateOnly ValideDu,
    DateOnly? ValideJusquAu)
{
    public static SurchargeDto From(SurchargeFrequence s) =>
        new(s.Id, s.AffilieId, s.CibleType, s.CibleId, s.RisqueCode, s.FrequenceMois, s.Motif, s.CpmtId, s.Validite.ValidFrom, s.Validite.ValidTo);
}

/// <summary>AFF-13 : surcharge de fréquence par le CPMT pour un poste, un groupe ou un travailleur, avec motif et période.</summary>
public sealed record DefinirSurchargeFrequence(
    Guid AffilieId,
    CibleSurcharge CibleType,
    Guid CibleId,
    string RisqueCode,
    int FrequenceMois,
    string Motif,
    DateOnly ValideDu,
    DateOnly? ValideJusquAu);

public sealed class DefinirSurchargeFrequenceHandler(
    ISurchargeFrequenceRepository surcharges,
    IRisqueRepository risques,
    IPosteRepository postes,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre) : ICommandHandler<DefinirSurchargeFrequence, Guid>
{
    public async Task<Result<Guid>> HandleAsync(DefinirSurchargeFrequence command, CancellationToken cancellationToken)
    {
        if (Acces.Verifier(currentUser, perimetre, command.AffilieId, Permissions.SurchargeFrequenceEcrire) is { } refus)
        {
            return refus;
        }

        var risque = string.IsNullOrWhiteSpace(command.RisqueCode)
            ? null
            : await risques.GetByCodeAsync(command.RisqueCode.Trim().ToUpperInvariant(), cancellationToken);
        if (risque is null)
        {
            return Error.Validation("risque.inconnu", $"Risque '{command.RisqueCode}' inconnu dans le référentiel.");
        }

        if (command.CibleType == CibleSurcharge.Poste)
        {
            var poste = await postes.GetAsync(command.CibleId, cancellationToken);
            if (poste is null || poste.AffilieId != command.AffilieId || poste.Statut != StatutPoste.Actif)
            {
                return Error.Validation("surcharge.cible-invalide", "Le poste ciblé n'existe pas, est archivé ou n'appartient pas à l'affilié.");
            }
        }

        SurchargeFrequence surcharge;
        try
        {
            surcharge = SurchargeFrequence.Definir(command.AffilieId, command.CibleType, command.CibleId, risque, command.FrequenceMois,
                command.Motif, currentUser.UserId, command.ValideDu, command.ValideJusquAu);
        }
        catch (DomainException ex)
        {
            return Error.Validation("surcharge.invalide", ex.Message);
        }

        var existantes = await surcharges.ListAsync(command.AffilieId, command.CibleType, command.CibleId, cancellationToken);
        if (existantes.Any(s => s.Chevauche(surcharge.CibleType, surcharge.CibleId, surcharge.RisqueId, surcharge.Validite)))
        {
            return Error.Conflict("surcharge.chevauchement", "Une surcharge existe déjà pour cette cible et ce risque sur la période : clôturez-la d'abord.");
        }

        surcharges.Add(surcharge);
        outbox.Add(Evenement(surcharge));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return surcharge.Id;
    }

    internal static SurchargeFrequenceDefinie Evenement(SurchargeFrequence s) =>
        new(s.Id, s.AffilieId, s.CibleType.ToString(), s.CibleId, s.RisqueCode, s.FrequenceMois, s.Validite.ValidFrom, s.Validite.ValidTo);
}

public sealed record CloturerSurchargeFrequence(Guid Id, DateOnly Fin);

public sealed class CloturerSurchargeFrequenceHandler(
    ISurchargeFrequenceRepository surcharges,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre) : ICommandHandler<CloturerSurchargeFrequence, Unit>
{
    public async Task<Result<Unit>> HandleAsync(CloturerSurchargeFrequence command, CancellationToken cancellationToken)
    {
        var surcharge = await surcharges.GetAsync(command.Id, cancellationToken);
        if (surcharge is null)
        {
            return Error.NotFound("surcharge.inconnue", $"Surcharge {command.Id} inconnue.");
        }

        if (Acces.Verifier(currentUser, perimetre, surcharge.AffilieId, Permissions.SurchargeFrequenceEcrire) is { } refus)
        {
            return refus;
        }

        try
        {
            surcharge.Cloturer(command.Fin);
        }
        catch (DomainException ex)
        {
            return Error.Validation("surcharge.invalide", ex.Message);
        }

        outbox.Add(DefinirSurchargeFrequenceHandler.Evenement(surcharge));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record ListerSurchargesFrequence(Guid AffilieId, CibleSurcharge? CibleType, Guid? CibleId, DateOnly? Date);

public sealed class ListerSurchargesFrequenceHandler(
    ISurchargeFrequenceRepository surcharges,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre) : IQueryHandler<ListerSurchargesFrequence, IReadOnlyList<SurchargeDto>>
{
    public async Task<Result<IReadOnlyList<SurchargeDto>>> HandleAsync(ListerSurchargesFrequence query, CancellationToken cancellationToken)
    {
        if (Acces.Verifier(currentUser, perimetre, query.AffilieId, Permissions.SurchargeFrequenceLire) is { } refus)
        {
            return refus;
        }

        var liste = await surcharges.ListAsync(query.AffilieId, query.CibleType, query.CibleId, cancellationToken);
        return liste.Where(s => query.Date is not { } date || s.Validite.Contains(date))
            .OrderBy(s => s.RisqueCode, StringComparer.Ordinal)
            .ThenBy(s => s.Validite.ValidFrom)
            .Select(SurchargeDto.From)
            .ToList();
    }
}
