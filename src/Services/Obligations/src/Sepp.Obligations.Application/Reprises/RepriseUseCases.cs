using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Obligations.Application.Projections;
using Sepp.Obligations.Domain.Reprises;

namespace Sepp.Obligations.Application.Reprises;

/// <summary>Issue de l'enregistrement d'une reprise : <c>Cree</c> pour un nouveau processus, <c>Modifie</c> si seul le début d'absence a changé.</summary>
public sealed record ResultatEnregistrement(Guid RepriseId, bool Cree, bool Modifie, StatutReprise Statut);

/// <summary>
/// ARC-33, POR-04 : enregistrement idempotent d'une reprise, partagé par l'API (<see cref="EnregistrerRepriseHandler"/>) et
/// l'événement <c>bff-employeur.reprise-annoncee</c>. Même travailleur, affilié et date : même processus ; seul le début
/// d'absence différent : modification. Crée la reprise locale, le processus, puis recalcule dans la même transaction.
/// </summary>
public sealed class EnregistrementReprise(
    IProcessusRepriseRepository processus,
    IProjectionRepository projections,
    MiseAJourProjection miseAJour,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    OptionsReprise options,
    TimeProvider clock)
{
    public const string CodeOccupationInactive = "reprise.occupation-inactive";

    public async Task<Result<ResultatEnregistrement>> EnregistrerAsync(
        Guid personneId, Guid affilieId, DateOnly dateReprise, DateOnly debutAbsence, OrigineReprise origine, DateTimeOffset evenementDu,
        CancellationToken cancellationToken)
    {
        if (personneId == Guid.Empty || affilieId == Guid.Empty)
        {
            return Error.Validation("reprise.invalide", "Le travailleur et l'affilié sont obligatoires.");
        }

        if (debutAbsence > dateReprise)
        {
            return Error.Validation("reprise.absence-invalide", "Le début de l'absence ne peut pas suivre la date de reprise.");
        }

        var maintenant = clock.GetUtcNow();
        var existant = await processus.GetActifAsync(personneId, affilieId, dateReprise, cancellationToken);
        if ((existant is null || existant.DebutAbsence != debutAbsence) && !await projections.OccupationActiveAsync(personneId, affilieId, dateReprise, cancellationToken))
        {
            // POR-04, ARC-33 : pas de reprise sans occupation active chez l'affilié à la date de reprise (création et modification).
            // À valider : une projection d'occupation pas encore reçue (événements en désordre) est indiscernable d'une absence
            // d'occupation ; l'annonce est alors refusée tant que personnes.occupation-debutee n'est pas arrivé.
            return Error.Unprocessable(CodeOccupationInactive, "Le travailleur n'a pas d'occupation active chez cet affilié à la date de reprise.");
        }

        var local = await projections.GetRepriseAsync(personneId, affilieId, dateReprise, cancellationToken);

        if (existant is null && local is { Annulee: true } && evenementDu < local.EvenementDu)
        {
            // Annonce plus ancienne que l'annulation : sans effet.
            return new ResultatEnregistrement(local.RepriseId ?? Guid.Empty, false, false, StatutReprise.Annulee);
        }

        if (existant is not null)
        {
            return await ReannoncerAsync(existant, local, debutAbsence, evenementDu, maintenant, cancellationToken);
        }

        ProcessusReprise nouveau;
        try
        {
            nouveau = ProcessusReprise.Enregistrer(personneId, affilieId, dateReprise, debutAbsence, origine, maintenant);
        }
        catch (DomainException ex)
        {
            return Error.Validation("reprise.invalide", ex.Message);
        }

        if (local is null)
        {
            projections.Add(new Domain.Projections.RepriseLocale(personneId, affilieId, dateReprise, debutAbsence, evenementDu, nouveau.Id));
        }
        else
        {
            local.Reactiver(nouveau.Id, debutAbsence, evenementDu);
        }

        processus.Add(nouveau);
        var evenements = EvenementsReprise.Traduire(nouveau, options, null);
        try
        {
            // Premier enregistrement : l'unicité du processus actif départage deux annonces simultanées.
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DoublonProcessusRepriseException)
        {
            processus.AbandonnerChangements();
            var gagnant = await processus.GetActifAsync(personneId, affilieId, dateReprise, cancellationToken) ?? throw new InvalidOperationException("Doublon de processus de reprise introuvable.");
            return new ResultatEnregistrement(gagnant.Id, false, false, gagnant.Statut);
        }

        foreach (var evenement in evenements)
        {
            outbox.Add(evenement);
        }

        await miseAJour.TerminerAsync([personneId], cancellationToken);
        return new ResultatEnregistrement(nouveau.Id, true, false, nouveau.Statut);
    }

    private async Task<Result<ResultatEnregistrement>> ReannoncerAsync(
        ProcessusReprise existant, Domain.Projections.RepriseLocale? local, DateOnly debutAbsence, DateTimeOffset evenementDu, DateTimeOffset maintenant,
        CancellationToken cancellationToken)
    {
        var modifie = false;
        if (existant.DebutAbsence != debutAbsence && (local is null || evenementDu >= local.EvenementDu))
        {
            try
            {
                modifie = existant.Modifier(debutAbsence, maintenant);
            }
            catch (DomainException ex)
            {
                return Error.Conflict("reprise.modification-impossible", ex.Message);
            }

            if (local is null)
            {
                projections.Add(new Domain.Projections.RepriseLocale(existant.PersonneId, existant.AffilieId, existant.DateReprise, debutAbsence, evenementDu, existant.Id));
            }
            else
            {
                local.Appliquer(debutAbsence, evenementDu);
            }
        }

        if (modifie)
        {
            EvenementsReprise.Publier(existant, options, null, outbox);
            await miseAJour.TerminerAsync([existant.PersonneId], cancellationToken);
        }

        // Une annonce répétée sans changement n'écrit rien (l'enregistrement interrompu d'un processus est repris par le traitement périodique).
        return new ResultatEnregistrement(existant.Id, false, modifie, existant.Statut);
    }
}

public sealed record EnregistrerReprise(Guid PersonneId, Guid AffilieId, DateOnly DateReprise, DateOnly DebutAbsence);

/// <summary>Annonce d'une reprise (API, portail employeur via le BFF ou application interne) ; permission <c>reprise:annoncer</c> et périmètre de l'affilié.</summary>
public sealed class EnregistrerRepriseHandler(
    EnregistrementReprise enregistrement, IPerimetreAffilies perimetre, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<EnregistrerReprise, ResultatEnregistrement>
{
    public async Task<Result<ResultatEnregistrement>> HandleAsync(EnregistrerReprise command, CancellationToken cancellationToken)
    {
        if (Acces.Affilie(user, perimetre, command.AffilieId, Permissions.RepriseAnnoncer) is { } interdit)
        {
            return interdit;
        }

        return await enregistrement.EnregistrerAsync(
            command.PersonneId, command.AffilieId, command.DateReprise, command.DebutAbsence,
            perimetre.EstExterne ? OrigineReprise.PortailEmployeur : OrigineReprise.Interne, clock.GetUtcNow(), cancellationToken);
    }
}

/// <summary>Correction du début d'absence (la date de reprise est la clé : pour la changer, annuler puis annoncer de nouveau).</summary>
public sealed record ModifierReprise(Guid RepriseId, DateOnly DebutAbsence);

public sealed class ModifierRepriseHandler(
    EnregistrementReprise enregistrement, IProcessusRepriseRepository processus, IPerimetreAffilies perimetre, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<ModifierReprise, ResultatEnregistrement>
{
    public async Task<Result<ResultatEnregistrement>> HandleAsync(ModifierReprise command, CancellationToken cancellationToken)
    {
        if (Acces.Permission(user, Permissions.RepriseGerer) is { } interdit)
        {
            return interdit;
        }

        var p = await processus.GetAsync(command.RepriseId, cancellationToken);
        if (p is null)
        {
            return Acces.RepriseInconnue(command.RepriseId);
        }

        if (Acces.Affilie(user, perimetre, p.AffilieId, Permissions.RepriseGerer) is { } horsPerimetre)
        {
            return horsPerimetre;
        }

        if (p.EstAnnulee)
        {
            return Error.Conflict("reprise.modification-impossible", "Une reprise annulée ne peut plus être modifiée.");
        }

        return await enregistrement.EnregistrerAsync(p.PersonneId, p.AffilieId, p.DateReprise, command.DebutAbsence, p.Origine, clock.GetUtcNow(), cancellationToken);
    }
}

/// <summary>Codes de motif d'annulation d'une reprise, jamais un texte libre (ARC-06). Codes provisoires, à valider.</summary>
public enum MotifAnnulationReprise
{
    ErreurDeSaisie,
    RepriseReportee,
    Autre,
}

/// <summary>
/// Compensation (ARC-33) : annule la reprise, donc l'obligation d'examen (via le recalcul) et, par <c>ObligationCloturee</c>,
/// le rendez-vous qui ne couvre plus d'obligation ouverte. Refusée (409) une fois l'examen clôturé.
/// </summary>
public sealed record AnnulerReprise(Guid RepriseId, string? Motif);

public sealed class AnnulerRepriseHandler(
    IProcessusRepriseRepository processus,
    IProjectionRepository projections,
    MiseAJourProjection miseAJour,
    IIntegrationEventOutbox outbox,
    OptionsReprise options,
    IPerimetreAffilies perimetre,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<AnnulerReprise, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AnnulerReprise command, CancellationToken cancellationToken)
    {
        if (Acces.Permission(user, Permissions.RepriseGerer) is { } interdit)
        {
            return interdit;
        }

        var motif = MotifAnnulationReprise.Autre;
        if (command.Motif is not null && !Enumerations.TryParse(command.Motif, out motif))
        {
            return Error.Validation("reprise.motif-invalide", "Le motif d'annulation doit être ErreurDeSaisie, RepriseReportee ou Autre.");
        }

        var p = await processus.GetAsync(command.RepriseId, cancellationToken);
        if (p is null)
        {
            return Acces.RepriseInconnue(command.RepriseId);
        }

        if (Acces.Affilie(user, perimetre, p.AffilieId, Permissions.RepriseGerer) is { } horsPerimetre)
        {
            return horsPerimetre;
        }

        var maintenant = clock.GetUtcNow();
        try
        {
            if (!p.Annuler(motif.ToString(), maintenant))
            {
                return Unit.Value;
            }
        }
        catch (DomainException ex)
        {
            return Error.Conflict("reprise.annulation-impossible", ex.Message);
        }

        var local = await projections.GetRepriseAsync(p.PersonneId, p.AffilieId, p.DateReprise, cancellationToken);
        local?.MarquerAnnulee(maintenant);
        EvenementsReprise.Publier(p, options, null, outbox);
        await miseAJour.TerminerAsync([p.PersonneId], cancellationToken);
        return Unit.Value;
    }
}

public sealed record RepriseDto(
    Guid Id,
    Guid PersonneId,
    Guid AffilieId,
    DateOnly DateReprise,
    DateOnly DebutAbsence,
    string Origine,
    string Statut,
    Guid? ObligationId,
    DateOnly? DateLimite,
    bool EnRetard,
    bool HorsDelai,
    Guid? RendezVousId,
    DateTimeOffset? DebutRendezVous,
    int NombreAbsences,
    DateTimeOffset? ConvocationEnvoyeeLe,
    bool ConvocationNonRemise,
    bool UrgenceNonCouverte,
    bool ReplanificationRequise,
    Guid? ExamenId,
    DateOnly? ExamenLe,
    Guid? DecisionId,
    DateTimeOffset? DecisionLe,
    Guid? DocumentEmployeurId,
    DateTimeOffset? AnnuleeLe,
    string? MotifAnnulation,
    DateOnly? ProchaineEcheance,
    string? TypeMinuterie,
    IReadOnlyList<AlerteRepriseDto> Alertes)
{
    /// <summary>
    /// Un profil externe (employeur, SIPP) voit le statut et les dates, mais pas les identifiants de l'examen et de la décision
    /// (minimisation, ARC-06) : ce que voit l'employeur est à valider (POR-04).
    /// </summary>
    public static RepriseDto De(ProcessusReprise p, DateOnly aujourdHui, bool externe) =>
        new(
            p.Id, p.PersonneId, p.AffilieId, p.DateReprise, p.DebutAbsence, p.Origine.ToString(), p.Statut.ToString(), p.ObligationId, p.DateLimite,
            p.EnRetardAu(aujourdHui), p.HorsDelai, p.RendezVousId, p.DebutRendezVous, p.NombreAbsences, p.ConvocationEnvoyeeLe,
            p.ConvocationNonRemise, p.UrgenceNonCouverte, p.ReplanificationRequise,
            externe ? null : p.ExamenId, p.ExamenLe, externe ? null : p.DecisionId, p.DecisionLe, p.DocumentEmployeurId,
            p.AnnuleeLe, p.MotifAnnulation, p.ProchaineEcheance, p.TypeMinuterie?.ToString(),
            [.. p.Alertes().Select(a => new AlerteRepriseDto(a.Type.ToString(), a.Depuis, a.Message))]);
}

public sealed record AlerteRepriseDto(string Type, DateOnly? Depuis, string Message);

public sealed record ObtenirReprise(Guid RepriseId);

public sealed class ObtenirRepriseHandler(
    IProcessusRepriseRepository processus, IPerimetreAffilies perimetre, ICurrentUser user, TimeProvider clock)
    : IQueryHandler<ObtenirReprise, RepriseDto>
{
    public async Task<Result<RepriseDto>> HandleAsync(ObtenirReprise query, CancellationToken cancellationToken)
    {
        if (Acces.Permission(user, Permissions.RepriseLire) is { } interdit)
        {
            return interdit;
        }

        var p = await processus.GetAsync(query.RepriseId, cancellationToken);
        return p is null || !perimetre.PeutAcceder(p.AffilieId)
            ? Acces.RepriseInconnue(query.RepriseId)
            : RepriseDto.De(p, clock.AujourdHui(), perimetre.EstExterne);
    }
}

/// <summary>Suivi des reprises (POR-04) : filtre par affilié, statut, et par date limite au plus tard à la date donnée (échéances à surveiller).</summary>
public sealed record ListerReprises(Guid? AffilieId, StatutReprise? Statut, DateOnly? EcheanceAvant);

public sealed class ListerReprisesHandler(
    IProcessusRepriseRepository processus, IPerimetreAffilies perimetre, ICurrentUser user, TimeProvider clock)
    : IQueryHandler<ListerReprises, IReadOnlyList<RepriseDto>>
{
    public const int NombreMaximum = 500;

    public async Task<Result<IReadOnlyList<RepriseDto>>> HandleAsync(ListerReprises query, CancellationToken cancellationToken)
    {
        if (Acces.Permission(user, Permissions.RepriseLire) is { } sansDroit)
        {
            return sansDroit;
        }

        if (query.AffilieId is { } cible && !perimetre.PeutAcceder(cible))
        {
            return Error.Forbidden("perimetre.interdit", "Cet affilié ne fait pas partie de votre périmètre.");
        }

        var aujourdHui = clock.AujourdHui();
        var liste = await processus.ListAsync(query.AffilieId, query.Statut, query.EcheanceAvant, NombreMaximum, cancellationToken);
        return liste
            .Where(p => perimetre.PeutAcceder(p.AffilieId))
            .Select(p => RepriseDto.De(p, aujourdHui, perimetre.EstExterne))
            .ToList();
    }
}
