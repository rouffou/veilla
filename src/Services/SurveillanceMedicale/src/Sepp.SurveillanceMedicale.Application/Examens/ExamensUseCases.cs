using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Examens;
using Sepp.SurveillanceMedicale.Domain;
using Sepp.SurveillanceMedicale.Domain.Examens;
using Sepp.SurveillanceMedicale.Domain.Projections;

namespace Sepp.SurveillanceMedicale.Application.Examens;

public sealed record ObservationDto(string? Anamnese, string? ExamenClinique, DateTimeOffset SaisieLe);

public sealed record ResultatActeDto(Guid Id, TypeActe TypeActe, DateOnly Date, SourceResultat Source, bool Inhabituel, string? Commentaire, IReadOnlyList<MesureEvaluee> Mesures);

public sealed record PropositionFrequenceDto(Guid Id, Guid ResultatActeId, DateOnly DateProposition, StatutProposition Statut, string? DecidePar, DateOnly? DateDecision);

public sealed record ExamenDto(
    Guid Id,
    Guid DossierId,
    Guid PersonneId,
    Guid AffilieId,
    string TypeExamen,
    DateOnly Date,
    string ProfessionnelId,
    Guid? RendezVousId,
    IReadOnlyList<Guid> ObligationIds,
    StatutExamen Statut,
    DateOnly? DateCloture,
    bool? HorsDelaiLegal,
    ObservationDto? Observation,
    IReadOnlyList<ResultatActeDto> Resultats,
    IReadOnlyList<PropositionFrequenceDto> PropositionsFrequence);

public static class ExamenProjections
{
    public static ExamenDto Dto(this Examen e) => new(
        e.Id, e.DossierId, e.PersonneId, e.AffilieId, e.TypeExamen, e.Date, e.ProfessionnelId, e.RendezVousId, e.ObligationIds, e.Statut, e.DateCloture,
        e.HorsDelaiLegal,
        e.Observation is { } o ? new ObservationDto(o.Anamnese, o.ExamenClinique, o.SaisieLe) : null,
        [.. e.Resultats.OrderBy(r => r.Date).Select(r => r.Dto())],
        [.. e.PropositionsFrequence.Select(p => new PropositionFrequenceDto(p.Id, p.ResultatActeId, p.DateProposition, p.Statut, p.DecidePar, p.DateDecision))]);

    public static ResultatActeDto Dto(this ResultatActe r) => new(r.Id, r.TypeActe, r.Date, r.Source, r.Inhabituel, r.Commentaire, r.Mesures);
}

/// <summary>Chargement d'un examen et contrôle d'accès à son dossier (secret médical).</summary>
public sealed class AccesExamens(IExamenRepository examens, GardeDossier garde)
{
    public static readonly Error Inconnu = Error.NotFound("examen.inconnu", "Examen inconnu.");

    public async Task<Result<Examen>> ChargerAsync(Guid examenId, ActionAudit action, CancellationToken cancellationToken)
    {
        if (garde.VerifierPermission(action) is { } interdit)
        {
            return interdit;
        }

        var examen = await examens.GetAsync(examenId, cancellationToken);
        if (examen is null)
        {
            return Inconnu;
        }

        var acces = action == ActionAudit.Lecture
            ? await garde.LireAsync(examen.DossierId, PartiesDossier.Examen, cancellationToken)
            : await garde.EcrireAsync(examen.DossierId, PartiesDossier.Examen, action, cancellationToken);
        return acces.IsSuccess ? examen : acces.Error!;
    }
}

/// <summary>
/// Ouverture d'un examen dans le dossier. Sur un rendez-vous planifié pour la personne (projection de
/// <c>RendezVousPlanifie</c>), l'affilié et les obligations couvertes sont repris du rendez-vous et la convocation
/// établit la relation de soin ; sinon (consultation spontanée…) l'affilié est fourni et la règle générale s'applique.
/// </summary>
public sealed record OuvrirExamen(Guid DossierId, string? TypeExamen, Guid? AffilieId, Guid? RendezVousId, DateOnly? Date);

public sealed class OuvrirExamenHandler(
    GardeDossier garde, IDossierSanteRepository dossiers, IExamenRepository examens, IProjectionRepository projections, ICurrentUser user,
    IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<OuvrirExamen, Guid>
{
    public async Task<Result<Guid>> HandleAsync(OuvrirExamen command, CancellationToken cancellationToken)
    {
        if (garde.VerifierPermission(ActionAudit.Creation) is { } interdit)
        {
            return interdit;
        }

        var dossierCible = await dossiers.GetAsync(command.DossierId, cancellationToken);
        if (dossierCible is null)
        {
            return GardeDossier.Inconnu;
        }

        var rendezVous = command.RendezVousId is { } rdvId ? await projections.GetRendezVousAsync(rdvId, cancellationToken) : null;
        if (command.RendezVousId is not null && (rendezVous is null || rendezVous.PersonneId != dossierCible.PersonneId))
        {
            return Error.Validation("examen.rendez-vous-inconnu", "Ce rendez-vous n'est pas planifié pour cette personne.");
        }

        var obligations = rendezVous is null ? [] : await projections.ListerObligationsParIdsAsync([.. rendezVous.ObligationIds], cancellationToken);
        var type = command.TypeExamen ?? obligations.Select(o => o.TypeExamen).FirstOrDefault();
        var affilie = command.AffilieId ?? rendezVous?.AffilieId;
        if (type is null || affilie is null)
        {
            return Error.Validation("examen.incomplet", "Précisez le type d'examen et l'affilié (ou un rendez-vous qui les porte).");
        }

        var dossier = await garde.EcrireAsync(command.DossierId, PartiesDossier.Examen, ActionAudit.Creation, cancellationToken, relationEtablie: rendezVous is not null);
        if (!dossier.IsSuccess)
        {
            return dossier.Error!;
        }

        var date = command.Date ?? (rendezVous is null ? horloge.Aujourdhui() : DateOnly.FromDateTime(rendezVous.Debut.UtcDateTime));
        var examen = Regles.Appliquer("examen.invalide", () => Examen.Ouvrir(
            dossier.Value.Id, dossier.Value.PersonneId, affilie.Value, type, date, user.UserId, rendezVous?.RendezVousId, rendezVous?.ObligationIds ?? []));
        if (!examen.IsSuccess)
        {
            return examen.Error!;
        }

        examens.Add(examen.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return examen.Value.Id;
    }
}

public sealed record ObtenirExamen(Guid ExamenId);

public sealed class ObtenirExamenHandler(AccesExamens acces) : IQueryHandler<ObtenirExamen, ExamenDto>
{
    public async Task<Result<ExamenDto>> HandleAsync(ObtenirExamen query, CancellationToken cancellationToken)
    {
        var examen = await acces.ChargerAsync(query.ExamenId, ActionAudit.Lecture, cancellationToken);
        return examen.IsSuccess ? examen.Value.Dto() : examen.Error!;
    }
}

/// <summary>SAN-21 : anamnèse et examen clinique (contenu chiffré).</summary>
public sealed record SaisirObservation(Guid ExamenId, string? Anamnese, string? ExamenClinique)
{
    public override string ToString() => $"SaisirObservation {{ ExamenId = {ExamenId} }}";
}

public sealed class SaisirObservationHandler(AccesExamens acces, IUnitOfWork unitOfWork, TimeProvider horloge) : ICommandHandler<SaisirObservation, Unit>
{
    public async Task<Result<Unit>> HandleAsync(SaisirObservation command, CancellationToken cancellationToken)
    {
        var examen = await acces.ChargerAsync(command.ExamenId, ActionAudit.Modification, cancellationToken);
        if (!examen.IsSuccess)
        {
            return examen.Error!;
        }

        var resultat = Regles.Appliquer("examen.invalide", () => examen.Value.SaisirObservation(command.Anamnese, command.ExamenClinique, horloge.GetUtcNow()));
        if (resultat.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return resultat;
    }
}

public sealed record MesureSaisie(string Code, decimal? Valeur, string? Unite, string? Texte);

public sealed record ResultatEnregistreDto(Guid ResultatId, bool Inhabituel, Guid? PropositionFrequenceId);

/// <summary>SAN-21, SAN-23 : saisie structurée d'un acte ; les mesures sont comparées aux valeurs de référence.</summary>
public sealed record EnregistrerActe(Guid ExamenId, TypeActe TypeActe, IReadOnlyList<MesureSaisie> Mesures, string? Commentaire, DateOnly? Date)
{
    public override string ToString() => $"EnregistrerActe {{ ExamenId = {ExamenId}, TypeActe = {TypeActe} }}";
}

public sealed class EnregistrerActeHandler(AccesExamens acces, IProtocolesRepository protocoles, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<EnregistrerActe, ResultatEnregistreDto>
{
    public async Task<Result<ResultatEnregistreDto>> HandleAsync(EnregistrerActe command, CancellationToken cancellationToken)
    {
        var examen = await acces.ChargerAsync(command.ExamenId, ActionAudit.Modification, cancellationToken);
        if (!examen.IsSuccess)
        {
            return examen.Error!;
        }

        return await Actes.EnregistrerAsync(examen.Value, command.TypeActe, [.. command.Mesures.Select(m => new Mesure(m.Code, m.Valeur, m.Unite, m.Texte))],
            command.Commentaire, SourceResultat.Saisie, command.Date ?? horloge.Aujourdhui(), protocoles, unitOfWork, cancellationToken);
    }
}

/// <summary>
/// SAN-21 : import direct d'un appareil (message HL7 v2, fichier CSV, pilote ou simulateur). Le contenu brut n'est
/// jamais conservé ni journalisé : seules les mesures extraites sont enregistrées (chiffrées).
/// </summary>
public sealed record ImporterAppareil(Guid ExamenId, TypeActe TypeActe, string Format, string Contenu, DateOnly? Date)
{
    public override string ToString() => $"ImporterAppareil {{ ExamenId = {ExamenId}, TypeActe = {TypeActe}, Format = {Format} }}";
}

public sealed class ImporterAppareilHandler(
    AccesExamens acces, IEnumerable<IImportAppareil> pilotes, IProtocolesRepository protocoles, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<ImporterAppareil, ResultatEnregistreDto>
{
    public async Task<Result<ResultatEnregistreDto>> HandleAsync(ImporterAppareil command, CancellationToken cancellationToken)
    {
        var pilote = pilotes.FirstOrDefault(p => string.Equals(p.Format, command.Format?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (pilote is null)
        {
            return Error.Validation("import.format-inconnu",
                $"Format d'import inconnu ; formats pris en charge : {string.Join(", ", pilotes.Select(p => p.Format))}.");
        }

        var examen = await acces.ChargerAsync(command.ExamenId, ActionAudit.Modification, cancellationToken);
        if (!examen.IsSuccess)
        {
            return examen.Error!;
        }

        IReadOnlyList<Mesure> mesures;
        try
        {
            mesures = pilote.Lire(command.TypeActe, command.Contenu ?? string.Empty);
        }
        catch (FormatException ex)
        {
            return Error.Validation("import.illisible", ex.Message);
        }

        return await Actes.EnregistrerAsync(examen.Value, command.TypeActe, mesures, null, SourceResultat.ImportAppareil,
            command.Date ?? horloge.Aujourdhui(), protocoles, unitOfWork, cancellationToken);
    }
}

internal static class Actes
{
    public static async Task<Result<ResultatEnregistreDto>> EnregistrerAsync(
        Examen examen, TypeActe typeActe, IReadOnlyList<Mesure> mesures, string? commentaire, SourceResultat source, DateOnly date,
        IProtocolesRepository protocoles, IUnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        var references = await protocoles.ListerValeursReferenceAsync(cancellationToken);
        var resultat = Regles.Appliquer("acte.invalide", () =>
        {
            var evaluees = EvaluateurResultats.Evaluer(typeActe, mesures, references, date);
            var enregistre = examen.EnregistrerActe(typeActe, evaluees, commentaire, source, date);
            var proposition = examen.PropositionsFrequence.FirstOrDefault(p => p.ResultatActeId == enregistre.Id);
            return new ResultatEnregistreDto(enregistre.Id, enregistre.Inhabituel, proposition?.Id);
        });
        if (resultat.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return resultat;
    }
}

/// <summary>
/// SAN-23 : décision du CPMT sur la proposition d'augmenter la fréquence (art. I.4-32). Acceptée, la nouvelle fréquence
/// est définie par le CPMT comme surcharge pour ce travailleur dans le service Postes et risques (AFF-13).
/// </summary>
public sealed record DeciderPropositionFrequence(Guid ExamenId, Guid PropositionId, bool Acceptee);

public sealed class DeciderPropositionFrequenceHandler(AccesExamens acces, ICurrentUser user, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<DeciderPropositionFrequence, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeciderPropositionFrequence command, CancellationToken cancellationToken)
    {
        if (Regles.Permission(user, Permissions.DecisionEcrire, "La fréquence de surveillance est décidée par le CPMT.") is { } interdit)
        {
            return interdit;
        }

        var examen = await acces.ChargerAsync(command.ExamenId, ActionAudit.Modification, cancellationToken);
        if (!examen.IsSuccess)
        {
            return examen.Error!;
        }

        var resultat = Regles.Appliquer("proposition.invalide", () => examen.Value.DeciderProposition(command.PropositionId, command.Acceptee, user.UserId, horloge.Aujourdhui()));
        if (resultat.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return resultat;
    }
}

/// <summary>
/// §14.5 <c>CloturerExamen</c> : clôture de l'examen ; les obligations couvertes sont satisfaites et
/// <c>ExamenCloture</c> est publié (type et date uniquement, ARC-06). Pour un examen de reprise, le respect du délai
/// légal est lu dans la projection de l'obligation (<see cref="ObligationDue.EstHorsDelai"/> : date de l'examen hors de
/// [<c>DateDue</c>, <c>DateLimite</c>]). La date limite est celle calculée par le service Obligations, propriétaire du
/// délai et du calendrier (saga « examen de reprise », ARC-33) : elle n'est jamais recalculée ici.
/// </summary>
public sealed record CloturerExamen(Guid ExamenId, DateOnly? Date);

public sealed class CloturerExamenHandler(
    AccesExamens acces, IProjectionRepository projections, IIntegrationEventOutbox outbox, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<CloturerExamen, Unit>
{
    public async Task<Result<Unit>> HandleAsync(CloturerExamen command, CancellationToken cancellationToken)
    {
        var acces1 = await acces.ChargerAsync(command.ExamenId, ActionAudit.Modification, cancellationToken);
        if (!acces1.IsSuccess)
        {
            return acces1.Error!;
        }

        var examen = acces1.Value;
        var date = command.Date ?? horloge.Aujourdhui();
        var obligations = await projections.ListerObligationsParIdsAsync([.. examen.ObligationIds], cancellationToken);

        var horsDelai = examen.TypeExamen == TypesExamen.ExamenReprise
            ? obligations.FirstOrDefault(o => o.TypeExamen == TypesExamen.ExamenReprise)?.EstHorsDelai(examen.Date)
            : null;

        var resultat = Regles.Appliquer("examen.invalide", () => examen.Cloturer(date, horsDelai));
        if (!resultat.IsSuccess)
        {
            return resultat;
        }

        foreach (var obligation in obligations)
        {
            obligation.Satisfaire(examen.Id);
        }

        EvenementsIntegration.Publier(examen, outbox);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return resultat;
    }
}
