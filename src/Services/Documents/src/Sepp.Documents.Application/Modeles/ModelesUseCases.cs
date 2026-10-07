using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Application.Securite;
using Sepp.Documents.Domain.Commun;
using Sepp.Documents.Domain.Fusion;
using Sepp.Documents.Domain.Modeles;

namespace Sepp.Documents.Application.Modeles;

public sealed record ChampDto(string Nom, TypeChamp Type, bool Obligatoire, string Libelle)
{
    public ChampDeclare ToChamp() => new(Nom, Type, Obligatoire, Libelle);
}

public sealed record ModeleDto(
    Guid Id,
    string Code,
    Language Langue,
    int Version,
    TypeModele Type,
    string Zone,
    string Libelle,
    string? Description,
    string Contenu,
    StatutModele Statut,
    IReadOnlyList<ChampDto> Champs,
    string? ValidePar,
    DateTimeOffset? ValideLe,
    string? PubliePar,
    DateTimeOffset? PublieLe)
{
    public static ModeleDto From(Modele m) => new(
        m.Id, m.Code, m.Langue, m.Version, m.Type, m.Zone.Code(), m.Libelle, m.Description, m.Contenu, m.Statut,
        m.Champs.Select(c => new ChampDto(c.Nom, c.TypeChamp, c.Obligatoire, c.Libelle)).ToList(),
        m.ValidePar, m.ValideLe, m.PubliePar, m.PublieLe);
}

public sealed record ModeleResumeDto(Guid Id, string Code, Language Langue, int Version, TypeModele Type, string Zone, string Libelle, StatutModele Statut);

public sealed record ListerModeles(string? Code);

public sealed class ListerModelesHandler(IModeleRepository repository, ICurrentUser currentUser) : IQueryHandler<ListerModeles, IReadOnlyList<ModeleResumeDto>>
{
    public async Task<Result<IReadOnlyList<ModeleResumeDto>>> HandleAsync(ListerModeles query, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.DocumentsModeleLire))
        {
            return Error.Forbidden("documents.interdit", "Droits insuffisants sur les modèles.");
        }

        var modeles = await repository.ListAsync(string.IsNullOrWhiteSpace(query.Code) ? null : query.Code.Trim().ToUpperInvariant(), cancellationToken);
        return modeles.OrderBy(m => m.Code, StringComparer.Ordinal).ThenBy(m => m.Langue).ThenByDescending(m => m.Version)
            .Select(m => new ModeleResumeDto(m.Id, m.Code, m.Langue, m.Version, m.Type, m.Zone.Code(), m.Libelle, m.Statut))
            .ToList();
    }
}

public sealed record ObtenirModele(Guid Id);

public sealed class ObtenirModeleHandler(IModeleRepository repository, ICurrentUser currentUser) : IQueryHandler<ObtenirModele, ModeleDto>
{
    public async Task<Result<ModeleDto>> HandleAsync(ObtenirModele query, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.DocumentsModeleLire))
        {
            return Error.Forbidden("documents.interdit", "Droits insuffisants sur les modèles.");
        }

        var modele = await repository.GetAsync(query.Id, cancellationToken);
        return modele is null ? Error.NotFound("modele.inconnu", "Modèle inconnu.") : ModeleDto.From(modele);
    }
}

/// <summary>DOC-01 : création de la version 1 d'un modèle dans une langue (un même code existe dans plusieurs langues).</summary>
public sealed record CreerModele(
    string Code,
    Language Langue,
    TypeModele Type,
    string Zone,
    string Libelle,
    string? Description,
    string Contenu,
    IReadOnlyList<ChampDto> Champs);

public sealed class CreerModeleHandler(IModeleRepository repository, IUnitOfWork unitOfWork, ICurrentUser currentUser) : ICommandHandler<CreerModele, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreerModele command, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.DocumentsModeleGerer))
        {
            return Error.Forbidden("documents.interdit", "La gestion des modèles est réservée à l'administrateur fonctionnel.");
        }

        Modele modele;
        try
        {
            var code = Modele.NormaliserCode(command.Code);
            var zone = Zones.Depuis(command.Zone);
            var existants = await repository.ListAsync(code, cancellationToken);
            if (existants.Any(m => m.Langue == command.Langue))
            {
                return Error.Conflict("modele.existe", $"Le modèle {code} existe déjà en {command.Langue} : créez une nouvelle version.");
            }

            if (existants.Count > 0 && existants[0] is var autre && (autre.Zone != zone || autre.Type != command.Type))
            {
                return Error.Validation("modele.incoherent", $"Le modèle {code} existe dans une autre langue avec la zone {autre.Zone.Code()} et le type {autre.Type}.");
            }

            modele = Modele.Creer(code, command.Langue, command.Type, zone, command.Libelle, command.Description, command.Contenu,
                (command.Champs ?? []).Select(c => c.ToChamp()));
        }
        catch (DomainException ex)
        {
            return Error.Validation("modele.invalide", ex.Message);
        }

        repository.Add(modele);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return modele.Id;
    }
}

public sealed record ModifierModele(Guid Id, string Libelle, string? Description, string Contenu, IReadOnlyList<ChampDto> Champs);

public sealed class ModifierModeleHandler(IModeleRepository repository, IUnitOfWork unitOfWork, ICurrentUser currentUser) : ICommandHandler<ModifierModele, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ModifierModele command, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.DocumentsModeleGerer))
        {
            return Error.Forbidden("documents.interdit", "La gestion des modèles est réservée à l'administrateur fonctionnel.");
        }

        var modele = await repository.GetAsync(command.Id, cancellationToken);
        if (modele is null)
        {
            return Error.NotFound("modele.inconnu", "Modèle inconnu.");
        }

        try
        {
            modele.ModifierBrouillon(command.Libelle, command.Description, command.Contenu, (command.Champs ?? []).Select(c => c.ToChamp()));
        }
        catch (DomainException ex)
        {
            return Error.Validation("modele.invalide", ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Nouvelle version en brouillon d'un modèle (à partir de la plus récente de sa langue).</summary>
public sealed record CreerNouvelleVersion(Guid Id);

public sealed class CreerNouvelleVersionHandler(IModeleRepository repository, IUnitOfWork unitOfWork, ICurrentUser currentUser) : ICommandHandler<CreerNouvelleVersion, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreerNouvelleVersion command, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.DocumentsModeleGerer))
        {
            return Error.Forbidden("documents.interdit", "La gestion des modèles est réservée à l'administrateur fonctionnel.");
        }

        var modele = await repository.GetAsync(command.Id, cancellationToken);
        if (modele is null)
        {
            return Error.NotFound("modele.inconnu", "Modèle inconnu.");
        }

        var versions = (await repository.ListAsync(modele.Code, cancellationToken)).Where(m => m.Langue == modele.Langue).ToList();
        if (versions.Any(m => m.Statut is StatutModele.Brouillon or StatutModele.Valide))
        {
            return Error.Conflict("modele.version-en-cours", $"Une version du modèle {modele.Code} en {modele.Langue} est déjà en cours de rédaction ou de validation.");
        }

        var copie = modele.NouvelleVersion(versions.Max(m => m.Version) + 1);
        repository.Add(copie);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return copie.Id;
    }
}

public sealed record ValiderModele(Guid Id);

/// <summary>DOC-01 : validation avant publication, par le valideur de la zone du modèle.</summary>
public sealed class ValiderModeleHandler(IModeleRepository repository, IUnitOfWork unitOfWork, ICurrentUser currentUser, AccesDocuments acces, TimeProvider horloge)
    : ICommandHandler<ValiderModele, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ValiderModele command, CancellationToken cancellationToken)
    {
        var modele = await repository.GetAsync(command.Id, cancellationToken);
        if (modele is null)
        {
            return currentUser.HasPermission(Permissions.DocumentsModeleLire) ? Error.NotFound("modele.inconnu", "Modèle inconnu.") : Error.Forbidden("documents.interdit", "Droits insuffisants.");
        }

        if (acces.VerifierValidationModele(modele.Zone) is { } refus)
        {
            return refus;
        }

        try
        {
            modele.Valider(currentUser.UserId, horloge.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Error.Conflict("modele.statut", ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record RenvoyerModeleEnBrouillon(Guid Id);

public sealed class RenvoyerModeleEnBrouillonHandler(IModeleRepository repository, IUnitOfWork unitOfWork, AccesDocuments acces, ICurrentUser currentUser)
    : ICommandHandler<RenvoyerModeleEnBrouillon, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RenvoyerModeleEnBrouillon command, CancellationToken cancellationToken)
    {
        var modele = await repository.GetAsync(command.Id, cancellationToken);
        if (modele is null)
        {
            return Error.NotFound("modele.inconnu", "Modèle inconnu.");
        }

        if (acces.VerifierValidationModele(modele.Zone) is { } refus && !currentUser.HasPermission(Permissions.DocumentsModeleGerer))
        {
            return refus;
        }

        try
        {
            modele.RenvoyerEnBrouillon();
        }
        catch (DomainException ex)
        {
            return Error.Conflict("modele.statut", ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record PublierModele(Guid Id);

/// <summary>Publication d'une version validée ; la version précédemment publiée dans la même langue est retirée.</summary>
public sealed class PublierModeleHandler(IModeleRepository repository, IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider horloge)
    : ICommandHandler<PublierModele, Unit>
{
    public async Task<Result<Unit>> HandleAsync(PublierModele command, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.DocumentsModeleGerer))
        {
            return Error.Forbidden("documents.interdit", "La publication des modèles est réservée à l'administrateur fonctionnel.");
        }

        var modele = await repository.GetAsync(command.Id, cancellationToken);
        if (modele is null)
        {
            return Error.NotFound("modele.inconnu", "Modèle inconnu.");
        }

        try
        {
            if (modele.Statut == StatutModele.Valide && await repository.PublieAsync(modele.Code, modele.Langue, cancellationToken) is { } precedent && precedent.Id != modele.Id)
            {
                precedent.Retirer();
            }

            modele.Publier(currentUser.UserId, horloge.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Error.Conflict("modele.statut", ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Aperçu de la fusion d'un modèle (toute version) avec des valeurs d'essai, sans rien archiver.</summary>
public sealed record ApercuModele(Guid Id, IReadOnlyDictionary<string, ValeurChamp> Valeurs);

public sealed record BlocDto(TypeBloc Type, string Texte);

public sealed class ApercuModeleHandler(IModeleRepository repository, ICurrentUser currentUser) : IQueryHandler<ApercuModele, IReadOnlyList<BlocDto>>
{
    public async Task<Result<IReadOnlyList<BlocDto>>> HandleAsync(ApercuModele query, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.DocumentsModeleLire))
        {
            return Error.Forbidden("documents.interdit", "Droits insuffisants sur les modèles.");
        }

        var modele = await repository.GetAsync(query.Id, cancellationToken);
        if (modele is null)
        {
            return Error.NotFound("modele.inconnu", "Modèle inconnu.");
        }

        try
        {
            return modele.Fusionner(query.Valeurs).Blocs.Select(b => new BlocDto(b.Type, b.Texte)).ToList();
        }
        catch (DomainException ex)
        {
            return Error.Validation("fusion.invalide", ex.Message);
        }
    }
}
