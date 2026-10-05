using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Personnes.Domain.Personnes;

namespace Sepp.Personnes.Application.Personnes;

/// <summary>
/// Recherche d'une personne existante par index aveugle du NISS, ou création (AFF-21 : détection des doublons).
/// Une personne existante n'est reprise que si son identité concorde (nom et date de naissance).
/// </summary>
public sealed class EnregistrementTravailleurs(IPersonneRepository repository, INissIndex index)
{
    public async Task<Result<(Personne Personne, bool Nouvelle)>> TrouverOuCreerAsync(
        Niss niss, Identite identite, Coordonnees? coordonnees, bool identiteDeReference, CancellationToken cancellationToken)
    {
        var hash = index.Calculer(niss);
        var existante = await repository.GetParNissHashAsync(hash, cancellationToken);
        if (existante is not null)
        {
            if (identiteDeReference)
            {
                // Source authentique (BCSS) : l'identité reçue remplace l'identité connue.
                var maj = Regles.Appliquer("personne.invalide", () =>
                {
                    existante.ModifierIdentite(identite);
                    return existante;
                });
                return maj.IsSuccess ? (existante, false) : maj.Error!;
            }

            if (!string.Equals(existante.Nom, identite.Nom.Trim(), StringComparison.OrdinalIgnoreCase) || existante.DateNaissance != identite.DateNaissance)
            {
                return Error.Conflict("personne.identite-divergente",
                    "Ce NISS est déjà enregistré pour une personne dont le nom ou la date de naissance diffère.");
            }

            return (existante, false);
        }

        return Regles.Appliquer<(Personne, bool)>("personne.invalide",
            () => (Personne.Creer(niss, hash, identite, coordonnees ?? CoordonneesDto.Aucune.ToCoordonnees()), true));
    }
}

/// <summary>
/// AFF-21 : saisie manuelle d'un travailleur. Si le NISS est déjà connu (doublon), la personne existante est reprise
/// et l'occupation lui est ajoutée. Un employeur doit fournir une occupation chez son affilié.
/// </summary>
public sealed record CreerPersonne(string Niss, IdentiteDto Identite, CoordonneesDto? Coordonnees, NouvelleOccupationDto? Occupation)
{
    public override string ToString() => $"CreerPersonne {{ Niss = masqué, Occupation = {Occupation?.AffilieId} }}";
}

public sealed record PersonneEnregistreeDto(Guid PersonneId, bool Nouvelle, Guid? OccupationId);

public sealed class CreerPersonneHandler(
    IPersonneRepository repository,
    EnregistrementTravailleurs enregistrement,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    ICurrentUser currentUser,
    PerimetreUtilisateur perimetre) : ICommandHandler<CreerPersonne, PersonneEnregistreeDto>
{
    public async Task<Result<PersonneEnregistreeDto>> HandleAsync(CreerPersonne command, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.PersonneEcrire))
        {
            return Error.Forbidden("personnes.interdit", "Droits insuffisants pour enregistrer un travailleur.");
        }

        if (perimetre.EstExterne && (command.Occupation is null || !perimetre.PeutAgirPour(command.Occupation.AffilieId)))
        {
            return Error.Forbidden("personnes.hors-perimetre", "Un employeur n'enregistre que des travailleurs occupés chez son affilié.");
        }

        if (!Niss.TryParse(command.Niss, out var niss, out var erreur))
        {
            return Error.Validation("personne.niss-invalide", erreur);
        }

        var trouvee = await enregistrement.TrouverOuCreerAsync(niss, command.Identite.ToIdentite(), command.Coordonnees?.ToCoordonnees(), false, cancellationToken);
        if (!trouvee.IsSuccess)
        {
            return trouvee.Error!;
        }

        var (personne, nouvelle) = trouvee.Value;
        if (!nouvelle && command.Occupation is null)
        {
            return Error.Conflict("personne.existe", "Une personne avec ce NISS existe déjà.");
        }

        Guid? occupationId = null;
        if (command.Occupation is not null)
        {
            var occupation = Regles.Appliquer("occupation.invalide", () => personne.DebuterOccupation(command.Occupation.ToOccupation()));
            if (!occupation.IsSuccess)
            {
                return occupation.Error!;
            }

            occupationId = occupation.Value.Id;
        }

        if (nouvelle)
        {
            repository.Add(personne);
        }

        EvenementsIntegration.Publier(personne, outbox);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new PersonneEnregistreeDto(personne.Id, nouvelle, occupationId);
    }
}

/// <summary>Recherche par NISS : transmis dans le corps d'un POST, jamais dans l'URL (DAT-06).</summary>
public sealed record RechercherParNiss(string Niss)
{
    public override string ToString() => "RechercherParNiss { Niss = masqué }";
}

public sealed class RechercherParNissHandler(
    IPersonneRepository repository,
    INissIndex index,
    ICurrentUser currentUser,
    PerimetreUtilisateur perimetre) : IQueryHandler<RechercherParNiss, PersonneResumeDto>
{
    public async Task<Result<PersonneResumeDto>> HandleAsync(RechercherParNiss query, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.PersonneLire))
        {
            return Error.Forbidden("personnes.interdit", "Droits insuffisants sur les données des travailleurs.");
        }

        if (!Niss.TryParse(query.Niss, out var niss, out var erreur))
        {
            return Error.Validation("personne.niss-invalide", erreur);
        }

        var personne = await repository.GetParNissHashAsync(index.Calculer(niss), cancellationToken);
        if (personne is null || !perimetre.PeutVoir(personne))
        {
            return Error.NotFound("personne.inconnue", "Aucune personne ne correspond à ce NISS.");
        }

        return personne.Resume();
    }
}

public sealed record ObtenirPersonne(Guid PersonneId);

public sealed class ObtenirPersonneHandler(AccesPersonnes acces, PerimetreUtilisateur perimetre) : IQueryHandler<ObtenirPersonne, PersonneDto>
{
    public async Task<Result<PersonneDto>> HandleAsync(ObtenirPersonne query, CancellationToken cancellationToken)
    {
        var personne = await acces.ChargerAsync(query.PersonneId, Permissions.PersonneLire, cancellationToken);
        return personne.IsSuccess ? personne.Value.Fiche(perimetre.OccupationsVisibles(personne.Value)) : personne.Error!;
    }
}

/// <summary>Travailleurs occupés chez un affilié à une date (liste de l'employeur, base des listes nominatives AFF-30).</summary>
public sealed record ListerTravailleurs(Guid AffilieId, DateOnly Date);

public sealed class ListerTravailleursHandler(IPersonneRepository repository, ICurrentUser currentUser, PerimetreUtilisateur perimetre)
    : IQueryHandler<ListerTravailleurs, IReadOnlyList<PersonneResumeDto>>
{
    public async Task<Result<IReadOnlyList<PersonneResumeDto>>> HandleAsync(ListerTravailleurs query, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.PersonneLire))
        {
            return Error.Forbidden("personnes.interdit", "Droits insuffisants sur les données des travailleurs.");
        }

        if (!perimetre.PeutAgirPour(query.AffilieId))
        {
            return Error.Forbidden("personnes.hors-perimetre", "Liste limitée aux travailleurs de votre affilié.");
        }

        var personnes = await repository.ListerParAffilieAsync(query.AffilieId, query.Date, cancellationToken);
        return personnes.OrderBy(p => p.Nom, StringComparer.CurrentCulture).ThenBy(p => p.Prenom, StringComparer.CurrentCulture)
            .Select(p => p.Resume()).ToList();
    }
}

/// <summary>Correction de l'identité : réservée aux internes (l'identité provient de la BCSS).</summary>
public sealed record ModifierIdentite(Guid PersonneId, IdentiteDto Identite);

public sealed class ModifierIdentiteHandler(AccesPersonnes acces, PerimetreUtilisateur perimetre, IUnitOfWork unitOfWork)
    : ICommandHandler<ModifierIdentite, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ModifierIdentite command, CancellationToken cancellationToken)
    {
        if (perimetre.EstExterne)
        {
            return Error.Forbidden("personnes.interdit", "L'identité d'un travailleur est corrigée par le SEPP.");
        }

        var personne = await acces.ChargerAsync(command.PersonneId, Permissions.PersonneEcrire, cancellationToken);
        if (!personne.IsSuccess)
        {
            return personne.Error!;
        }

        var resultat = Regles.Appliquer("personne.invalide", () =>
        {
            personne.Value.ModifierIdentite(command.Identite.ToIdentite());
            return Unit.Value;
        });
        if (resultat.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return resultat;
    }
}

public sealed record ModifierCoordonnees(Guid PersonneId, CoordonneesDto Coordonnees);

public sealed class ModifierCoordonneesHandler(AccesPersonnes acces, IUnitOfWork unitOfWork) : ICommandHandler<ModifierCoordonnees, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ModifierCoordonnees command, CancellationToken cancellationToken)
    {
        var personne = await acces.ChargerAsync(command.PersonneId, Permissions.PersonneEcrire, cancellationToken);
        if (!personne.IsSuccess)
        {
            return personne.Error!;
        }

        var resultat = Regles.Appliquer("personne.invalide", () =>
        {
            personne.Value.ModifierCoordonnees(command.Coordonnees.ToCoordonnees());
            return Unit.Value;
        });
        if (resultat.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return resultat;
    }
}
