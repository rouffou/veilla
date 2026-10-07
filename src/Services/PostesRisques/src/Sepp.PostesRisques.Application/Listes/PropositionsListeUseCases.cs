using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.PostesRisques.Domain;
using Sepp.PostesRisques.Domain.Listes;

namespace Sepp.PostesRisques.Application.Listes;

public sealed record LignePropositionListeDto(TypeModification Type, Guid PersonneId, Guid PosteId);

public sealed record PropositionListeDto(
    Guid Id,
    Guid ListeNominativeId,
    Guid AffilieId,
    TypeListeNominative TypeListe,
    int VersionListe,
    OrigineProposition Origine,
    string ProposeePar,
    DateTimeOffset DateProposition,
    string Motif,
    StatutProposition Statut,
    IReadOnlyList<LignePropositionListeDto> Lignes,
    string? DecidePar,
    DateTimeOffset? DateDecision,
    string? MotifRefus,
    Guid? ListeResultanteId)
{
    public static PropositionListeDto From(PropositionListeNominative p) =>
        new(p.Id, p.ListeNominativeId, p.AffilieId, p.TypeListe, p.VersionListe, p.Origine, p.ProposeePar, p.DateProposition, p.Motif,
            p.Statut, p.Lignes.Select(l => new LignePropositionListeDto(l.Type, l.PersonneId, l.PosteId)).ToList(),
            p.DecidePar, p.DateDecision, p.MotifRefus, p.ListeResultanteId);
}

/// <summary>AFF-31 : proposition de modification d'une liste nominative (employeur via le portail, ou interne).</summary>
public sealed record SoumettrePropositionListe(Guid ListeNominativeId, string Motif, IReadOnlyList<DemandeLigneListe> Lignes);

public sealed class SoumettrePropositionListeHandler(
    IListeNominativeRepository listes,
    IPosteRepository postes,
    IPropositionListeRepository propositions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre,
    TimeProvider clock) : ICommandHandler<SoumettrePropositionListe, Guid>
{
    public async Task<Result<Guid>> HandleAsync(SoumettrePropositionListe command, CancellationToken cancellationToken)
    {
        var liste = await listes.GetAsync(command.ListeNominativeId, cancellationToken);
        if (liste is null)
        {
            return ObtenirListeNominativeHandler.ListeInconnue(command.ListeNominativeId);
        }

        if (Acces.Verifier(currentUser, perimetre, liste.AffilieId, Permissions.PosteEcrire, Permissions.RisquePosteValider) is { } refus)
        {
            return refus;
        }

        foreach (var ajout in (command.Lignes ?? []).Where(l => l.Type == TypeModification.Ajout))
        {
            var poste = await postes.GetAsync(ajout.PosteId, cancellationToken);
            if (poste is null || poste.AffilieId != liste.AffilieId)
            {
                return Error.Validation("proposition.poste-invalide", $"Le poste {ajout.PosteId} n'appartient pas à l'affilié.");
            }
        }

        PropositionListeNominative proposition;
        try
        {
            proposition = PropositionListeNominative.Soumettre(
                liste,
                perimetre.EstExterne ? OrigineProposition.PortailEmployeur : OrigineProposition.Interne,
                currentUser.UserId,
                clock.GetUtcNow(),
                command.Motif,
                command.Lignes ?? []);
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

/// <summary>
/// AFF-31 : validation par le CPMT. Une nouvelle version de la liste est produite (version d'origine moins les
/// retraits, plus les ajouts) ; la version d'origine et la proposition restent dans l'historique.
/// </summary>
public sealed record ValiderPropositionListe(Guid PropositionId);

public sealed class ValiderPropositionListeHandler(
    IPropositionListeRepository propositions,
    IListeNominativeRepository listes,
    CalculListesNominatives calcul,
    PolitiqueConservationListes conservation,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    ICurrentUser currentUser,
    TimeProvider clock) : ICommandHandler<ValiderPropositionListe, ListeGenereeDto>
{
    public async Task<Result<ListeGenereeDto>> HandleAsync(ValiderPropositionListe command, CancellationToken cancellationToken)
    {
        if (Acces.Permission(currentUser, Permissions.RisquePosteValider, "Seul le CPMT valide une modification de liste nominative.") is { } interdit)
        {
            return interdit;
        }

        var proposition = await propositions.GetAsync(command.PropositionId, cancellationToken);
        if (proposition is null)
        {
            return PropositionInconnue(command.PropositionId);
        }

        var origine = await listes.GetAsync(proposition.ListeNominativeId, cancellationToken);
        if (origine is null)
        {
            return ObtenirListeNominativeHandler.ListeInconnue(proposition.ListeNominativeId);
        }

        var concernes = await calcul.PostesConcernesAsync(origine.AffilieId, origine.Type, origine.DateReference, cancellationToken);
        var ajouts = new Dictionary<(Guid, Guid), LigneCalculee>();
        foreach (var ajout in proposition.Lignes.Where(l => l.Type == TypeModification.Ajout))
        {
            if (!concernes.TryGetValue(ajout.PosteId, out var codes))
            {
                return Error.Validation(
                    "proposition.poste-non-expose",
                    $"Le poste {ajout.PosteId} n'expose à aucun risque de ce type de liste au {origine.DateReference:yyyy-MM-dd} : faites d'abord valider son profil de risques (AFF-14).");
            }

            ajouts[(ajout.PersonneId, ajout.PosteId)] = new LigneCalculee(
                ajout.PersonneId, ajout.PosteId, codes,
                await calcul.DerniereEvaluationAsync(origine.AffilieId, ajout.PersonneId, origine.DateReference, cancellationToken),
                OrigineLigne.AjustementValide);
        }

        try
        {
            var maintenant = clock.GetUtcNow();
            var nouvelle = ListeNominative.Generer(
                origine.AffilieId,
                origine.Type,
                await listes.DerniereVersionAsync(origine.AffilieId, origine.Type, cancellationToken) + 1,
                origine.DateReference,
                maintenant,
                currentUser.UserId,
                await conservation.AnneesAsync(DateOnly.FromDateTime(maintenant.UtcDateTime), cancellationToken),
                proposition.AppliquerA(origine, l => ajouts[(l.PersonneId, l.PosteId)]),
                proposition.Id);
            proposition.Valider(currentUser.UserId, maintenant, nouvelle);
            listes.Add(nouvelle);
            PublicationListes.Publier(outbox, nouvelle);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new ListeGenereeDto(nouvelle.Id, nouvelle.Version, nouvelle.Lignes.Count);
        }
        catch (DomainException ex)
        {
            return Error.Validation("proposition.invalide", ex.Message);
        }
    }

    internal static Error PropositionInconnue(Guid id) => Error.NotFound("proposition.inconnue", $"Proposition {id} inconnue.");
}

public sealed record RefuserPropositionListe(Guid PropositionId, string Motif);

public sealed class RefuserPropositionListeHandler(
    IPropositionListeRepository propositions,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider clock) : ICommandHandler<RefuserPropositionListe, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RefuserPropositionListe command, CancellationToken cancellationToken)
    {
        if (Acces.Permission(currentUser, Permissions.RisquePosteValider, "Seul le CPMT refuse une modification de liste nominative.") is { } interdit)
        {
            return interdit;
        }

        var proposition = await propositions.GetAsync(command.PropositionId, cancellationToken);
        if (proposition is null)
        {
            return ValiderPropositionListeHandler.PropositionInconnue(command.PropositionId);
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

public sealed record ListerPropositionsListe(Guid? AffilieId, StatutProposition? Statut);

public sealed class ListerPropositionsListeHandler(
    IPropositionListeRepository propositions,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre) : IQueryHandler<ListerPropositionsListe, IReadOnlyList<PropositionListeDto>>
{
    public async Task<Result<IReadOnlyList<PropositionListeDto>>> HandleAsync(ListerPropositionsListe query, CancellationToken cancellationToken)
    {
        if (Acces.Permission(currentUser, Permissions.PosteLire, "Accès aux propositions refusé.") is { } interdit)
        {
            return interdit;
        }

        if (Acces.Filtre(perimetre, query.AffilieId) is { } refus)
        {
            return refus;
        }

        return (await propositions.ListAsync(query.AffilieId, query.Statut, cancellationToken))
            .Where(p => perimetre.PeutAcceder(p.AffilieId))
            .OrderByDescending(p => p.DateProposition)
            .Select(PropositionListeDto.From)
            .ToList();
    }
}

public sealed record ObtenirPropositionListe(Guid Id);

public sealed class ObtenirPropositionListeHandler(
    IPropositionListeRepository propositions,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre) : IQueryHandler<ObtenirPropositionListe, PropositionListeDto>
{
    public async Task<Result<PropositionListeDto>> HandleAsync(ObtenirPropositionListe query, CancellationToken cancellationToken)
    {
        var proposition = await propositions.GetAsync(query.Id, cancellationToken);
        if (proposition is null)
        {
            return ValiderPropositionListeHandler.PropositionInconnue(query.Id);
        }

        return Acces.Verifier(currentUser, perimetre, proposition.AffilieId, Permissions.PosteLire) is { } refus
            ? refus
            : PropositionListeDto.From(proposition);
    }
}
