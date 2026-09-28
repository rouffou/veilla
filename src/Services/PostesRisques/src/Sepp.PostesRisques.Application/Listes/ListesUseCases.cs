using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.PostesRisques.Domain;
using Sepp.PostesRisques.Domain.Listes;
using Sepp.PostesRisques.Domain.Postes;

namespace Sepp.PostesRisques.Application.Listes;

public sealed record LigneListeDto(Guid PersonneId, Guid PosteId, IReadOnlyList<string> CodesRisques, DateOnly? DateDerniereEvaluation, OrigineLigne Origine);

public sealed record ListeNominativeDto(
    Guid Id,
    Guid AffilieId,
    TypeListeNominative Type,
    int Version,
    DateOnly DateReference,
    DateTimeOffset DateGeneration,
    string GenereePar,
    Guid? DocumentId,
    DateOnly ConserverJusquAu,
    Guid? PropositionId,
    int NombreLignes,
    IReadOnlyList<LigneListeDto> Lignes)
{
    public static ListeNominativeDto From(ListeNominative l, bool avecLignes) =>
        new(l.Id, l.AffilieId, l.Type, l.Version, l.DateReference, l.DateGeneration, l.GenereePar, l.DocumentId, l.ConserverJusquAu,
            l.PropositionId, l.Lignes.Count,
            avecLignes
                ? l.Lignes.Select(x => new LigneListeDto(x.PersonneId, x.PosteId, x.CodesRisques, x.DateDerniereEvaluation, x.Origine)).ToList()
                : []);
}

/// <summary>
/// Calcul des inscriptions d'une liste (AFF-30) : postes actifs de l'affilié exposés, à la date de référence,
/// à un risque d'une catégorie du type de liste ; travailleurs affectés à ces postes (projection de
/// AffectationModifiee) ; date de la dernière évaluation (projection de ExamenCloture). Aucun appel synchrone.
/// </summary>
public sealed class CalculListesNominatives(IPosteRepository postes, IRisqueRepository risques, IProjectionRepository projections)
{
    public async Task<Dictionary<Guid, IReadOnlyList<string>>> PostesConcernesAsync(
        Guid affilieId, TypeListeNominative type, DateOnly date, CancellationToken cancellationToken)
    {
        var categories = ListeNominative.CategoriesDe(type);
        var referentiel = (await risques.ListAsync(cancellationToken)).ToDictionary(r => r.Id);
        var resultat = new Dictionary<Guid, IReadOnlyList<string>>();
        foreach (var poste in await postes.ListAsync(affilieId, StatutPoste.Actif, cancellationToken))
        {
            var codes = poste.RisquesAu(date)
                .Where(l => referentiel.TryGetValue(l.RisqueId, out var r) && categories.Contains(r.Categorie))
                .Select(l => l.RisqueCode)
                .ToList();
            if (codes.Count > 0)
            {
                resultat[poste.Id] = codes;
            }
        }

        return resultat;
    }

    public async Task<IReadOnlyList<LigneCalculee>> LignesAsync(Guid affilieId, TypeListeNominative type, DateOnly date, CancellationToken cancellationToken)
    {
        var concernes = await PostesConcernesAsync(affilieId, type, date, cancellationToken);
        if (concernes.Count == 0)
        {
            return [];
        }

        var affectations = await projections.ListAffectationsActivesAsync(concernes.Keys, date, cancellationToken);
        var evaluations = await projections.DernieresEvaluationsAsync(
            affilieId, affectations.Select(a => a.PersonneId).Distinct().ToList(), date, cancellationToken);
        return affectations
            .Select(a => new LigneCalculee(
                a.PersonneId,
                a.PosteId,
                concernes[a.PosteId],
                evaluations.TryGetValue(a.PersonneId, out var derniere) ? derniere : null,
                OrigineLigne.Calcul))
            .ToList();
    }

    public async Task<DateOnly?> DerniereEvaluationAsync(Guid affilieId, Guid personneId, DateOnly date, CancellationToken cancellationToken) =>
        (await projections.DernieresEvaluationsAsync(affilieId, [personneId], date, cancellationToken)).TryGetValue(personneId, out var d) ? d : null;
}

/// <summary>AFF-30 : génère une nouvelle version de la liste nominative d'un affilié pour un type.</summary>
public sealed record GenererListeNominative(Guid AffilieId, TypeListeNominative Type, DateOnly DateReference);

public sealed record ListeGenereeDto(Guid Id, int Version, int NombreLignes);

public sealed class GenererListeNominativeHandler(
    CalculListesNominatives calcul,
    IListeNominativeRepository listes,
    PolitiqueConservationListes conservation,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre,
    TimeProvider clock) : ICommandHandler<GenererListeNominative, ListeGenereeDto>
{
    public async Task<Result<ListeGenereeDto>> HandleAsync(GenererListeNominative command, CancellationToken cancellationToken)
    {
        if (Acces.Verifier(currentUser, perimetre, command.AffilieId, Permissions.PosteEcrire, Permissions.RisquePosteValider) is { } refus)
        {
            return refus;
        }

        var lignes = await calcul.LignesAsync(command.AffilieId, command.Type, command.DateReference, cancellationToken);
        var maintenant = clock.GetUtcNow();
        var liste = ListeNominative.Generer(
            command.AffilieId,
            command.Type,
            await listes.DerniereVersionAsync(command.AffilieId, command.Type, cancellationToken) + 1,
            command.DateReference,
            maintenant,
            currentUser.UserId,
            await conservation.AnneesAsync(DateOnly.FromDateTime(maintenant.UtcDateTime), cancellationToken),
            lignes);
        listes.Add(liste);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ListeGenereeDto(liste.Id, liste.Version, liste.Lignes.Count);
    }
}

/// <summary>Historique des versions (AFF-31), sans les lignes.</summary>
public sealed record ListerListesNominatives(Guid AffilieId, TypeListeNominative? Type);

public sealed class ListerListesNominativesHandler(
    IListeNominativeRepository listes,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre) : IQueryHandler<ListerListesNominatives, IReadOnlyList<ListeNominativeDto>>
{
    public async Task<Result<IReadOnlyList<ListeNominativeDto>>> HandleAsync(ListerListesNominatives query, CancellationToken cancellationToken)
    {
        if (Acces.Verifier(currentUser, perimetre, query.AffilieId, Permissions.PosteLire) is { } refus)
        {
            return refus;
        }

        return (await listes.ListAsync(query.AffilieId, query.Type, cancellationToken))
            .OrderBy(l => l.Type)
            .ThenByDescending(l => l.Version)
            .Select(l => ListeNominativeDto.From(l, avecLignes: false))
            .ToList();
    }
}

public sealed record ObtenirListeNominative(Guid Id);

public sealed class ObtenirListeNominativeHandler(
    IListeNominativeRepository listes,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre) : IQueryHandler<ObtenirListeNominative, ListeNominativeDto>
{
    public async Task<Result<ListeNominativeDto>> HandleAsync(ObtenirListeNominative query, CancellationToken cancellationToken)
    {
        var liste = await listes.GetAsync(query.Id, cancellationToken);
        if (liste is null)
        {
            return ListeInconnue(query.Id);
        }

        return Acces.Verifier(currentUser, perimetre, liste.AffilieId, Permissions.PosteLire) is { } refus
            ? refus
            : ListeNominativeDto.From(liste, avecLignes: true);
    }

    internal static Error ListeInconnue(Guid id) => Error.NotFound("liste.inconnue", $"Liste nominative {id} inconnue.");
}

/// <summary>Associe le document (PDF) produit pour une version de liste (§15.3 liste_nominative.document_id).</summary>
public sealed record AssocierDocumentListe(Guid Id, Guid DocumentId);

public sealed class AssocierDocumentListeHandler(
    IListeNominativeRepository listes,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre) : ICommandHandler<AssocierDocumentListe, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AssocierDocumentListe command, CancellationToken cancellationToken)
    {
        var liste = await listes.GetAsync(command.Id, cancellationToken);
        if (liste is null)
        {
            return ObtenirListeNominativeHandler.ListeInconnue(command.Id);
        }

        if (Acces.Verifier(currentUser, perimetre, liste.AffilieId, Permissions.PosteEcrire, Permissions.RisquePosteValider) is { } refus)
        {
            return refus;
        }

        try
        {
            liste.AssocierDocument(command.DocumentId);
        }
        catch (DomainException ex)
        {
            return Error.Validation("liste.invalide", ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
