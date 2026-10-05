using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.SurveillanceMedicale.Domain.Conservation;
using Sepp.SurveillanceMedicale.Domain.Dossiers;

namespace Sepp.SurveillanceMedicale.Application.Dossiers;

public sealed record DossierResumeDto(Guid Id, Guid PersonneId, string? GestionnaireCpmtId, DateOnly DateOuverture, StatutArchivage StatutArchivage, DateOnly? DatePurgePrevue);

public sealed record ExpositionDto(Guid Id, string Agent, string Niveau, DateOnly PeriodeDebut, DateOnly? PeriodeFin, Guid? MesurageId, Guid? GroupeExpositionId);

public sealed record GroupeExpositionDto(Guid Id, Guid GroupeExpositionId, Guid AffilieId, DateOnly ValideDu, DateOnly? ValideJusquAu);

public sealed record PieceJointeDto(Guid Id, Guid DocumentId, CategoriePieceJointe Categorie, string Titre, string? Description, DateOnly DateDocument, DateOnly AjouteeLe);

public sealed record QuestionnaireDto(Guid Id, string ModeleCode, int ModeleVersion, SourceQuestionnaire Source, DateTimeOffset RempliLe, Guid? ExamenId, IReadOnlyList<ReponseQuestionnaire> Reponses);

public sealed record VaccinationDto(Guid Id, string VaccinCode, int Dose, DateOnly Date, Guid? LotId, Guid? CentreId, string AdministrePar, string? Remarque, bool EnregistreRegistre);

public sealed record TestTuberculiniqueDto(Guid Id, DateOnly DatePose, DateOnly? DateLecture, ResultatTuberculinique? Resultat, int? IndurationMm, string RealisePar);

/// <summary>Parties du dossier portées par l'agrégat (art. I.4-85 à I.4-87).</summary>
public sealed record DossierDto(
    DossierResumeDto Dossier,
    IReadOnlyList<ExpositionDto> Expositions,
    IReadOnlyList<GroupeExpositionDto> GroupesExposition,
    IReadOnlyList<PieceJointeDto> PiecesJointes,
    IReadOnlyList<QuestionnaireDto> Questionnaires,
    IReadOnlyList<VaccinationDto> Vaccinations,
    IReadOnlyList<TestTuberculiniqueDto> TestsTuberculiniques);

public static class DossierProjections
{
    public static DossierResumeDto Resume(this DossierSante d) =>
        new(d.Id, d.PersonneId, d.GestionnaireCpmtId, d.DateOuverture, d.StatutArchivage, d.DatePurgePrevue);

    public static DossierDto Complet(this DossierSante d) => new(
        d.Resume(),
        [.. d.Expositions.OrderBy(e => e.PeriodeDebut).Select(e => e.Dto())],
        [.. d.GroupesExposition.Select(g => new GroupeExpositionDto(g.Id, g.GroupeExpositionId, g.AffilieId, g.Periode.ValidFrom, g.Periode.ValidTo))],
        [.. d.PiecesJointes.OrderBy(p => p.DateDocument).Select(p => new PieceJointeDto(p.Id, p.DocumentId, p.Categorie, p.Titre, p.Description, p.DateDocument, p.AjouteeLe))],
        [.. d.Questionnaires.OrderBy(q => q.RempliLe).Select(q => q.Dto())],
        [.. d.Vaccinations.OrderBy(v => v.Date).Select(v => new VaccinationDto(v.Id, v.VaccinCode, v.Dose, v.Date, v.LotId, v.CentreId, v.AdministrePar, v.Remarque, v.EnregistreRegistre))],
        [.. d.TestsTuberculiniques.OrderBy(t => t.DatePose).Select(t => new TestTuberculiniqueDto(t.Id, t.DatePose, t.DateLecture, t.Lecture?.Resultat, t.Lecture?.IndurationMm, t.RealisePar))]);

    public static ExpositionDto Dto(this Exposition e) => new(e.Id, e.Agent, e.Niveau, e.PeriodeDebut, e.PeriodeFin, e.MesurageId, e.GroupeExpositionId);

    public static QuestionnaireDto Dto(this QuestionnaireRempli q) => new(q.Id, q.ModeleCode, q.ModeleVersion, q.Source, q.RempliLe, q.ExamenId, q.Reponses);
}

/// <summary>SAN-40 : ouverture du dossier unique de la personne ; le CPMT qui l'ouvre en devient gestionnaire par défaut.</summary>
public sealed record OuvrirDossier(Guid PersonneId, string? GestionnaireCpmtId);

public sealed class OuvrirDossierHandler(
    IDossierSanteRepository dossiers, GardeDossier garde, ICurrentUser user, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<OuvrirDossier, Guid>
{
    public async Task<Result<Guid>> HandleAsync(OuvrirDossier command, CancellationToken cancellationToken)
    {
        if (garde.VerifierPermission(ActionAudit.Creation) is { } interdit)
        {
            return interdit;
        }

        if (await dossiers.GetParPersonneAsync(command.PersonneId, cancellationToken) is not null)
        {
            return Error.Conflict("dossier-sante.existe", "Cette personne a déjà un dossier de santé (dossier unique, SAN-40).");
        }

        var gestionnaire = command.GestionnaireCpmtId ?? (user.HasPermission(Permissions.DecisionEcrire) ? user.UserId : null);
        var ouvert = Regles.Appliquer("dossier-sante.invalide", () => DossierSante.Ouvrir(command.PersonneId, gestionnaire, horloge.Aujourdhui()));
        if (!ouvert.IsSuccess)
        {
            return ouvert.Error!;
        }

        dossiers.Add(ouvert.Value);
        garde.Journaliser(ActionAudit.Creation, PartiesDossier.Dossier, ouvert.Value.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ouvert.Value.Id;
    }
}

/// <summary>
/// Recherche du dossier d'une personne : identifiant et statut uniquement, sans contenu clinique. Réservée au
/// personnel médical et journalisée ; le motif n'est pas exigé (aucune donnée de santé n'est rendue).
/// </summary>
public sealed record TrouverDossier(Guid PersonneId);

public sealed class TrouverDossierHandler(IDossierSanteRepository dossiers, GardeDossier garde, IAuditTrail audit, IContexteAcces contexte)
    : IQueryHandler<TrouverDossier, DossierResumeDto>
{
    public async Task<Result<DossierResumeDto>> HandleAsync(TrouverDossier query, CancellationToken cancellationToken)
    {
        if (garde.VerifierPermission(ActionAudit.Lecture) is { } interdit)
        {
            return interdit;
        }

        if (MotifAcces.Verifier(contexte.Motif, contexte.BrisDeGlace) is { } motifInvalide)
        {
            return motifInvalide;
        }

        var dossier = await dossiers.GetParPersonneAsync(query.PersonneId, cancellationToken);
        if (dossier is null)
        {
            return GardeDossier.Inconnu;
        }

        await audit.EnregistrerLectureAsync(PartiesDossier.TypeObjet(PartiesDossier.Identification), dossier.Id, contexte.Motif, contexte.BrisDeGlace, cancellationToken);
        return dossier.Resume();
    }
}

public sealed record ObtenirDossier(Guid DossierId);

public sealed class ObtenirDossierHandler(GardeDossier garde) : IQueryHandler<ObtenirDossier, DossierDto>
{
    public async Task<Result<DossierDto>> HandleAsync(ObtenirDossier query, CancellationToken cancellationToken)
    {
        var dossier = await garde.LireAsync(query.DossierId, PartiesDossier.Dossier, cancellationToken);
        return dossier.IsSuccess ? dossier.Value.Complet() : dossier.Error!;
    }
}

public sealed record ChangerGestionnaire(Guid DossierId, string CpmtId);

public sealed class ChangerGestionnaireHandler(GardeDossier garde, IUnitOfWork unitOfWork) : ICommandHandler<ChangerGestionnaire, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ChangerGestionnaire command, CancellationToken cancellationToken)
    {
        var dossier = await garde.EcrireAsync(command.DossierId, PartiesDossier.Dossier, ActionAudit.Modification, cancellationToken);
        if (!dossier.IsSuccess)
        {
            return dossier.Error!;
        }

        var resultat = Regles.Appliquer("dossier-sante.invalide", () => dossier.Value.ChangerGestionnaire(command.CpmtId));
        if (resultat.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return resultat;
    }
}

/// <summary>
/// SAN-40 : rattachement de la personne à un groupe d'exposition du service Prévention ; les mesurages déjà reçus pour
/// ce groupe pendant la période sont versés au dossier, les suivants le seront à leur réception.
/// </summary>
public sealed record RattacherGroupeExposition(Guid DossierId, Guid GroupeExpositionId, Guid AffilieId, DateOnly ValideDu, DateOnly? ValideJusquAu);

public sealed class RattacherGroupeExpositionHandler(GardeDossier garde, IProjectionRepository projections, IUnitOfWork unitOfWork)
    : ICommandHandler<RattacherGroupeExposition, Guid>
{
    public async Task<Result<Guid>> HandleAsync(RattacherGroupeExposition command, CancellationToken cancellationToken)
    {
        var dossier = await garde.EcrireAsync(command.DossierId, PartiesDossier.Expositions, ActionAudit.Modification, cancellationToken);
        if (!dossier.IsSuccess)
        {
            return dossier.Error!;
        }

        var mesurages = await projections.ListerMesuragesAsync(command.GroupeExpositionId, cancellationToken);
        var resultat = Regles.Appliquer("exposition.invalide", () =>
        {
            var rattachement = dossier.Value.RattacherGroupeExposition(command.GroupeExpositionId, command.AffilieId, new Validity(command.ValideDu, command.ValideJusquAu));
            foreach (var m in mesurages.Where(m => rattachement.Periode.Contains(m.Date)))
            {
                dossier.Value.EnregistrerExposition(m.Agent, m.Niveau, m.Date, m.Date, m.MesurageId, m.GroupeExpositionId);
            }

            return rattachement.Id;
        });
        if (resultat.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return resultat;
    }
}

/// <summary>Donnée d'exposition saisie par le CPMT (exposition antérieure, déclaration du travailleur…).</summary>
public sealed record AjouterExposition(Guid DossierId, string Agent, string Niveau, DateOnly Debut, DateOnly? Fin);

public sealed class AjouterExpositionHandler(GardeDossier garde, IUnitOfWork unitOfWork) : ICommandHandler<AjouterExposition, Guid>
{
    public async Task<Result<Guid>> HandleAsync(AjouterExposition command, CancellationToken cancellationToken)
    {
        var dossier = await garde.EcrireAsync(command.DossierId, PartiesDossier.Expositions, ActionAudit.Creation, cancellationToken);
        if (!dossier.IsSuccess)
        {
            return dossier.Error!;
        }

        var resultat = Regles.Appliquer("exposition.invalide",
            () => dossier.Value.EnregistrerExposition(command.Agent, command.Niveau, command.Debut, command.Fin, null, null)!.Id);
        if (resultat.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return resultat;
    }
}

/// <summary>SAN-41 : pièce jointe (le fichier est déjà stocké par le service Documents).</summary>
public sealed record AjouterPieceJointe(Guid DossierId, Guid DocumentId, CategoriePieceJointe Categorie, string Titre, string? Description, DateOnly DateDocument)
{
    public override string ToString() => $"AjouterPieceJointe {{ DossierId = {DossierId}, DocumentId = {DocumentId}, Categorie = {Categorie} }}";
}

public sealed class AjouterPieceJointeHandler(GardeDossier garde, IUnitOfWork unitOfWork, TimeProvider horloge) : ICommandHandler<AjouterPieceJointe, Guid>
{
    public async Task<Result<Guid>> HandleAsync(AjouterPieceJointe command, CancellationToken cancellationToken)
    {
        var dossier = await garde.EcrireAsync(command.DossierId, PartiesDossier.PiecesJointes, ActionAudit.Creation, cancellationToken);
        if (!dossier.IsSuccess)
        {
            return dossier.Error!;
        }

        var resultat = Regles.Appliquer("piece-jointe.invalide", () => dossier.Value.AjouterPieceJointe(
            command.DocumentId, command.Categorie, command.Titre, command.Description, command.DateDocument, horloge.Aujourdhui()).Id);
        if (resultat.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return resultat;
    }
}

/// <summary>
/// SAN-44 : archivage d'un dossier sans activité ; la date de purge est calculée par la politique de conservation
/// (minimum légal, au moins 15 ans, et durées propres aux expositions) à partir de la dernière activité.
/// </summary>
public sealed record ArchiverDossier(Guid DossierId);

public sealed class ArchiverDossierHandler(
    GardeDossier garde, IExamenRepository examens, IProtocolesRepository protocoles, ParametresMedicaux parametres, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<ArchiverDossier, DossierResumeDto>
{
    public async Task<Result<DossierResumeDto>> HandleAsync(ArchiverDossier command, CancellationToken cancellationToken)
    {
        var acces = await garde.EcrireAsync(command.DossierId, PartiesDossier.Conservation, ActionAudit.Modification, cancellationToken);
        if (!acces.IsSuccess)
        {
            return acces.Error!;
        }

        var dossier = acces.Value;
        var aujourdhui = horloge.Aujourdhui();
        var datePurge = await DatePurgeAsync(dossier, examens, protocoles, parametres, aujourdhui, cancellationToken);
        var resultat = Regles.Appliquer("dossier-sante.archivage", () => dossier.Archiver(aujourdhui, datePurge));
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return dossier.Resume();
    }

    /// <summary>Dernière activité : ouverture, examens, expositions, vaccinations, pièces jointes, questionnaires.</summary>
    internal static async Task<DateOnly> DatePurgeAsync(
        DossierSante dossier, IExamenRepository examens, IProtocolesRepository protocoles, ParametresMedicaux parametres, DateOnly aujourdhui, CancellationToken cancellationToken)
    {
        var listeExamens = await examens.ListerParDossierAsync(dossier.Id, cancellationToken);
        var dates = new List<DateOnly> { dossier.DateOuverture };
        dates.AddRange(listeExamens.Select(e => e.DateCloture ?? e.Date));
        dates.AddRange(dossier.Expositions.Select(e => e.DerniereDate));
        dates.AddRange(dossier.Vaccinations.Select(v => v.Date));
        dates.AddRange(dossier.PiecesJointes.Select(p => p.AjouteeLe));
        dates.AddRange(dossier.Questionnaires.Select(q => DateOnly.FromDateTime(q.RempliLe.UtcDateTime)));
        var derniere = dates.Max();

        var minimum = await parametres.ConservationMinimumAnneesAsync(aujourdhui, cancellationToken);
        var durees = await protocoles.ListerDureesConservationAsync(cancellationToken);
        return PolitiqueConservationDossier.DatePurge(derniere, minimum, dossier.Expositions.Select(e => e.Agent), durees);
    }
}
