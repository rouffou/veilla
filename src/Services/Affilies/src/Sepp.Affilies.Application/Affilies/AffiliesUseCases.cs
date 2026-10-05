using Sepp.Affilies.Application.Securite;
using Sepp.Affilies.Domain.Affilies;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Affilies;

namespace Sepp.Affilies.Application.Affilies;

/// <summary>Données saisies de la fiche employeur (AFF-01).</summary>
public sealed record FicheSaisie(
    string Denomination,
    string FormeJuridique,
    string CodeNace,
    string CommissionParitaire,
    CategorieTarifaire CategorieTarifaire,
    Language Langue,
    RegimeLinguistique RegimeLinguistique)
{
    public DonneesFiche ToDonnees() => new(Denomination, FormeJuridique, CodeNace, CommissionParitaire, CategorieTarifaire, Langue, RegimeLinguistique);
}

/// <summary>Affiliation d'un employeur (gestionnaire de dossiers). <c>SeppOrigine</c> : transfert entrant (AFF-06).</summary>
public sealed record CreerAffilie(string NumeroBce, FicheSaisie Fiche, DateOnly DateAffiliation, Guid? GroupeId, string? SeppOrigine);

public sealed class CreerAffilieHandler(
    IAffilieRepository affilies,
    IGroupeRepository groupes,
    ControleAcces acces,
    ModificateurAffilie modificateur,
    IUnitOfWork unitOfWork) : ICommandHandler<CreerAffilie, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreerAffilie command, CancellationToken cancellationToken)
    {
        if (acces.VerifierGestion() is { } refus)
        {
            return refus;
        }

        Affilie affilie;
        try
        {
            var numero = new NumeroBce(command.NumeroBce);
            if (await affilies.BceUtiliseAsync(numero, cancellationToken))
            {
                return Error.Conflict("affilie.bce-existe", $"Un affilié porte déjà le numéro BCE {numero}.");
            }

            if (command.GroupeId is { } groupeId && await groupes.GetAsync(groupeId, cancellationToken) is null)
            {
                return Error.Validation("groupe.inconnu", $"Groupe {groupeId} inconnu.");
            }

            affilie = Affilie.Creer(numero, command.Fiche.ToDonnees(), command.DateAffiliation, command.GroupeId, command.SeppOrigine);
        }
        catch (DomainException ex)
        {
            return Error.Validation("affilie.invalide", ex.Message);
        }

        affilies.Add(affilie);
        modificateur.JournaliserCreation(affilie);
        modificateur.Publier(new AffilieCree(affilie.Id, affilie.NumeroBce.Value, affilie.CategorieTarifaire.ToString()));
        foreach (var operation in affilie.Operations)
        {
            modificateur.Publier(ModificateurAffilie.OperationModifiee(affilie, operation));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return affilie.Id;
    }
}

/// <summary>Modification de la fiche et du groupe (gestionnaire de dossiers).</summary>
public sealed record ModifierFiche(Guid AffilieId, FicheSaisie Fiche, Guid? GroupeId);

public sealed class ModifierFicheHandler(ModificateurAffilie modificateur, IGroupeRepository groupes) : ICommandHandler<ModifierFiche, Unit>
{
    public Task<Result<Unit>> HandleAsync(ModifierFiche command, CancellationToken cancellationToken) =>
        modificateur.ModifierEtVerifierAsync(command.AffilieId, PartieFiche.Fiche, "fiche.modifiee", async affilie =>
        {
            if (command.GroupeId is { } groupeId && await groupes.GetAsync(groupeId, cancellationToken) is null)
            {
                return Error.Validation("groupe.inconnu", $"Groupe {groupeId} inconnu.");
            }

            affilie.ModifierFiche(command.Fiche.ToDonnees(), command.GroupeId);
            return Result<Unit>.Success(Unit.Value);
        }, cancellationToken);
}

/// <summary>Résiliation de l'affiliation ; <c>DateFin</c> est le dernier jour couvert.</summary>
public sealed record ResilierAffiliation(Guid AffilieId, DateOnly DateFin);

public sealed class ResilierAffiliationHandler(ModificateurAffilie modificateur) : ICommandHandler<ResilierAffiliation, Unit>
{
    public Task<Result<Unit>> HandleAsync(ResilierAffiliation command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Fiche, "affiliation.resiliee", affilie =>
        {
            affilie.Resilier(command.DateFin);
            return Unit.Value;
        }, cancellationToken);
}

public sealed record AnnulerResiliation(Guid AffilieId);

public sealed class AnnulerResiliationHandler(ModificateurAffilie modificateur) : ICommandHandler<AnnulerResiliation, Unit>
{
    public Task<Result<Unit>> HandleAsync(AnnulerResiliation command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Fiche, "resiliation.annulee", affilie =>
        {
            affilie.AnnulerResiliation();
            return Unit.Value;
        }, cancellationToken);
}

public sealed record ObtenirAffilie(Guid AffilieId);

public sealed class ObtenirAffilieHandler(IAffilieRepository affilies, ControleAcces acces) : IQueryHandler<ObtenirAffilie, AffilieDto>
{
    public async Task<Result<AffilieDto>> HandleAsync(ObtenirAffilie query, CancellationToken cancellationToken)
    {
        if (acces.VerifierLecture(query.AffilieId) is { } refus)
        {
            return refus;
        }

        var affilie = await affilies.GetAsync(query.AffilieId, cancellationToken);
        return affilie is null ? Error.NotFound("affilie.inconnu", $"Affilié {query.AffilieId} inconnu.") : affilie.ToDto();
    }
}

/// <summary>Recherche par numéro BCE (tous formats : 0123.456.749, BE0123456749, 123456749).</summary>
public sealed record ObtenirAffilieParBce(string NumeroBce);

public sealed class ObtenirAffilieParBceHandler(IAffilieRepository affilies, ControleAcces acces) : IQueryHandler<ObtenirAffilieParBce, AffilieDto>
{
    public async Task<Result<AffilieDto>> HandleAsync(ObtenirAffilieParBce query, CancellationToken cancellationToken)
    {
        if (!NumeroBce.TryParse(query.NumeroBce, out var numero))
        {
            return Error.Validation("affilie.bce-invalide", $"Numéro BCE invalide : '{query.NumeroBce}'.");
        }

        var affilie = await affilies.GetParBceAsync(numero!, cancellationToken);
        if (affilie is null)
        {
            return acces.PerimetreLecture is null
                ? Error.NotFound("affilie.inconnu", $"Aucun affilié ne porte le numéro BCE {numero}.")
                : Error.Forbidden("affilie.hors-perimetre", "Cet affilié n'est pas dans votre périmètre.");
        }

        return acces.VerifierLecture(affilie.Id) is { } refus ? refus : affilie.ToDto();
    }
}

/// <summary>Recherche paginée par numéro BCE et / ou dénomination (contient, insensible à la casse).</summary>
public sealed record RechercherAffilies(string? NumeroBce, string? Denomination, int Page = 1, int Taille = 20);

public sealed class RechercherAffiliesHandler(IAffilieRepository affilies, ControleAcces acces) : IQueryHandler<RechercherAffilies, PageDto<AffilieResumeDto>>
{
    public const int TailleMaximale = 100;

    public async Task<Result<PageDto<AffilieResumeDto>>> HandleAsync(RechercherAffilies query, CancellationToken cancellationToken)
    {
        if (acces.VerifierPermissionLecture() is { } refus)
        {
            return refus;
        }

        if (query.Page < 1 || query.Taille is < 1 or > TailleMaximale)
        {
            return Error.Validation("recherche.pagination", $"Page à partir de 1, taille de 1 à {TailleMaximale}.");
        }

        NumeroBce? numero = null;
        if (!string.IsNullOrWhiteSpace(query.NumeroBce) && !NumeroBce.TryParse(query.NumeroBce, out numero))
        {
            return Error.Validation("affilie.bce-invalide", $"Numéro BCE invalide : '{query.NumeroBce}'.");
        }

        var denomination = string.IsNullOrWhiteSpace(query.Denomination) ? null : query.Denomination.Trim();
        return await affilies.RechercherAsync(new CriteresRecherche(numero, denomination, acces.PerimetreLecture, query.Page, query.Taille), cancellationToken);
    }
}
