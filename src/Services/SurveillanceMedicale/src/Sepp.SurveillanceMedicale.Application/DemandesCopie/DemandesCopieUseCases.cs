using System.Globalization;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Documents;
using Sepp.SurveillanceMedicale.Application.Consultation;
using Sepp.SurveillanceMedicale.Application.Vaccinations;
using Sepp.SurveillanceMedicale.Domain.Decisions;
using Sepp.SurveillanceMedicale.Domain.DemandesCopie;
using Sepp.SurveillanceMedicale.Domain.Examens;
using Sepp.SurveillanceMedicale.Domain.Vaccinations;

namespace Sepp.SurveillanceMedicale.Application.DemandesCopie;

/// <summary>
/// Demande de copie vue par le travailleur et par le CPMT (SAN-43) : dates, statut et document publié — jamais de contenu
/// médical, ni l'identité du CPMT qui l'a traitée.
/// </summary>
public sealed record DemandeCopieDto(
    Guid Id,
    Guid PersonneId,
    DateTimeOffset DeposeeLe,
    DateOnly DateLimite,
    StatutDemandeCopie Statut,
    bool EnRetard,
    DateTimeOffset? TraiteeLe,
    MotifRefusCopie? MotifRefus,
    Guid? DocumentId,
    DateTimeOffset? PublieeLe);

/// <summary>
/// Accès aux demandes de copie (§3.3 « Dossier de santé — travailleur : copie sur demande ») : le travailleur ne voit que
/// les siennes (claim <c>personne_id</c>), le CPMT les traite. Chaque étape est tracée dans le journal d'audit de la zone
/// médicale (type d'objet <c>dossier-sante.copie</c>, identifiant du dossier, ou de la demande quand la personne n'a pas
/// de dossier).
/// </summary>
public sealed class AccesDemandesCopie(IDemandeCopieRepository demandes, ICurrentUser user, IContexteAcces contexte, TimeProvider horloge)
{
    public const string MotifTransmissionCopie = "SAN-43 : copie validée par le CPMT, transmise au service Documents pour publication au travailleur";
    public const string MotifTransmissionCarnet = "SAN-50 : carnet de vaccination transmis au service Documents pour publication au travailleur";
    public const string MotifPublication = "SAN-43 : copie publiée au travailleur par le service Documents";

    public static readonly Error Inconnue = Error.NotFound("demande-copie.inconnue", "Demande de copie inconnue.");

    public static readonly Error HorsPerimetre = Error.Forbidden(
        "demande-copie.hors-perimetre", "Un travailleur ne demande et ne suit que la copie de son propre dossier (claim personne_id).");

    /// <summary>Le travailleur du jeton (claim <c>personne_id</c>), seul à pouvoir demander et suivre sa copie.</summary>
    public Result<Guid> Travailleur()
    {
        if (!user.HasPermission(Permissions.CopieDossierDemander))
        {
            return Error.Forbidden("demande-copie.interdit", "Seul le travailleur demande la copie de son dossier de santé.");
        }

        return user.Roles.Contains(Roles.Travailleur) && contexte.PersonneIdJeton is { } personne ? personne : HorsPerimetre;
    }

    public Error? VerifierTraitement() =>
        Regles.Permission(user, Permissions.CopieDossierTraiter, "Seul le CPMT traite les demandes de copie du dossier de santé (secret médical).");

    public Error? VerifierTransmission() =>
        Regles.Permission(user, Permissions.DossierSanteTransmettreDocuments, "Seul le service Documents reçoit le contenu à publier au travailleur.");

    public async Task<Result<DemandeCopieDossier>> ChargerAsync(Guid demandeId, CancellationToken cancellationToken) =>
        await demandes.GetAsync(demandeId, cancellationToken) is { } demande ? demande : Inconnue;

    public DemandeCopieDto Dto(DemandeCopieDossier d) =>
        new(d.Id, d.PersonneId, d.DeposeeLe, d.DateLimite, d.Statut, d.EstEnRetard(horloge.Aujourdhui()), d.TraiteeLe, d.MotifRefus, d.DocumentId, d.PublieeLe);

    /// <summary>Objet de la trace : le dossier (pour que le DPO retrouve tous les accès à un dossier), sinon la demande.</summary>
    public static Guid ObjetAudit(DemandeCopieDossier d) => d.DossierId ?? d.Id;
}

/// <summary>
/// SAN-43, POR-13 : le travailleur dépose, depuis le portail, une demande de copie de son dossier. Aucun contenu médical
/// n'est lu ni renvoyé ; la personne est celle du jeton. Une seule demande en cours à la fois. La date limite de traitement
/// suit le délai paramétré (<see cref="OptionsCopieDossier.DelaiJours"/>, à valider).
/// </summary>
public sealed record DeposerDemandeCopie;

public sealed class DeposerDemandeCopieHandler(
    AccesDemandesCopie acces, IDemandeCopieRepository demandes, IDossierSanteRepository dossiers, IAuditTrail audit, OptionsSurveillanceMedicale options,
    IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<DeposerDemandeCopie, DemandeCopieDto>
{
    public async Task<Result<DemandeCopieDto>> HandleAsync(DeposerDemandeCopie command, CancellationToken cancellationToken)
    {
        var personne = acces.Travailleur();
        if (!personne.IsSuccess)
        {
            return personne.Error!;
        }

        if ((await demandes.ListerParPersonneAsync(personne.Value, cancellationToken)).Any(d => d.EstEnCours))
        {
            return Error.Conflict("demande-copie.en-cours", "Une demande de copie de votre dossier est déjà en cours de traitement.");
        }

        var dossier = await dossiers.GetParPersonneAsync(personne.Value, cancellationToken);
        var demande = Regles.Appliquer("demande-copie.invalide", () => DemandeCopieDossier.Deposer(
            personne.Value, dossier?.Id, horloge.GetUtcNow(), horloge.Aujourdhui(), options.CopieDossier.DelaiJours));
        if (!demande.IsSuccess)
        {
            return demande.Error!;
        }

        demandes.Add(demande.Value);
        audit.Enregistrer(ActionAudit.Creation, PartiesDossier.TypeObjet(PartiesDossier.Copie), AccesDemandesCopie.ObjetAudit(demande.Value));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return acces.Dto(demande.Value);
    }
}

/// <summary>Suivi par le travailleur de ses demandes de copie (statut, date limite, document publié), les plus récentes d'abord.</summary>
public sealed record ListerMesDemandesCopie;

public sealed class ListerMesDemandesCopieHandler(AccesDemandesCopie acces, IDemandeCopieRepository demandes)
    : IQueryHandler<ListerMesDemandesCopie, IReadOnlyList<DemandeCopieDto>>
{
    public async Task<Result<IReadOnlyList<DemandeCopieDto>>> HandleAsync(ListerMesDemandesCopie query, CancellationToken cancellationToken)
    {
        var personne = acces.Travailleur();
        if (!personne.IsSuccess)
        {
            return personne.Error!;
        }

        return (await demandes.ListerParPersonneAsync(personne.Value, cancellationToken))
            .OrderByDescending(d => d.DeposeeLe).Select(acces.Dto).ToList();
    }
}

/// <summary>File de travail du CPMT : demandes d'un statut (déposées par défaut), les plus urgentes d'abord (date limite).</summary>
public sealed record ListerDemandesCopie(StatutDemandeCopie? Statut);

public sealed class ListerDemandesCopieHandler(AccesDemandesCopie acces, IDemandeCopieRepository demandes)
    : IQueryHandler<ListerDemandesCopie, IReadOnlyList<DemandeCopieDto>>
{
    public async Task<Result<IReadOnlyList<DemandeCopieDto>>> HandleAsync(ListerDemandesCopie query, CancellationToken cancellationToken)
    {
        if (acces.VerifierTraitement() is { } interdit)
        {
            return interdit;
        }

        return (await demandes.ListerParStatutAsync(query.Statut ?? StatutDemandeCopie.Deposee, cancellationToken))
            .OrderBy(d => d.DateLimite).ThenBy(d => d.DeposeeLe).Select(acces.Dto).ToList();
    }
}

/// <summary>
/// SAN-43 : le CPMT valide l'export de la copie. Il doit avoir accès au dossier (secret médical : relation de soin, sinon
/// motif ; voir <see cref="GardeDossier"/>), l'accès est tracé dans la transaction de la validation, et
/// <c>CopieDossierValidee</c> (identifiants seulement) est publié pour que Documents génère et publie la copie.
/// </summary>
public sealed record ValiderDemandeCopie(Guid DemandeId);

public sealed class ValiderDemandeCopieHandler(
    AccesDemandesCopie acces, IDossierSanteRepository dossiers, GardeDossier garde, ICurrentUser user, IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<ValiderDemandeCopie, DemandeCopieDto>
{
    public async Task<Result<DemandeCopieDto>> HandleAsync(ValiderDemandeCopie command, CancellationToken cancellationToken)
    {
        if (acces.VerifierTraitement() is { } interdit)
        {
            return interdit;
        }

        var demande = await acces.ChargerAsync(command.DemandeId, cancellationToken);
        if (!demande.IsSuccess)
        {
            return demande.Error!;
        }

        if (demande.Value.Statut != StatutDemandeCopie.Deposee)
        {
            return Error.Conflict("demande-copie.deja-traitee", "Cette demande de copie a déjà été traitée.");
        }

        var dossier = await dossiers.GetParPersonneAsync(demande.Value.PersonneId, cancellationToken);
        if (dossier is null)
        {
            return Error.Unprocessable("demande-copie.aucun-dossier", "La personne n'a pas de dossier de santé : refusez la demande (motif AucunDossier).");
        }

        var accesDossier = await garde.EcrireAsync(dossier.Id, PartiesDossier.Copie, ActionAudit.Modification, cancellationToken);
        if (!accesDossier.IsSuccess)
        {
            return accesDossier.Error!;
        }

        var resultat = Regles.Appliquer("demande-copie.invalide", () => demande.Value.Valider(dossier.Id, user.UserId, horloge.GetUtcNow()));
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        EvenementsIntegration.Publier(demande.Value, outbox);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return acces.Dto(demande.Value);
    }
}

/// <summary>SAN-43 : refus motivé par un code (aucun texte libre ne part vers le portail) ; tracé.</summary>
public sealed record RefuserDemandeCopie(Guid DemandeId, MotifRefusCopie Motif);

public sealed class RefuserDemandeCopieHandler(AccesDemandesCopie acces, GardeDossier garde, ICurrentUser user, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<RefuserDemandeCopie, DemandeCopieDto>
{
    public async Task<Result<DemandeCopieDto>> HandleAsync(RefuserDemandeCopie command, CancellationToken cancellationToken)
    {
        if (acces.VerifierTraitement() is { } interdit)
        {
            return interdit;
        }

        var demande = await acces.ChargerAsync(command.DemandeId, cancellationToken);
        if (!demande.IsSuccess)
        {
            return demande.Error!;
        }

        if (demande.Value.Statut != StatutDemandeCopie.Deposee)
        {
            return Error.Conflict("demande-copie.deja-traitee", "Cette demande de copie a déjà été traitée.");
        }

        var resultat = Regles.Appliquer("demande-copie.invalide", () => demande.Value.Refuser(command.Motif, user.UserId, horloge.GetUtcNow()));
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        garde.Journaliser(ActionAudit.Modification, PartiesDossier.Copie, AccesDemandesCopie.ObjetAudit(demande.Value));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return acces.Dto(demande.Value);
    }
}

// ---- Contenu transmis au service Documents (compte technique, dossier-sante:transmettre-documents) ----

/// <summary>Rubrique d'un élément de la copie : code (libellé traduit par Documents) et valeur saisie dans le dossier.</summary>
public sealed record RubriqueCopieDto(string Code, string Valeur);

/// <summary>Élément d'une partie du dossier : date, intitulé (code de référentiel) et rubriques.</summary>
public sealed record ElementCopieDto(DateOnly? Date, string Intitule, IReadOnlyList<RubriqueCopieDto> Rubriques);

/// <summary>
/// SAN-43 : contenu de la copie du dossier, par partie (art. I.4-85 à I.4-87), tel que Documents le met en page pour le
/// travailleur. Les décisions en brouillon (notes de travail du CPMT, non émises) et le paquet brut des dossiers reçus
/// n'y figurent pas ; les pièces jointes sont listées (leurs fichiers restent au service Documents).
/// </summary>
public sealed record CopieDossierDto(
    Guid DemandeId,
    Guid PersonneId,
    DateOnly DateOuverture,
    DateTimeOffset ValideeLe,
    IReadOnlyList<ElementCopieDto> Expositions,
    IReadOnlyList<ElementCopieDto> EvaluationsSante,
    IReadOnlyList<ElementCopieDto> Decisions,
    IReadOnlyList<ElementCopieDto> Questionnaires,
    IReadOnlyList<ElementCopieDto> Vaccinations,
    IReadOnlyList<ElementCopieDto> TestsTuberculiniques,
    IReadOnlyList<ElementCopieDto> PiecesJointes,
    IReadOnlyList<ElementCopieDto> MaladiesProfessionnelles);

/// <summary>Codes des rubriques de la copie et du carnet (Documents en a un libellé FR, NL et DE).</summary>
public static class RubriquesCopie
{
    public const string Niveau = "NIVEAU";
    public const string Fin = "FIN";
    public const string Statut = "STATUT";
    public const string Anamnese = "ANAMNESE";
    public const string ExamenClinique = "EXAMEN_CLINIQUE";
    public const string Resultat = "RESULTAT";
    public const string Commentaire = "COMMENTAIRE";
    public const string Mesures = "MESURES";
    public const string Validite = "VALIDITE";
    public const string Justification = "JUSTIFICATION";
    public const string Recommandations = "RECOMMANDATIONS";
    public const string Reponse = "REPONSE";
    public const string Dose = "DOSE";
    public const string Remarque = "REMARQUE";
    public const string Lecture = "LECTURE";
    public const string Induration = "INDURATION";
    public const string Titre = "TITRE";
    public const string Description = "DESCRIPTION";
    public const string Agent = "AGENT";
    public const string Lot = "LOT";

    public static IReadOnlyList<string> Connus { get; } =
    [
        Niveau, Fin, Statut, Anamnese, ExamenClinique, Resultat, Commentaire, Mesures, Validite, Justification, Recommandations,
        Reponse, Dose, Remarque, Lecture, Induration, Titre, Description, Agent, Lot,
    ];
}

internal static class CopiesDossier
{
    public static CopieDossierDto Construire(DemandeCopieDossier demande, ExportDossierDto export)
    {
        return new CopieDossierDto(
            demande.Id,
            demande.PersonneId,
            export.Identification.DateOuverture,
            demande.TraiteeLe ?? export.GenereLe,
            [.. export.DonneesExposition.Select(e => Element(e.PeriodeDebut, e.Agent, (RubriquesCopie.Niveau, e.Niveau), (RubriquesCopie.Fin, Date(e.PeriodeFin))))],
            [.. export.EvaluationsSante.Select(e => new ElementCopieDto(e.Date, e.TypeExamen, Rubriques(
                [
                    (RubriquesCopie.Statut, e.Statut.ToString()),
                    (RubriquesCopie.Anamnese, e.Observation?.Anamnese),
                    (RubriquesCopie.ExamenClinique, e.Observation?.ExamenClinique),
                    .. e.Resultats.SelectMany(r => new (string, string?)[]
                    {
                        (RubriquesCopie.Resultat, $"{r.TypeActe} ({Date(r.Date)}) : {string.Join(" ; ", r.Mesures.Select(Mesure))}"),
                        (RubriquesCopie.Commentaire, r.Commentaire),
                    }),
                ])))],
            [.. export.Decisions.Where(d => d.Resume.Statut != StatutDecision.Brouillon).Select(d => Element(d.Resume.DateExamen, d.Resume.Categorie,
                (RubriquesCopie.Mesures, d.Resume.Mesures.Count == 0 ? null : string.Join(", ", d.Resume.Mesures)),
                (RubriquesCopie.Validite, Date(d.Resume.ValideJusquAu)),
                (RubriquesCopie.Justification, d.Justification),
                (RubriquesCopie.Recommandations, d.Recommandations)))],
            [.. export.Questionnaires.Select(q => new ElementCopieDto(DateOnly.FromDateTime(q.RempliLe.UtcDateTime), $"{q.ModeleCode} v{q.ModeleVersion}",
                Rubriques([.. q.Reponses.Select(r => (RubriquesCopie.Reponse, (string?)$"{r.CodeQuestion} : {r.Valeur}"))])))],
            [.. export.Vaccinations.Select(v => Element(v.Date, v.VaccinCode,
                (RubriquesCopie.Dose, v.Dose.ToString(CultureInfo.InvariantCulture)), (RubriquesCopie.Remarque, v.Remarque)))],
            [.. export.TestsTuberculiniques.Select(t => Element(t.DatePose, CodesVaccination.TestTuberculinique,
                (RubriquesCopie.Lecture, Date(t.DateLecture)), (RubriquesCopie.Resultat, t.Resultat?.ToString()),
                (RubriquesCopie.Induration, t.IndurationMm is { } mm ? $"{mm} mm" : null)))],
            [.. export.PiecesJointes.Select(p => Element(p.DateDocument, p.Categorie.ToString(), (RubriquesCopie.Titre, p.Titre), (RubriquesCopie.Description, p.Description)))],
            [.. export.MaladiesProfessionnelles.Select(m => Element(m.Date, m.Contenu.CodeMaladie,
                (RubriquesCopie.Statut, m.Statut.ToString()), (RubriquesCopie.Description, m.Contenu.Description), (RubriquesCopie.Agent, m.Contenu.AgentCausal)))]);
    }

    public static string? Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Mesure(MesureEvaluee m) =>
        string.Join(' ', new[] { m.Code, m.Valeur?.ToString(CultureInfo.InvariantCulture), m.Unite, m.Texte }.Where(v => !string.IsNullOrWhiteSpace(v)));

    private static ElementCopieDto Element(DateOnly? date, string intitule, params (string Code, string? Valeur)[] rubriques) =>
        new(date, intitule, Rubriques(rubriques));

    private static List<RubriqueCopieDto> Rubriques(IEnumerable<(string Code, string? Valeur)> rubriques) =>
        [.. rubriques.Where(r => !string.IsNullOrWhiteSpace(r.Valeur)).Select(r => new RubriqueCopieDto(r.Code, r.Valeur!.Trim()))];
}

/// <summary>
/// SAN-43 : contenu d'une copie validée, lu par le service Documents (compte technique) pour la générer et la publier au
/// travailleur. Seule une demande validée par le CPMT et pas encore publiée est servie ; la transmission est tracée
/// (export, zone médicale) et validée avant que le contenu soit rendu.
/// </summary>
public sealed record ObtenirContenuCopie(Guid DemandeId);

public sealed class ObtenirContenuCopieHandler(
    AccesDemandesCopie acces, IDossierSanteRepository dossiers, ExportDossiers export, IAuditTrail audit, IUnitOfWork unitOfWork)
    : IQueryHandler<ObtenirContenuCopie, CopieDossierDto>
{
    public async Task<Result<CopieDossierDto>> HandleAsync(ObtenirContenuCopie query, CancellationToken cancellationToken)
    {
        if (acces.VerifierTransmission() is { } interdit)
        {
            return interdit;
        }

        var demande = await acces.ChargerAsync(query.DemandeId, cancellationToken);
        if (!demande.IsSuccess)
        {
            return demande.Error!;
        }

        if (demande.Value.Statut != StatutDemandeCopie.Validee || demande.Value.DossierId is not { } dossierId)
        {
            return Error.Conflict("demande-copie.non-validee", "Seule une copie validée par le CPMT, et pas encore publiée, est transmise.");
        }

        var dossier = await dossiers.GetAsync(dossierId, cancellationToken);
        if (dossier is null)
        {
            return GardeDossier.Inconnu;
        }

        var contenu = CopiesDossier.Construire(demande.Value, await export.ConstruireAsync(dossier, cancellationToken));
        audit.Enregistrer(ActionAudit.Export, PartiesDossier.TypeObjet(PartiesDossier.Copie), dossier.Id, AccesDemandesCopie.MotifTransmissionCopie);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return contenu;
    }
}

// ---- SAN-50 : carnet de vaccination (publié au travailleur par Documents après chaque vaccination) ----

/// <summary>Libellé d'un vaccin dans les langues des documents (repris du schéma vaccinal).</summary>
public sealed record LibelleVaccinDto(string Fr, string Nl, string De, string? En);

public sealed record DoseCarnetDto(DateOnly Date, string VaccinCode, LibelleVaccinDto? Libelle, int Dose, string? NumeroLot);

public sealed record TestCarnetDto(DateOnly DatePose, DateOnly? DateLecture, string? Resultat, int? IndurationMm);

public sealed record RappelCarnetDto(string VaccinCode, LibelleVaccinDto? Libelle, int DoseSuivante, DateOnly DateDue, bool EnRetard);

/// <summary>
/// SAN-50 : carnet de vaccination de la personne : doses administrées (vaccin, dose, lot), tests tuberculiniques et
/// prochaines doses ou rappels dus. Ni remarque (réaction, contre-indication) ni vaccinateur n'y figurent.
/// </summary>
public sealed record CarnetVaccinationDto(
    Guid VaccinationId,
    Guid PersonneId,
    DateOnly Date,
    IReadOnlyList<DoseCarnetDto> Doses,
    IReadOnlyList<TestCarnetDto> TestsTuberculiniques,
    IReadOnlyList<RappelCarnetDto> Rappels);

/// <summary>
/// SAN-50, POR-13 : carnet de vaccination à jour de la vaccination <paramref name="VaccinationId"/>, lu par le service
/// Documents (compte technique) à la réception de <c>VaccinationAdministree</c>. La transmission est tracée (export de la
/// partie vaccinations, zone médicale) avant que le contenu soit rendu.
/// </summary>
public sealed record ObtenirCarnetVaccination(Guid VaccinationId);

public sealed class ObtenirCarnetVaccinationHandler(
    AccesDemandesCopie acces, IDossierSanteRepository dossiers, IProtocolesRepository protocoles, ILotVaccinRepository lots, RisquesPersonne risques,
    IAuditTrail audit, IUnitOfWork unitOfWork, TimeProvider horloge)
    : IQueryHandler<ObtenirCarnetVaccination, CarnetVaccinationDto>
{
    public async Task<Result<CarnetVaccinationDto>> HandleAsync(ObtenirCarnetVaccination query, CancellationToken cancellationToken)
    {
        if (acces.VerifierTransmission() is { } interdit)
        {
            return interdit;
        }

        var dossier = await dossiers.GetParVaccinationAsync(query.VaccinationId, cancellationToken);
        if (dossier is null)
        {
            return Error.NotFound("vaccination.inconnue", "Vaccination inconnue.");
        }

        var date = horloge.Aujourdhui();
        var schemas = await protocoles.ListerSchemasVaccinauxAsync(cancellationToken);
        var libelles = schemas.GroupBy(s => s.VaccinCode).ToDictionary(g => g.Key, g => g.First().Libelle, StringComparer.Ordinal);
        LibelleVaccinDto? Libelle(string code) =>
            libelles.TryGetValue(code, out var l) ? new LibelleVaccinDto(l.Fr, l.Nl, l.De, l.En) : null;

        var doses = new List<DoseCarnetDto>();
        foreach (var v in dossier.Vaccinations.OrderBy(v => v.Date).ThenBy(v => v.Dose))
        {
            var lot = v.LotId is { } lotId ? await lots.GetAsync(lotId, cancellationToken) : null;
            doses.Add(new DoseCarnetDto(v.Date, v.VaccinCode, Libelle(v.VaccinCode), v.Dose, lot?.NumeroLot));
        }

        var codes = await risques.CodesAsync(dossier.PersonneId, date, cancellationToken);
        var carnet = new CarnetVaccinationDto(
            query.VaccinationId,
            dossier.PersonneId,
            date,
            doses,
            [.. dossier.TestsTuberculiniques.OrderBy(t => t.DatePose).Select(t => new TestCarnetDto(t.DatePose, t.DateLecture, t.Lecture?.Resultat.ToString(), t.Lecture?.IndurationMm))],
            [.. CalculRappels.Calculer(schemas, codes, dossier, date).Select(r => new RappelCarnetDto(r.VaccinCode, Libelle(r.VaccinCode), r.DoseSuivante, r.DateDue, r.EnRetard))]);

        audit.Enregistrer(ActionAudit.Export, PartiesDossier.TypeObjet(PartiesDossier.Vaccinations), dossier.Id, AccesDemandesCopie.MotifTransmissionCarnet);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return carnet;
    }
}

/// <summary>
/// SAN-43 : le service Documents a publié la copie au travailleur (<c>documents.document-publie</c>, objet
/// <c>demande-copie-dossier</c>) : la demande passe au statut « publiée » avec l'identifiant du document. Les autres
/// publications sont ignorées. Idempotent (inbox, puis statut) ; tracé.
/// </summary>
public sealed class DocumentPublieHandler(IDemandeCopieRepository demandes, IAuditTrail audit, IUnitOfWork unitOfWork) : IIntegrationEventHandler<DocumentPublie>
{
    /// <summary>Type d'objet des copies dans <c>DocumentPublie.ObjetType</c> (contrat avec le service Documents).</summary>
    public const string ObjetCopie = "demande-copie-dossier";

    public async Task HandleAsync(DocumentPublie integrationEvent, CancellationToken cancellationToken)
    {
        if (!string.Equals(integrationEvent.ObjetType, ObjetCopie, StringComparison.Ordinal) || integrationEvent.ObjetId is not { } demandeId)
        {
            return;
        }

        // Demande inconnue (purgée avec son dossier), refusée ou déjà publiée : rien à faire, sans rejouer le message.
        var demande = await demandes.GetAsync(demandeId, cancellationToken);
        if (demande?.Statut != StatutDemandeCopie.Validee || !demande.EnregistrerPublication(integrationEvent.DocumentId, integrationEvent.OccurredAt))
        {
            return;
        }

        audit.Enregistrer(ActionAudit.Modification, PartiesDossier.TypeObjet(PartiesDossier.Copie), AccesDemandesCopie.ObjetAudit(demande), AccesDemandesCopie.MotifPublication);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
