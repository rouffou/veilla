using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.PostesRisques;
using Sepp.PostesRisques.Domain;
using Sepp.PostesRisques.Domain.Postes;

namespace Sepp.PostesRisques.Application.Postes;

public sealed record LignePropositionDto(TypeModification Type, Guid RisqueId, string RisqueCode, NiveauExposition? NiveauExposition);

public sealed record PropositionPosteRisqueDto(
    Guid Id,
    Guid PosteId,
    Guid AffilieId,
    OrigineProposition Origine,
    string ProposeePar,
    DateTimeOffset DateProposition,
    string Motif,
    DateOnly ValideDu,
    StatutProposition Statut,
    IReadOnlyList<LignePropositionDto> Lignes,
    DateOnly? DateAvisCppt,
    Guid? DocumentAvisCpptId,
    string? DecidePar,
    DateTimeOffset? DateDecision,
    string? MotifRefus)
{
    public static PropositionPosteRisqueDto From(PropositionPosteRisque p) =>
        new(p.Id, p.PosteId, p.AffilieId, p.Origine, p.ProposeePar, p.DateProposition, p.Motif, p.ValideDu, p.Statut,
            p.Lignes.Select(l => new LignePropositionDto(l.Type, l.RisqueId, l.RisqueCode, l.NiveauExposition)).ToList(),
            p.DateAvisCppt, p.DocumentAvisCpptId, p.DecidePar, p.DateDecision, p.MotifRefus);
}

public sealed record LigneRisqueSaisie(TypeModification Type, string RisqueCode, NiveauExposition? NiveauExposition);

public sealed record AvisCpptSaisie(DateOnly Date, Guid DocumentId);

/// <summary>
/// AFF-14 / AFF-31 : proposition de modification du lien poste ↔ risque, par un gestionnaire, par l'employeur
/// ou le SIPP via le portail, ou par le CPMT lui-même. Sans effet tant que le CPMT ne l'a pas validée.
/// </summary>
public sealed record SoumettrePropositionPosteRisque(
    Guid PosteId,
    string Motif,
    DateOnly ValideDu,
    IReadOnlyList<LigneRisqueSaisie> Lignes,
    AvisCpptSaisie? AvisCppt);

public sealed class SoumettrePropositionPosteRisqueHandler(
    IPosteRepository postes,
    IRisqueRepository risques,
    IPropositionPosteRisqueRepository propositions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre,
    TimeProvider clock) : ICommandHandler<SoumettrePropositionPosteRisque, Guid>
{
    public async Task<Result<Guid>> HandleAsync(SoumettrePropositionPosteRisque command, CancellationToken cancellationToken)
    {
        var poste = await postes.GetAsync(command.PosteId, cancellationToken);
        if (poste is null)
        {
            return Erreurs.PosteInconnu(command.PosteId);
        }

        if (Acces.Verifier(currentUser, perimetre, poste.AffilieId, Permissions.PosteEcrire, Permissions.RisquePosteValider) is { } refus)
        {
            return refus;
        }

        var demandes = new List<DemandeLienRisque>();
        foreach (var ligne in command.Lignes ?? [])
        {
            var risque = string.IsNullOrWhiteSpace(ligne.RisqueCode)
                ? null
                : await risques.GetByCodeAsync(ligne.RisqueCode.Trim().ToUpperInvariant(), cancellationToken);
            if (risque is null)
            {
                return Error.Validation("risque.inconnu", $"Risque '{ligne.RisqueCode}' inconnu dans le référentiel.");
            }

            var expose = poste.Risques.Any(r => r.RisqueId == risque.Id && r.Validite.IsOpen);
            if (ligne.Type == TypeModification.Ajout && expose)
            {
                return Error.Validation("proposition.incoherente", $"Le poste est déjà exposé au risque {risque.Code}.");
            }

            if (ligne.Type != TypeModification.Ajout && !expose)
            {
                return Error.Validation("proposition.incoherente", $"Le poste n'est pas exposé au risque {risque.Code}.");
            }

            demandes.Add(new DemandeLienRisque(ligne.Type, risque.Id, risque.Code, ligne.NiveauExposition));
        }

        PropositionPosteRisque proposition;
        try
        {
            proposition = PropositionPosteRisque.Soumettre(
                poste,
                perimetre.EstExterne ? OrigineProposition.PortailEmployeur : OrigineProposition.Interne,
                currentUser.UserId,
                clock.GetUtcNow(),
                command.Motif,
                command.ValideDu,
                demandes);
            if (command.AvisCppt is { } avis)
            {
                proposition.JoindreAvisCppt(avis.Date, avis.DocumentId);
            }
        }
        catch (DomainException ex)
        {
            return Error.Validation("proposition.invalide", ex.Message);
        }

        propositions.Add(proposition);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return proposition.Id;
    }
}

/// <summary>Joint l'avis du Comité PPT (date, pièce jointe) à une proposition en attente (AFF-14).</summary>
public sealed record JoindreAvisCppt(Guid PropositionId, DateOnly Date, Guid DocumentId);

public sealed class JoindreAvisCpptHandler(
    IPropositionPosteRisqueRepository propositions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre) : ICommandHandler<JoindreAvisCppt, Unit>
{
    public async Task<Result<Unit>> HandleAsync(JoindreAvisCppt command, CancellationToken cancellationToken)
    {
        var proposition = await propositions.GetAsync(command.PropositionId, cancellationToken);
        if (proposition is null)
        {
            return PropositionInconnue(command.PropositionId);
        }

        if (Acces.Verifier(currentUser, perimetre, proposition.AffilieId, Permissions.PosteEcrire, Permissions.RisquePosteValider) is { } refus)
        {
            return refus;
        }

        try
        {
            proposition.JoindreAvisCppt(command.Date, command.DocumentId);
        }
        catch (DomainException ex)
        {
            return Error.Validation("proposition.invalide", ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    internal static Error PropositionInconnue(Guid id) => Error.NotFound("proposition.inconnue", $"Proposition {id} inconnue.");
}

/// <summary>
/// AFF-14 : validation par le CPMT. Le nouveau profil de risques est appliqué au poste et
/// ProfilRisquePosteModifie est publié (outbox) — jamais avant la validation.
/// </summary>
public sealed record ValiderPropositionPosteRisque(Guid PropositionId, DateOnly? DateAvisCppt, Guid? DocumentAvisCpptId);

public sealed class ValiderPropositionPosteRisqueHandler(
    IPropositionPosteRisqueRepository propositions,
    IPosteRepository postes,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre,
    TimeProvider clock) : ICommandHandler<ValiderPropositionPosteRisque, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ValiderPropositionPosteRisque command, CancellationToken cancellationToken)
    {
        if (Acces.Permission(currentUser, Permissions.RisquePosteValider, "Seul le CPMT valide une modification du lien poste ↔ risque.") is { } interdit)
        {
            return interdit;
        }

        var proposition = await propositions.GetAsync(command.PropositionId, cancellationToken);
        if (proposition is null)
        {
            return JoindreAvisCpptHandler.PropositionInconnue(command.PropositionId);
        }

        if (Acces.Verifier(currentUser, perimetre, proposition.AffilieId, Permissions.RisquePosteValider) is { } refus)
        {
            return refus;
        }

        var poste = await postes.GetAsync(proposition.PosteId, cancellationToken);
        if (poste is null)
        {
            return Erreurs.PosteInconnu(proposition.PosteId);
        }

        try
        {
            proposition.Valider(currentUser.UserId, clock.GetUtcNow(), command.DateAvisCppt, command.DocumentAvisCpptId);
            poste.AppliquerProposition(proposition);
        }
        catch (DomainException ex)
        {
            return Error.Validation("proposition.invalide", ex.Message);
        }

        outbox.Add(new ProfilRisquePosteModifie(
            poste.Id,
            poste.AffilieId,
            poste.RisquesAu(proposition.ValideDu).Select(r => r.RisqueCode).ToList(),
            proposition.ValideDu));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record RefuserPropositionPosteRisque(Guid PropositionId, string Motif);

public sealed class RefuserPropositionPosteRisqueHandler(
    IPropositionPosteRisqueRepository propositions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider clock) : ICommandHandler<RefuserPropositionPosteRisque, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RefuserPropositionPosteRisque command, CancellationToken cancellationToken)
    {
        if (Acces.Permission(currentUser, Permissions.RisquePosteValider, "Seul le CPMT refuse une modification du lien poste ↔ risque.") is { } interdit)
        {
            return interdit;
        }

        var proposition = await propositions.GetAsync(command.PropositionId, cancellationToken);
        if (proposition is null)
        {
            return JoindreAvisCpptHandler.PropositionInconnue(command.PropositionId);
        }

        try
        {
            proposition.Refuser(currentUser.UserId, clock.GetUtcNow(), command.Motif);
        }
        catch (DomainException ex)
        {
            return Error.Validation("proposition.invalide", ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record ListerPropositionsPosteRisque(Guid? AffilieId, Guid? PosteId, StatutProposition? Statut);

public sealed class ListerPropositionsPosteRisqueHandler(
    IPropositionPosteRisqueRepository propositions,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre) : IQueryHandler<ListerPropositionsPosteRisque, IReadOnlyList<PropositionPosteRisqueDto>>
{
    public async Task<Result<IReadOnlyList<PropositionPosteRisqueDto>>> HandleAsync(ListerPropositionsPosteRisque query, CancellationToken cancellationToken)
    {
        if (Acces.Permission(currentUser, Permissions.PosteLire, "Accès aux propositions refusé.") is { } interdit)
        {
            return interdit;
        }

        if (Acces.Filtre(perimetre, query.AffilieId) is { } refus)
        {
            return refus;
        }

        var liste = await propositions.ListAsync(query.AffilieId, query.PosteId, query.Statut, cancellationToken);
        return liste.Where(p => perimetre.PeutAcceder(p.AffilieId))
            .OrderByDescending(p => p.DateProposition)
            .Select(PropositionPosteRisqueDto.From)
            .ToList();
    }
}

public sealed record ObtenirPropositionPosteRisque(Guid Id);

public sealed class ObtenirPropositionPosteRisqueHandler(
    IPropositionPosteRisqueRepository propositions,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre) : IQueryHandler<ObtenirPropositionPosteRisque, PropositionPosteRisqueDto>
{
    public async Task<Result<PropositionPosteRisqueDto>> HandleAsync(ObtenirPropositionPosteRisque query, CancellationToken cancellationToken)
    {
        var proposition = await propositions.GetAsync(query.Id, cancellationToken);
        if (proposition is null)
        {
            return JoindreAvisCpptHandler.PropositionInconnue(query.Id);
        }

        return Acces.Verifier(currentUser, perimetre, proposition.AffilieId, Permissions.PosteLire) is { } refus
            ? refus
            : PropositionPosteRisqueDto.From(proposition);
    }
}
