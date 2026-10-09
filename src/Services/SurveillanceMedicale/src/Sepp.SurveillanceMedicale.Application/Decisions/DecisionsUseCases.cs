using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.SurveillanceMedicale.Application.Examens;
using Sepp.SurveillanceMedicale.Domain.Decisions;
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
public sealed record DecisionDto(DecisionResumeDto Resume, string AuteurId, string? Justification, string? Recommandations, string? ReferenceSignature, IReadOnlyList<RecoursDto> Recours);

public static class DecisionProjections
{
    public static DecisionResumeDto Resume(this Decision d) => new(
        d.Id, d.PersonneId, d.AffilieId, d.TypeExamen, d.DateExamen, CodesDecision.Code(d.Categorie), d.Mesures, d.ValideJusquAu, d.Statut, d.SigneeLe, d.DocumentId);

    public static DecisionDto Complete(this Decision d) => new(
        d.Resume(), d.AuteurId, d.Justification, d.Recommandations, d.ReferenceSignature,
        [.. d.Recours.OrderBy(r => r.DateIntroduction).Select(r => new RecoursDto(
            r.Id, r.Type, r.DateIntroduction, r.DateLimiteIntroduction, r.IntroduitDansLeDelai, r.DateLimiteIssue, r.Issue, r.DateIssue, r.Commentaire))]);
}

/// <summary>Accès aux décisions : rédaction par le CPMT (zone médicale) et lecture du résumé selon la matrice §3.3.</summary>
public sealed class AccesDecisions(IDecisionRepository decisions, GardeDossier garde, ICurrentUser user, IContexteAcces contexte, IAuditTrail audit)
{
    public static readonly Error Inconnue = Error.NotFound("decision.inconnue", "Décision inconnue.");

    /// <summary>Rédaction, signature, recours : CPMT (<c>decision:ecrire</c>) avec accès au dossier.</summary>
    public async Task<Result<Decision>> EcrireAsync(Guid decisionId, ActionAudit action, CancellationToken cancellationToken)
    {
        if (Regles.Permission(user, Permissions.DecisionEcrire, "Seul le CPMT rédige et signe les décisions d'évaluation de santé.") is { } interdit)
        {
            return interdit;
        }

        var decision = await decisions.GetAsync(decisionId, cancellationToken);
        if (decision is null)
        {
            return Inconnue;
        }

        var dossier = await garde.EcrireAsync(decision.DossierId, PartiesDossier.Decision, action, cancellationToken);
        return dossier.IsSuccess ? decision : dossier.Error!;
    }

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

/// <summary>SAN-31 : rédaction de la décision d'un examen (une décision par examen).</summary>
public sealed record RedigerDecision(Guid ExamenId, ContenuDecisionSaisi Contenu);

public sealed class RedigerDecisionHandler(AccesExamens examens, IDecisionRepository decisions, ICurrentUser user, IUnitOfWork unitOfWork)
    : ICommandHandler<RedigerDecision, Guid>
{
    public async Task<Result<Guid>> HandleAsync(RedigerDecision command, CancellationToken cancellationToken)
    {
        if (Regles.Permission(user, Permissions.DecisionEcrire, "Seul le CPMT rédige les décisions d'évaluation de santé.") is { } interdit)
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

public sealed record ModifierDecision(Guid DecisionId, ContenuDecisionSaisi Contenu);

public sealed class ModifierDecisionHandler(AccesDecisions acces, IUnitOfWork unitOfWork) : ICommandHandler<ModifierDecision, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ModifierDecision command, CancellationToken cancellationToken)
    {
        var decision = await acces.EcrireAsync(command.DecisionId, ActionAudit.Modification, cancellationToken);
        if (!decision.IsSuccess)
        {
            return decision.Error!;
        }

        var resultat = Regles.Appliquer("decision.invalide", () => decision.Value.Modifier(command.Contenu.ToContenu()));
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
public sealed record SignerDecision(Guid DecisionId);

public sealed class SignerDecisionHandler(
    AccesDecisions acces, IExamenRepository examens, ISignatureQualifiee signature, ICurrentUser user, ParametresMedicaux parametres,
    IIntegrationEventOutbox outbox, IUnitOfWork unitOfWork)
    : ICommandHandler<SignerDecision, DecisionResumeDto>
{
    public async Task<Result<DecisionResumeDto>> HandleAsync(SignerDecision command, CancellationToken cancellationToken)
    {
        var acces1 = await acces.EcrireAsync(command.DecisionId, ActionAudit.Modification, cancellationToken);
        if (!acces1.IsSuccess)
        {
            return acces1.Error!;
        }

        var decision = acces1.Value;
        if (decision.Statut != StatutDecision.Brouillon)
        {
            return Error.Conflict("decision.deja-signee", "Cette décision est déjà signée.");
        }

        if (!string.Equals(decision.AuteurId, user.UserId, StringComparison.Ordinal))
        {
            return Error.Forbidden("decision.signataire", "Seul le CPMT auteur de la décision peut la signer.");
        }

        var examen = await examens.GetAsync(decision.ExamenId, cancellationToken);
        if (examen?.Statut != StatutExamen.Cloture)
        {
            return Error.Validation("decision.examen-non-cloture", "Clôturez l'examen avant de signer la décision.");
        }

        var formulaire = Formulaires.Construire(decision, ExemplaireFormulaire.Dossier, parametres.DelaisRecours, await parametres.PolitiqueRecoursAsync(decision.DateExamen, cancellationToken));
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
    string CpmtId,
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
        return Formulaires.Construire(decision.Value, query.Exemplaire, parametres.DelaisRecours, politique);
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
        var decision = await acces.EcrireAsync(command.DecisionId, ActionAudit.Modification, cancellationToken);
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
