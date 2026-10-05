using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.SurveillanceMedicale.Domain.Dossiers;
using Sepp.SurveillanceMedicale.Domain.Vaccinations;

namespace Sepp.SurveillanceMedicale.Application.Vaccinations;

/// <summary>
/// SAN-50, SAN-51 : administration d'une dose ; le lot (si précisé) est décompté du stock du centre et doit être
/// valide. <c>VaccinationAdministree</c> est publié pour l'enregistrement dans Vaccinnet ou e-Vax par le service
/// Intégrations (SAN-52).
/// </summary>
public sealed record AdministrerVaccin(Guid DossierId, string VaccinCode, int Dose, DateOnly? Date, Guid? LotId, string? Remarque)
{
    public override string ToString() => $"AdministrerVaccin {{ DossierId = {DossierId}, VaccinCode = {VaccinCode}, Dose = {Dose} }}";
}

public sealed class AdministrerVaccinHandler(
    GardeDossier garde, ILotVaccinRepository lots, ICurrentUser user, IIntegrationEventOutbox outbox, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<AdministrerVaccin, Guid>
{
    public async Task<Result<Guid>> HandleAsync(AdministrerVaccin command, CancellationToken cancellationToken)
    {
        var dossier = await garde.EcrireAsync(command.DossierId, PartiesDossier.Vaccinations, ActionAudit.Creation, cancellationToken);
        if (!dossier.IsSuccess)
        {
            return dossier.Error!;
        }

        LotVaccin? lot = null;
        if (command.LotId is { } lotId)
        {
            lot = await lots.GetAsync(lotId, cancellationToken);
            if (lot is null)
            {
                return Error.Validation("vaccination.lot-inconnu", "Lot de vaccins inconnu.");
            }
        }

        var date = command.Date ?? horloge.Aujourdhui();
        var resultat = Regles.Appliquer("vaccination.invalide", () =>
        {
            var code = (command.VaccinCode ?? string.Empty).Trim().ToUpperInvariant();
            lot?.Consommer(code, date);
            return dossier.Value.Vacciner(code, command.Dose, date, lot?.Id, lot?.CentreId, user.UserId, command.Remarque).Id;
        });
        if (!resultat.IsSuccess)
        {
            return resultat;
        }

        EvenementsIntegration.Publier(dossier.Value, outbox);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return resultat;
    }
}

public sealed record PoserTestTuberculinique(Guid DossierId, DateOnly? DatePose);

public sealed class PoserTestTuberculiniqueHandler(GardeDossier garde, ICurrentUser user, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<PoserTestTuberculinique, Guid>
{
    public async Task<Result<Guid>> HandleAsync(PoserTestTuberculinique command, CancellationToken cancellationToken)
    {
        var dossier = await garde.EcrireAsync(command.DossierId, PartiesDossier.Vaccinations, ActionAudit.Creation, cancellationToken);
        if (!dossier.IsSuccess)
        {
            return dossier.Error!;
        }

        var resultat = Regles.Appliquer("test-tuberculinique.invalide",
            () => dossier.Value.PoserTestTuberculinique(command.DatePose ?? horloge.Aujourdhui(), user.UserId).Id);
        if (resultat.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return resultat;
    }
}

public sealed record LireTestTuberculinique(Guid DossierId, Guid TestId, DateOnly? DateLecture, ResultatTuberculinique Resultat, int? IndurationMm)
{
    public override string ToString() => $"LireTestTuberculinique {{ DossierId = {DossierId}, TestId = {TestId} }}";
}

public sealed class LireTestTuberculiniqueHandler(GardeDossier garde, IUnitOfWork unitOfWork, TimeProvider horloge) : ICommandHandler<LireTestTuberculinique, Unit>
{
    public async Task<Result<Unit>> HandleAsync(LireTestTuberculinique command, CancellationToken cancellationToken)
    {
        var dossier = await garde.EcrireAsync(command.DossierId, PartiesDossier.Vaccinations, ActionAudit.Modification, cancellationToken);
        if (!dossier.IsSuccess)
        {
            return dossier.Error!;
        }

        var resultat = Regles.Appliquer("test-tuberculinique.invalide", () =>
        {
            dossier.Value.LireTestTuberculinique(
                command.TestId, command.DateLecture ?? horloge.Aujourdhui(), LectureTuberculinique.Creer(command.Resultat, command.IndurationMm));
        });
        if (resultat.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return resultat;
    }
}

/// <summary>Risques actuels de la personne : postes affectés à la date (projections Personnes) et profils de risques (Postes et risques).</summary>
public sealed class RisquesPersonne(IProjectionRepository projections)
{
    public sealed record PosteRisques(Guid PosteId, Guid? AffilieId, DateOnly DateDebut, DateOnly? DateFin, IReadOnlyList<string> CodesRisques);

    public async Task<IReadOnlyList<PosteRisques>> PostesAsync(Guid personneId, DateOnly date, CancellationToken cancellationToken)
    {
        var affectations = (await projections.ListerAffectationsAsync(personneId, cancellationToken)).Where(a => a.EstActiveAu(date)).ToList();
        var profils = await projections.ListerProfilsAsync([.. affectations.Select(a => a.PosteId).Distinct()], cancellationToken);
        return
        [
            .. affectations.Select(a =>
            {
                var profil = profils.Where(p => p.PosteId == a.PosteId && p.ValideDu <= date).MaxBy(p => p.ValideDu);
                return new PosteRisques(a.PosteId, profil?.AffilieId, a.DateDebut, a.DateFin, profil?.CodesRisques ?? []);
            }),
        ];
    }

    public async Task<IReadOnlyCollection<string>> CodesAsync(Guid personneId, DateOnly date, CancellationToken cancellationToken) =>
        (await PostesAsync(personneId, date, cancellationToken)).SelectMany(p => p.CodesRisques).ToHashSet(StringComparer.Ordinal);
}

public sealed record ObtenirRappels(Guid DossierId, DateOnly? Date);

public sealed class ObtenirRappelsHandler(GardeDossier garde, IProtocolesRepository protocoles, RisquesPersonne risques, TimeProvider horloge)
    : IQueryHandler<ObtenirRappels, IReadOnlyList<RappelVaccinal>>
{
    public async Task<Result<IReadOnlyList<RappelVaccinal>>> HandleAsync(ObtenirRappels query, CancellationToken cancellationToken)
    {
        var dossier = await garde.LireAsync(query.DossierId, PartiesDossier.Vaccinations, cancellationToken);
        if (!dossier.IsSuccess)
        {
            return dossier.Error!;
        }

        var date = query.Date ?? horloge.Aujourdhui();
        var codes = await risques.CodesAsync(dossier.Value.PersonneId, date, cancellationToken);
        return Result<IReadOnlyList<RappelVaccinal>>.Success(
            CalculRappels.Calculer(await protocoles.ListerSchemasVaccinauxAsync(cancellationToken), codes, dossier.Value, date));
    }
}

// ---- SAN-51 : stock de vaccins par centre (aucune donnée personnelle) ----

public sealed record LotVaccinDto(Guid Id, Guid CentreId, string VaccinCode, string NumeroLot, DateOnly Peremption, int QuantiteInitiale, int QuantiteRestante, bool Perime, bool PerimeBientot);

public sealed record StockVaccinDto(string VaccinCode, int Quantite, IReadOnlyList<LotVaccinDto> Lots);

public sealed record ReceptionnerLot(Guid CentreId, string VaccinCode, string NumeroLot, DateOnly Peremption, int Quantite);

public sealed class ReceptionnerLotHandler(ILotVaccinRepository lots, ICurrentUser user, IUnitOfWork unitOfWork, TimeProvider horloge) : ICommandHandler<ReceptionnerLot, Guid>
{
    public async Task<Result<Guid>> HandleAsync(ReceptionnerLot command, CancellationToken cancellationToken)
    {
        if (Regles.Permission(user, Permissions.StockVaccinsGerer, "Le stock de vaccins est géré par le personnel médical du centre.") is { } interdit)
        {
            return interdit;
        }

        var lot = Regles.Appliquer("lot-vaccin.invalide",
            () => LotVaccin.Receptionner(command.CentreId, command.VaccinCode, command.NumeroLot, command.Peremption, command.Quantite, horloge.Aujourdhui()));
        if (!lot.IsSuccess)
        {
            return lot.Error!;
        }

        lots.Add(lot.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return lot.Value.Id;
    }
}

public sealed record AjusterLot(Guid LotId, int Delta);

public sealed class AjusterLotHandler(ILotVaccinRepository lots, ICurrentUser user, IUnitOfWork unitOfWork) : ICommandHandler<AjusterLot, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AjusterLot command, CancellationToken cancellationToken)
    {
        if (Regles.Permission(user, Permissions.StockVaccinsGerer, "Le stock de vaccins est géré par le personnel médical du centre.") is { } interdit)
        {
            return interdit;
        }

        var lot = await lots.GetAsync(command.LotId, cancellationToken);
        if (lot is null)
        {
            return Error.NotFound("lot-vaccin.inconnu", "Lot de vaccins inconnu.");
        }

        var resultat = Regles.Appliquer("lot-vaccin.invalide", () => lot.Ajuster(command.Delta));
        if (resultat.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return resultat;
    }
}

/// <summary>SAN-51 : stock d'un centre par vaccin ; les lots périmés ou périmant dans les 30 jours sont signalés.</summary>
public sealed record ObtenirStockCentre(Guid CentreId, DateOnly? Date);

public sealed class ObtenirStockCentreHandler(ILotVaccinRepository lots, ICurrentUser user, TimeProvider horloge)
    : IQueryHandler<ObtenirStockCentre, IReadOnlyList<StockVaccinDto>>
{
    public const int AlertePeremptionJours = 30;

    public async Task<Result<IReadOnlyList<StockVaccinDto>>> HandleAsync(ObtenirStockCentre query, CancellationToken cancellationToken)
    {
        if (Regles.Permission(user, Permissions.StockVaccinsGerer, "Le stock de vaccins est géré par le personnel médical du centre.") is { } interdit)
        {
            return interdit;
        }

        var date = query.Date ?? horloge.Aujourdhui();
        var liste = await lots.ListerParCentreAsync(query.CentreId, cancellationToken);
        return liste.GroupBy(l => l.VaccinCode).OrderBy(g => g.Key).Select(g => new StockVaccinDto(
            g.Key,
            g.Where(l => !l.EstPerimeAu(date)).Sum(l => l.QuantiteRestante),
            [.. g.OrderBy(l => l.Peremption).Select(l => new LotVaccinDto(
                l.Id, l.CentreId, l.VaccinCode, l.NumeroLot, l.Peremption, l.QuantiteInitiale, l.QuantiteRestante,
                l.EstPerimeAu(date), !l.EstPerimeAu(date) && l.EstPerimeAu(date.AddDays(AlertePeremptionJours))))])).ToList();
    }
}
