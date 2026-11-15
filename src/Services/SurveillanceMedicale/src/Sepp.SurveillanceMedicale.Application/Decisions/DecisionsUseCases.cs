using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.SurveillanceMedicale.Application.Examens;
using Sepp.SurveillanceMedicale.Domain.Decisions;
using Sepp.SurveillanceMedicale.Domain.Dossiers;
using Sepp.SurveillanceMedicale.Domain.Examens;

namespace Sepp.SurveillanceMedicale.Application.Decisions;

/// <summary>Ce qui sort de la zone médicale (§2.1, ARC-06) : catégorie, mesures codées, validité — jamais le motif médical.</summary>
public sealed record DecisionResumeDto(
    Guid Id, Guid PersonneId, Guid AffilieId, string TypeExamen, DateOnly DateExamen, string Categorie, IReadOnlyList<string> Mesures,
    DateOnly? ValideJusquAu, StatutDecision Statut, DateTimeOffset? SigneeLe, Guid? DocumentId);

public sealed record RecoursDto(
    Guid Id, TypeRecours Type, DateOnly DateIntroduction, DateOnly DateLimiteIntroduction, bool IntroduitDansLeDelai, DateOnly DateLimiteIssue,
    IssueRecours? Issue, DateOnly? DateIssue, string? Commentaire);

/// <summary>Décision complète (zone médicale) : justification, recommandations et procédures.</summary>
/// <remarks>
/// <c>AuteurId</c> : CPMT signataire, vide tant que la décision est un brouillon (ADR 0017). <c>RedigeePar</c>,
/// <c>ModifieePar</c>, <c>ModifieeLe</c> : qui a préparé ou modifié le brouillon, visible du CPMT avant de signer.
/// <c>Version</c> : à renvoyer dans l'en-tête <c>If-Match</c> pour modifier ou signer cette version.
/// </remarks>
public sealed record DecisionDto(
    DecisionResumeDto Resume, string? AuteurId, string RedigeePar, string? ModifieePar, DateTimeOffset? ModifieeLe, int Version,
    string? Justification, string? Recommandations, string? ReferenceSignature, IReadOnlyList<RecoursDto> Recours);

public static class DecisionProjections
{
    public static DecisionResumeDto Resume(this Decision d) => new(
        d.Id, d.PersonneId, d.AffilieId, d.TypeExamen, d.DateExamen, CodesDecision.Code(d.Categorie), d.Mesures, d.ValideJusquAu, d.Statut, d.SigneeLe, d.DocumentId);

    public static DecisionDto Complete(this Decision d) => new(
        d.Resume(), d.AuteurId, d.RedigeePar, d.ModifieePar, d.ModifieeLe, d.VersionContenu, d.Justification, d.Recommandations, d.ReferenceSignature,
        [.. d.Recours.OrderBy(r => r.DateIntroduction).Select(r => new RecoursDto(
            r.Id, r.Type, r.DateIntroduction, r.DateLimiteIntroduction, r.IntroduitDansLeDelai, r.DateLimiteIssue, r.Issue, r.DateIssue, r.Commentaire))]);
}

/// <summary>
/// Accès aux décisions : rédaction et modification par le CPMT ou l'infirmier (<c>decision:ecrire</c>), signature et
/// issue des recours par le seul CPMT (<c>decision:signer</c>, #401), lecture du résumé selon la matrice §3.3.
/// </summary>
public sealed class AccesDecisions(IDecisionRepository decisions, GardeDossier garde, ICurrentUser user, IContexteAcces contexte, IAuditTrail audit)
{
    public static readonly Error Inconnue = Error.NotFound("decision.inconnue", "Décision inconnue.");

    public const string MessageEcrire = "Seuls les CPMT, CPMT dirigeant compris, et l'infirmier rédigent les décisions d'évaluation de santé.";

    public const string MessageSigner = "Seul un CPMT signe les décisions d'évaluation de santé et enregistre l'issue des recours.";

    public static readonly Error RelationDeSoinRequise = Error.Forbidden(
        "decision.relation-de-soin-requise",
        "Le brouillon d'un autre professionnel ne se modifie qu'en relation de soin sur l'examen de la décision ou par bris de glace motivé (X-Bris-De-Glace), jamais sur simple motif.");

    public static readonly Error SignatureHorsRelation = Error.Forbidden(
        "decision.signature-hors-relation",
        "Hors relation de soin, la signature exige un bris de glace motivé (X-Bris-De-Glace), un simple motif ne suffit pas.");

    /// <summary>Écriture courante (<c>decision:ecrire</c>) : introduction d'un recours ou d'une concertation.</summary>
    public async Task<Result<Decision>> EcrireAsync(Guid decisionId, ActionAudit action, CancellationToken cancellationToken) =>
        SeulementDecision(await AccederAsync(decisionId, action, Permissions.DecisionEcrire, MessageEcrire, cancellationToken));

    /// <summary>
    /// Modification d'un brouillon (<c>decision:ecrire</c>). Le brouillon d'un autre ne se modifie qu'en relation de soin sur
    /// l'examen de la décision (professionnel de cet examen ou CPMT gestionnaire du dossier) ou par bris de glace motivé,
    /// jamais sur simple motif ni par un autre examen. La règle vaut pour tous, CPMT compris (#401, ADR 0017). Ses propres
    /// brouillons suivent les règles habituelles (ARC-41).
    /// </summary>
    public async Task<Result<Decision>> ModifierAsync(Guid decisionId, CancellationToken cancellationToken)
    {
        var acces = await AccederAsync(decisionId, ActionAudit.Modification, Permissions.DecisionEcrire, MessageEcrire, cancellationToken);
        if (!acces.IsSuccess)
        {
            return acces.Error!;
        }

        var (decision, dossier) = acces.Value;
        var brouillonDUnAutre = !string.Equals(decision.RedigeePar, user.UserId, StringComparison.Ordinal);
        return !brouillonDUnAutre || await EnRelationSurExamenOuBrisDeGlaceAsync(decision, dossier, cancellationToken)
            ? decision
            : RelationDeSoinRequise;
    }

    /// <summary>
    /// Signature (<c>decision:signer</c>, ADR 0017) : en relation de soin sur l'examen de la décision (professionnel de cet
    /// examen ou CPMT gestionnaire du dossier), ou par bris de glace motivé, tracé et signalé (ARC-41). Ni un simple motif ni
    /// un autre examen du dossier (par exemple un examen spontané ouvert sur motif) ne suffisent (#495).
    /// </summary>
    public async Task<Result<Decision>> SignerAsync(Guid decisionId, CancellationToken cancellationToken)
    {
        var acces = await AccederAsync(decisionId, ActionAudit.Modification, Permissions.DecisionSigner, MessageSigner, cancellationToken);
        if (!acces.IsSuccess)
        {
            return acces.Error!;
        }

        var (decision, dossier) = acces.Value;
        return await EnRelationSurExamenOuBrisDeGlaceAsync(decision, dossier, cancellationToken) ? decision : SignatureHorsRelation;
    }

    /// <summary>
    /// Bris de glace motivé (tracé et signalé, ARC-41), ou relation de soin sur l'examen de la décision : professionnel de
    /// cet examen ou CPMT gestionnaire du dossier (ADR 0017).
    /// </summary>
    private async Task<bool> EnRelationSurExamenOuBrisDeGlaceAsync(Decision decision, DossierSante dossier, CancellationToken cancellationToken) =>
        (contexte.BrisDeGlace && MotifAcces.Normaliser(contexte.Motif) is not null)
        || await garde.EstEnRelationDeSoinSurExamenAsync(dossier, decision.ExamenId, cancellationToken);

    /// <summary>Acte réservé au CPMT (<c>decision:signer</c>) : issue d'un recours.</summary>
    public async Task<Result<Decision>> ActeCpmtAsync(Guid decisionId, ActionAudit action, CancellationToken cancellationToken) =>
        SeulementDecision(await AccederAsync(decisionId, action, Permissions.DecisionSigner, MessageSigner, cancellationToken));

    private async Task<Result<(Decision Decision, DossierSante Dossier)>> AccederAsync(
        Guid decisionId, ActionAudit action, string permission, string message, CancellationToken cancellationToken)
    {
        if (Regles.Permission(user, permission, message) is { } interdit)
        {
            return interdit;
        }

        var decision = await decisions.GetAsync(decisionId, cancellationToken);
        if (decision is null)
        {
            return Inconnue;
        }

        var dossier = await garde.EcrireAsync(decision.DossierId, PartiesDossier.Decision, action, cancellationToken);
        return dossier.IsSuccess ? Result<(Decision, DossierSante)>.Success((decision, dossier.Value)) : dossier.Error!;
    }

    private static Result<Decision> SeulementDecision(Result<(Decision Decision, DossierSante Dossier)> acces) =>
        acces.IsSuccess ? acces.Value.Decision : acces.Error!;

    /// <summary>Lecture complète (justification, recommandations) : personnel médical, secret médical.</summary>
    public async Task<Result<Decision>> LireCompleteAsync(Guid decisionId, CancellationToken cancellationToken)
    {
        if (garde.VerifierPermission(ActionAudit.Lecture) is { } interdit)
        {
            return interdit;
        }

        var decision = await decisions.GetAsync(decisionId, cancellationToken);
        if (decision is null)
        {
            return Inconnue;
        }

        var dossier = await garde.LireAsync(decision.DossierId, PartiesDossier.Decision, cancellationToken);
        return dossier.IsSuccess ? decision : dossier.Error!;
    }

    /// <summary>
    /// §3.3 « Décision d'évaluation de santé » : lecture du résumé par les autres conseillers, le gestionnaire, l'employeur
    /// (son affilié), le travailleur (ses propres décisions). Seule une décision émise est visible hors de la zone médicale.
    /// </summary>
    public async Task<Result<Decision>> LireResumeAsync(Guid decisionId, CancellationToken cancellationToken)
    {
        if (Regles.Permission(user, Permissions.DecisionLire, "Droits insuffisants sur les décisions d'évaluation de santé.") is { } interdit)
        {
            return interdit;
        }

        var decision = await decisions.GetAsync(decisionId, cancellationToken);
        if (decision is null || !EstVisible(decision))
        {
            return Inconnue;
        }

        await audit.EnregistrerLectureAsync(PartiesDossier.TypeObjet(PartiesDossier.Decision), decision.DossierId, MotifAcces.Normaliser(contexte.Motif), false, cancellationToken);
        return decision;
    }

    public bool EstVisible(Decision decision)
    {
        if (decision.Statut != StatutDecision.Emise && !user.HasPermission(Permissions.DossierSanteLire))
        {
            return false;
        }

        if (user.Roles.Contains(Roles.Employeur) || user.Roles.Contains(Roles.Sipp))
        {
            return contexte.AffilieIdJeton == decision.AffilieId;
        }

        return !user.Roles.Contains(Roles.Travailleur) || contexte.PersonneIdJeton == decision.PersonneId;
    }
}

public sealed record ContenuDecisionSaisi(CategorieDecision Categorie, IReadOnlyList<string>? Mesures, DateOnly? ValideJusquAu, string? Justification, string? Recommandations)
{
    public ContenuDecision ToContenu() => new(Categorie, Mesures ?? [], ValideJusquAu, Justification, Recommandations);

    public override string ToString() => $"ContenuDecisionSaisi {{ Categorie = {Categorie} }}";
}

/// <summary>
/// SAN-31 : rédaction de la décision d'un examen (une décision par examen), par le CPMT ou l'infirmier (#401). Le brouillon
/// porte son rédacteur réel (<c>RedigeePar</c>) ; l'auteur légal n'est fixé qu'à la signature (ADR 0017).
/// </summary>
public sealed record RedigerDecision(Guid ExamenId, ContenuDecisionSaisi Contenu);

public sealed class RedigerDecisionHandler(AccesExamens examens, IDecisionRepository decisions, ICurrentUser user, IUnitOfWork unitOfWork)
    : ICommandHandler<RedigerDecision, Guid>
{
    public async Task<Result<Guid>> HandleAsync(RedigerDecision command, CancellationToken cancellationToken)
    {
        if (Regles.Permission(user, Permissions.DecisionEcrire, AccesDecisions.MessageEcrire) is { } interdit)
        {
            return interdit;
        }

        var acces = await examens.ChargerAsync(command.ExamenId, ActionAudit.Creation, cancellationToken);
        if (!acces.IsSuccess)
        {
            return acces.Error!;
        }

        var examen = acces.Value;
        if (examen.Statut == StatutExamen.Annule)
        {
            return Error.Validation("decision.examen-annule", "Un examen annulé ne donne pas lieu à une décision.");
        }

        if (await decisions.GetParExamenAsync(examen.Id, cancellationToken) is not null)
        {
            return Error.Conflict("decision.existe", "Cet examen a déjà une décision.");
        }

        var decision = Regles.Appliquer("decision.invalide", () => Decision.Rediger(
            examen.Id, examen.DossierId, examen.PersonneId, examen.AffilieId, examen.TypeExamen, examen.Date, user.UserId, command.Contenu.ToContenu()));
        if (!decision.IsSuccess)
        {
            return decision.Error!;
        }

        decisions.Add(decision.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return decision.Value.Id;
    }
}

/// <summary>Contrôle de concurrence optimiste (#401) : la modification et la signature portent sur la version relue.</summary>
public static class VersionsDecision
{
    public static readonly Error Requise = Error.Validation(
        "decision.version-requise", "Indiquez la version relue de la décision (en-tête If-Match).");

    public static readonly Error Obsolete = Error.Conflict(
        "decision.version-obsolete", "La décision a été modifiée depuis votre lecture : relisez-la avant de continuer.");

    public static Error? Verifier(Decision decision, int? versionAttendue) =>
        versionAttendue is not { } version ? Requise : decision.EstALaVersion(version) ? null : Obsolete;
}

/// <param name="VersionAttendue">Version relue (<see cref="DecisionDto.Version"/>), transmise par l'en-tête <c>If-Match</c>.</param>
public sealed record ModifierDecision(Guid DecisionId, ContenuDecisionSaisi Contenu, int? VersionAttendue);

public sealed class ModifierDecisionHandler(AccesDecisions acces, ICurrentUser user, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<ModifierDecision, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ModifierDecision command, CancellationToken cancellationToken)
    {
        var decision = await acces.ModifierAsync(command.DecisionId, cancellationToken);
        if (!decision.IsSuccess)
        {
            return decision.Error!;
        }

        if (VersionsDecision.Verifier(decision.Value, command.VersionAttendue) is { } conflit)
        {
            return conflit;
        }

        var resultat = Regles.Appliquer("decision.invalide", () => decision.Value.Modifier(command.Contenu.ToContenu(), user.UserId, horloge.GetUtcNow()));
        if (resultat.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return resultat;
    }
}

/// <summary>
/// SAN-32, SAN-33 : signature qualifiée par le CPMT (port de signature ; eID/itsme réel hors périmètre) puis émission :
/// <c>DecisionEmise</c> (catégorie, mesures codées, validité) déclenche la génération du formulaire en trois
/// exemplaires par le service Documents et sa transmission à l'employeur et au travailleur. L'examen doit être clôturé.
/// </summary>
/// <param name="VersionAttendue">Version relue et signée (<see cref="DecisionDto.Version"/>), en-tête <c>If-Match</c>.</param>
public sealed record SignerDecision(Guid DecisionId, int? VersionAttendue);

public sealed class SignerDecisionHandler(
    AccesDecisions acces, IExamenRepository examens, ISignatureQualifiee signature, ICurrentUser user, ParametresMedicaux parametres,
    IIntegrationEventOutbox outbox, IUnitOfWork unitOfWork)
    : ICommandHandler<SignerDecision, DecisionResumeDto>
{
    public async Task<Result<DecisionResumeDto>> HandleAsync(SignerDecision command, CancellationToken cancellationToken)
    {
        var acces1 = await acces.SignerAsync(command.DecisionId, cancellationToken);
        if (!acces1.IsSuccess)
        {
            return acces1.Error!;
        }

        var decision = acces1.Value;
        if (decision.Statut != StatutDecision.Brouillon)
        {
            return Error.Conflict("decision.deja-signee", "Cette décision est déjà signée.");
        }

        if (VersionsDecision.Verifier(decision, command.VersionAttendue) is { } conflit)
        {
            return conflit;
        }

        var examen = await examens.GetAsync(decision.ExamenId, cancellationToken);
        if (examen?.Statut != StatutExamen.Cloture)
        {
            return Error.Validation("decision.examen-non-cloture", "Clôturez l'examen avant de signer la décision.");
        }

        // L'empreinte signée porte sur le formulaire tel qu'il sera émis, signataire compris (auteur légal, ADR 0017).
        var delais = await parametres.DelaisRecoursAsync(decision.DateExamen, cancellationToken);
        var politique = await parametres.PolitiqueRecoursAsync(decision.DateExamen, cancellationToken);
        var formulaire = Formulaires.Construire(decision, ExemplaireFormulaire.Dossier, delais, politique) with { CpmtId = user.UserId };
        var empreinte = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(formulaire))));
        var signee = await signature.SignerAsync(new DemandeSignature(decision.Id, user.UserId, empreinte), cancellationToken);

        var resultat = Regles.Appliquer("decision.signature", () => decision.Signer(user.UserId, signee.Reference, signee.SigneeLe));
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        EvenementsIntegration.Publier(decision, outbox);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return decision.Resume();
    }
}

public sealed record ObtenirDecision(Guid DecisionId);

public sealed class ObtenirDecisionHandler(AccesDecisions acces) : IQueryHandler<ObtenirDecision, DecisionResumeDto>
{
    public async Task<Result<DecisionResumeDto>> HandleAsync(ObtenirDecision query, CancellationToken cancellationToken)
    {
        var decision = await acces.LireResumeAsync(query.DecisionId, cancellationToken);
        return decision.IsSuccess ? decision.Value.Resume() : decision.Error!;
    }
}

public sealed record ObtenirDecisionComplete(Guid DecisionId);

public sealed class ObtenirDecisionCompleteHandler(AccesDecisions acces) : IQueryHandler<ObtenirDecisionComplete, DecisionDto>
{
    public async Task<Result<DecisionDto>> HandleAsync(ObtenirDecisionComplete query, CancellationToken cancellationToken)
    {
        var decision = await acces.LireCompleteAsync(query.DecisionId, cancellationToken);
        return decision.IsSuccess ? decision.Value.Complete() : decision.Error!;
    }
}

/// <summary>Décisions émises d'une personne visibles par l'utilisateur (portail employeur via BFF, gestionnaire…).</summary>
public sealed record ListerDecisionsPersonne(Guid PersonneId);

public sealed class ListerDecisionsPersonneHandler(
    IDossierSanteRepository dossiers, IDecisionRepository decisions, AccesDecisions acces, ICurrentUser user, IAuditTrail audit)
    : IQueryHandler<ListerDecisionsPersonne, IReadOnlyList<DecisionResumeDto>>
{
    public async Task<Result<IReadOnlyList<DecisionResumeDto>>> HandleAsync(ListerDecisionsPersonne query, CancellationToken cancellationToken)
    {
        if (Regles.Permission(user, Permissions.DecisionLire, "Droits insuffisants sur les décisions d'évaluation de santé.") is { } interdit)
        {
            return interdit;
        }

        var dossier = await dossiers.GetParPersonneAsync(query.PersonneId, cancellationToken);
        if (dossier is null)
        {
            return Array.Empty<DecisionResumeDto>();
        }

        var visibles = (await decisions.ListerParDossierAsync(dossier.Id, cancellationToken)).Where(acces.EstVisible).ToList();
        if (visibles.Count > 0)
        {
            await audit.EnregistrerLectureAsync(PartiesDossier.TypeObjet(PartiesDossier.Decision), dossier.Id, null, false, cancellationToken);
        }

        return visibles.OrderByDescending(d => d.DateExamen).Select(d => d.Resume()).ToList();
    }
}

public enum ExemplaireFormulaire
{
    /// <summary>Exemplaire de l'employeur : décision, mesures, validité ; jamais de donnée médicale.</summary>
    Employeur,

    /// <summary>Exemplaire du travailleur : complété des recommandations.</summary>
    Travailleur,

    /// <summary>Exemplaire versé au dossier de santé : complet (justification et procédures).</summary>
    Dossier,
}

public sealed record VoieRecoursDto(TypeRecours Type, int DelaiJoursOuvrables, DateOnly? DateLimite);

/// <summary>
/// SAN-30 : données du formulaire d'évaluation de santé (annexe I.4-2) pour un exemplaire. Contrat avec le service
/// Documents : il compose le PDF/A (identité de la personne lue au service Personnes, en-tête du SEPP) et le fait
/// signer ; la mention des voies de concertation et de recours (SAN-33) est fournie ici.
/// </summary>
public sealed record FormulaireEvaluationSanteDto(
    ExemplaireFormulaire Exemplaire,
    Guid DecisionId,
    StatutDecision Statut,
    Guid PersonneId,
    Guid AffilieId,
    string TypeExamen,
    DateOnly DateExamen,
    string Categorie,
    IReadOnlyList<string> Mesures,
    DateOnly? ValideJusquAu,
    string? CpmtId,
    DateTimeOffset? SigneeLe,
    string? ReferenceSignature,
    IReadOnlyList<VoieRecoursDto> VoiesDeRecours,
    string? Recommandations,
    string? Justification);

public sealed record ObtenirFormulaire(Guid DecisionId, ExemplaireFormulaire Exemplaire);

public sealed class ObtenirFormulaireHandler(AccesDecisions acces, ParametresMedicaux parametres) : IQueryHandler<ObtenirFormulaire, FormulaireEvaluationSanteDto>
{
    public async Task<Result<FormulaireEvaluationSanteDto>> HandleAsync(ObtenirFormulaire query, CancellationToken cancellationToken)
    {
        // L'exemplaire employeur ne contient aucune donnée médicale : lecture du résumé (§3.3). Les autres sont en zone médicale.
        var decision = query.Exemplaire == ExemplaireFormulaire.Employeur
            ? await acces.LireResumeAsync(query.DecisionId, cancellationToken)
            : await acces.LireCompleteAsync(query.DecisionId, cancellationToken);
        if (!decision.IsSuccess)
        {
            return decision.Error!;
        }

        var politique = await parametres.PolitiqueRecoursAsync(decision.Value.DateExamen, cancellationToken);
        return Formulaires.Construire(decision.Value, query.Exemplaire, await parametres.DelaisRecoursAsync(decision.Value.DateExamen, cancellationToken), politique);
    }
}

internal static class Formulaires
{
    public static FormulaireEvaluationSanteDto Construire(Decision d, ExemplaireFormulaire exemplaire, DelaisRecours delais, PolitiqueRecours politique)
    {
        var options = new[] { TypeRecours.Concertation, TypeRecours.RecoursMedecinInspecteur };
        var voies = options.Select(t =>
        {
            var limite = d.DateRemise is { } remise ? politique.DateLimiteIntroduction(t, remise) : (DateOnly?)null;
            var delai = t == TypeRecours.Concertation ? delais.IntroductionConcertation : delais.IntroductionRecours;
            return new VoieRecoursDto(t, delai, limite);
        }).ToList();

        return new FormulaireEvaluationSanteDto(
            exemplaire, d.Id, d.Statut, d.PersonneId, d.AffilieId, d.TypeExamen, d.DateExamen, CodesDecision.Code(d.Categorie), d.Mesures, d.ValideJusquAu,
            d.AuteurId, d.SigneeLe, d.ReferenceSignature, voies,
            exemplaire == ExemplaireFormulaire.Employeur ? null : d.Recommandations,
            exemplaire == ExemplaireFormulaire.Dossier ? d.Justification : null);
    }
}

/// <summary>SAN-34 : introduction d'une concertation ou d'un recours auprès du médecin-inspecteur social.</summary>
public sealed record IntroduireRecours(Guid DecisionId, TypeRecours Type, DateOnly DateIntroduction);

public sealed class IntroduireRecoursHandler(AccesDecisions acces, ParametresMedicaux parametres, IUnitOfWork unitOfWork) : ICommandHandler<IntroduireRecours, RecoursDto>
{
    public async Task<Result<RecoursDto>> HandleAsync(IntroduireRecours command, CancellationToken cancellationToken)
    {
        var decision = await acces.EcrireAsync(command.DecisionId, ActionAudit.Modification, cancellationToken);
        if (!decision.IsSuccess)
        {
            return decision.Error!;
        }

        var politique = await parametres.PolitiqueRecoursAsync(command.DateIntroduction, cancellationToken);
        var resultat = Regles.Appliquer("recours.invalide", () => decision.Value.IntroduireRecours(command.Type, command.DateIntroduction, politique));
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var r = resultat.Value;
        return new RecoursDto(r.Id, r.Type, r.DateIntroduction, r.DateLimiteIntroduction, r.IntroduitDansLeDelai, r.DateLimiteIssue, r.Issue, r.DateIssue, r.Commentaire);
    }
}

/// <summary>
/// SAN-34 : issue d'une procédure (décision du médecin-inspecteur social pour un recours). Une décision réformée est
/// retransmise (<c>DecisionEmise</c> avec la nouvelle catégorie, même identifiant de décision).
/// </summary>
public sealed record EnregistrerIssueRecours(Guid DecisionId, Guid RecoursId, IssueRecours Issue, DateOnly DateIssue, ContenuDecisionSaisi? Reformation, string? Commentaire)
{
    public override string ToString() => $"EnregistrerIssueRecours {{ DecisionId = {DecisionId}, RecoursId = {RecoursId}, Issue = {Issue} }}";
}

public sealed class EnregistrerIssueRecoursHandler(AccesDecisions acces, IIntegrationEventOutbox outbox, IUnitOfWork unitOfWork)
    : ICommandHandler<EnregistrerIssueRecours, Unit>
{
    public async Task<Result<Unit>> HandleAsync(EnregistrerIssueRecours command, CancellationToken cancellationToken)
    {
        var decision = await acces.ActeCpmtAsync(command.DecisionId, ActionAudit.Modification, cancellationToken);
        if (!decision.IsSuccess)
        {
            return decision.Error!;
        }

        var resultat = Regles.Appliquer("recours.invalide", () => decision.Value.EnregistrerIssue(
            command.RecoursId, command.Issue, command.DateIssue, command.Reformation?.ToContenu(), command.Commentaire));
        if (!resultat.IsSuccess)
        {
            return resultat;
        }

        EvenementsIntegration.Publier(decision.Value, outbox);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return resultat;
    }
}
