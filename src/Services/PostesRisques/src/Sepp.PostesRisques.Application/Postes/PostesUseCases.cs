using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.PostesRisques.Application.Risques;
using Sepp.PostesRisques.Domain.Postes;
using Sepp.PostesRisques.Domain.Risques;

namespace Sepp.PostesRisques.Application.Postes;

public sealed record LienRisqueDto(
    Guid RisqueId,
    string RisqueCode,
    NiveauExposition NiveauExposition,
    string ValideParCpmtId,
    DateOnly DateAvisCppt,
    Guid DocumentAvisCpptId,
    Guid PropositionId,
    DateOnly ValideDu,
    DateOnly? ValideJusquAu)
{
    public static LienRisqueDto From(PosteRisque r) =>
        new(r.RisqueId, r.RisqueCode, r.NiveauExposition, r.ValideParCpmtId, r.DateAvisCppt, r.DocumentAvisCpptId, r.PropositionId,
            r.Validite.ValidFrom, r.Validite.ValidTo);
}

public sealed record PosteDto(
    Guid Id,
    Guid AffilieId,
    string Intitule,
    string? Description,
    string? MetierTypeCode,
    StatutPoste Statut,
    IReadOnlyList<LienRisqueDto> Risques)
{
    public static PosteDto From(Poste p, IEnumerable<PosteRisque> risques) =>
        new(p.Id, p.AffilieId, p.Intitule, p.Description, p.MetierTypeCode, p.Statut, risques.Select(LienRisqueDto.From).ToList());
}

/// <summary>AFF-10 : catalogue des postes d'un affilié, avec leurs risques en vigueur à une date.</summary>
public sealed record ListerPostes(Guid AffilieId, StatutPoste? Statut, DateOnly Date);

public sealed class ListerPostesHandler(IPosteRepository repository, ICurrentUser currentUser, IPerimetreAffilies perimetre)
    : IQueryHandler<ListerPostes, IReadOnlyList<PosteDto>>
{
    public async Task<Result<IReadOnlyList<PosteDto>>> HandleAsync(ListerPostes query, CancellationToken cancellationToken)
    {
        if (Acces.Verifier(currentUser, perimetre, query.AffilieId, Permissions.PosteLire) is { } refus)
        {
            return refus;
        }

        var postes = await repository.ListAsync(query.AffilieId, query.Statut, cancellationToken);
        return postes.OrderBy(p => p.Intitule, StringComparer.CurrentCulture)
            .Select(p => PosteDto.From(p, p.RisquesAu(query.Date)))
            .ToList();
    }
}

/// <summary>Un poste avec l'historique complet de ses liens aux risques (DAT-04).</summary>
public sealed record ObtenirPoste(Guid Id);

public sealed class ObtenirPosteHandler(IPosteRepository repository, ICurrentUser currentUser, IPerimetreAffilies perimetre)
    : IQueryHandler<ObtenirPoste, PosteDto>
{
    public async Task<Result<PosteDto>> HandleAsync(ObtenirPoste query, CancellationToken cancellationToken)
    {
        var poste = await repository.GetAsync(query.Id, cancellationToken);
        if (poste is null)
        {
            return Erreurs.PosteInconnu(query.Id);
        }

        if (Acces.Verifier(currentUser, perimetre, poste.AffilieId, Permissions.PosteLire) is { } refus)
        {
            return refus;
        }

        return PosteDto.From(poste, poste.Risques.OrderBy(r => r.RisqueCode, StringComparer.Ordinal).ThenBy(r => r.Validite.ValidFrom));
    }
}

public sealed record RisqueProfilDto(
    string Code,
    CategorieRisque Categorie,
    string Libelle,
    NiveauExposition NiveauExposition,
    DateOnly ExposeDepuis,
    RegleDto? Regle);

public sealed record ProfilRisquesDto(Guid PosteId, Guid AffilieId, DateOnly Date, IReadOnlyList<RisqueProfilDto> Risques);

/// <summary>AFF-11/AFF-12 : risques du poste à une date et règles de surveillance qu'ils portent.</summary>
public sealed record ObtenirProfilRisques(Guid PosteId, DateOnly Date, Language Langue);

public sealed class ObtenirProfilRisquesHandler(
    IPosteRepository postes,
    IRisqueRepository risques,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre) : IQueryHandler<ObtenirProfilRisques, ProfilRisquesDto>
{
    public async Task<Result<ProfilRisquesDto>> HandleAsync(ObtenirProfilRisques query, CancellationToken cancellationToken)
    {
        var poste = await postes.GetAsync(query.PosteId, cancellationToken);
        if (poste is null)
        {
            return Erreurs.PosteInconnu(query.PosteId);
        }

        if (Acces.Verifier(currentUser, perimetre, poste.AffilieId, Permissions.PosteLire) is { } refus)
        {
            return refus;
        }

        var referentiel = (await risques.ListAsync(cancellationToken)).ToDictionary(r => r.Id);
        var profil = poste.RisquesAu(query.Date)
            .Where(l => referentiel.ContainsKey(l.RisqueId))
            .Select(l =>
            {
                var risque = referentiel[l.RisqueId];
                return new RisqueProfilDto(risque.Code, risque.Categorie, risque.Libelle.In(query.Langue), l.NiveauExposition, l.Validite.ValidFrom,
                    risque.RegleAu(query.Date) is { } regle ? RegleDto.From(regle) : null);
            })
            .ToList();
        return new ProfilRisquesDto(poste.Id, poste.AffilieId, query.Date, profil);
    }
}

public sealed record CreerPoste(Guid AffilieId, string Intitule, string? Description, string? MetierTypeCode);

public sealed class CreerPosteHandler(
    IPosteRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre) : ICommandHandler<CreerPoste, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreerPoste command, CancellationToken cancellationToken)
    {
        if (Acces.Verifier(currentUser, perimetre, command.AffilieId, Permissions.PosteEcrire) is { } refus)
        {
            return refus;
        }

        Poste poste;
        try
        {
            poste = Poste.Creer(command.AffilieId, command.Intitule, command.Description, command.MetierTypeCode);
        }
        catch (DomainException ex)
        {
            return Error.Validation("poste.invalide", ex.Message);
        }

        repository.Add(poste);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return poste.Id;
    }
}

public sealed record ModifierPoste(Guid Id, string Intitule, string? Description, string? MetierTypeCode);

public sealed class ModifierPosteHandler(
    IPosteRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre) : ICommandHandler<ModifierPoste, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ModifierPoste command, CancellationToken cancellationToken) =>
        await Erreurs.SurPoste(repository, unitOfWork, currentUser, perimetre, command.Id,
            p => p.Modifier(command.Intitule, command.Description, command.MetierTypeCode), cancellationToken);
}

/// <summary>Archive un poste qui n'est plus exposé à aucun risque (AFF-14 : le retrait des risques passe par le CPMT).</summary>
public sealed record ArchiverPoste(Guid Id, DateOnly Date);

public sealed class ArchiverPosteHandler(
    IPosteRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre) : ICommandHandler<ArchiverPoste, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ArchiverPoste command, CancellationToken cancellationToken) =>
        await Erreurs.SurPoste(repository, unitOfWork, currentUser, perimetre, command.Id, p => p.Archiver(command.Date), cancellationToken);
}

public sealed record ReactiverPoste(Guid Id);

public sealed class ReactiverPosteHandler(
    IPosteRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IPerimetreAffilies perimetre) : ICommandHandler<ReactiverPoste, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ReactiverPoste command, CancellationToken cancellationToken) =>
        await Erreurs.SurPoste(repository, unitOfWork, currentUser, perimetre, command.Id, p => p.Reactiver(), cancellationToken);
}

internal static class Erreurs
{
    public static Error PosteInconnu(Guid id) => Error.NotFound("poste.inconnu", $"Poste {id} inconnu.");

    /// <summary>Charge un poste, vérifie l'écriture dans le périmètre, applique la modification et valide la transaction.</summary>
    public static async Task<Result<Unit>> SurPoste(
        IPosteRepository repository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IPerimetreAffilies perimetre,
        Guid id,
        Action<Poste> modification,
        CancellationToken cancellationToken)
    {
        var poste = await repository.GetAsync(id, cancellationToken);
        if (poste is null)
        {
            return PosteInconnu(id);
        }

        if (Acces.Verifier(currentUser, perimetre, poste.AffilieId, Permissions.PosteEcrire) is { } refus)
        {
            return refus;
        }

        try
        {
            modification(poste);
        }
        catch (DomainException ex)
        {
            return Error.Validation("poste.invalide", ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
