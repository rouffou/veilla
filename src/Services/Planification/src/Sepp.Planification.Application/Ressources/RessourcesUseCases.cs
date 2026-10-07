using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Planification.Domain;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Ressources;

namespace Sepp.Planification.Application.Ressources;

public sealed record LieuDto(
    Guid Id,
    TypeLieu Type,
    string Nom,
    string? Adresse,
    string? CodePostal,
    double? Latitude,
    double? Longitude,
    Guid? AffilieId,
    Guid? SiteId,
    bool Actif);

public sealed record RessourceDto(
    Guid Id,
    TypeRessource Type,
    string Libelle,
    string? ReferenceId,
    IReadOnlyList<string> Competences,
    Guid? LieuId,
    bool Active,
    FournisseurAgenda? FournisseurAgenda,
    string? CompteAgenda);

public sealed record AbsenceDto(Guid Id, Guid RessourceId, DateTimeOffset Debut, DateTimeOffset Fin, SourceAbsence Source, string? ReferenceExterne);

internal static class RessourcesMapping
{
    public static LieuDto ToDto(this Lieu l) => new(l.Id, l.Type, l.Nom, l.Adresse, l.CodePostal, l.Latitude, l.Longitude, l.AffilieId, l.SiteId, l.Actif);

    public static RessourceDto ToDto(this Ressource r) =>
        new(r.Id, r.Type, r.Libelle, r.ReferenceId, r.Competences, r.LieuId, r.Active, r.FournisseurAgenda, r.CompteAgenda);

    public static AbsenceDto ToDto(this Absence a) => new(a.Id, a.RessourceId, a.Debut, a.Fin, a.Source, a.ReferenceExterne);
}

/// <summary>PLA-02 : déclare un lieu de prestation.</summary>
public sealed record CreerLieu(string Type, string Nom, string? Adresse, string? CodePostal, double? Latitude, double? Longitude, Guid? AffilieId, Guid? SiteId);

public sealed class CreerLieuHandler(ILieuRepository lieux, IUnitOfWork unitOfWork, ICurrentUser user) : ICommandHandler<CreerLieu, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreerLieu command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationRessources) is { } interdit)
        {
            return interdit;
        }

        if (!Enumerations.TryParse<TypeLieu>(command.Type, out var type))
        {
            return Error.Validation("lieu.type-inconnu", $"Type de lieu inconnu : '{command.Type}'.");
        }

        if ((command.Latitude is null) != (command.Longitude is null))
        {
            return Error.Validation("lieu.position-incomplete", "Indiquez la latitude et la longitude, ou aucune des deux.");
        }

        var lieu = Domaine.Essayer(() => Lieu.Creer(type, command.Nom, command.Adresse, command.CodePostal,
            command.Latitude is { } lat ? new Coordonnees(lat, command.Longitude!.Value) : null, command.AffilieId, command.SiteId));
        if (!lieu.IsSuccess)
        {
            return lieu.Error!;
        }

        lieux.Add(lieu.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return lieu.Value.Id;
    }
}

public sealed record ListerLieux;

public sealed class ListerLieuxHandler(ILieuRepository lieux, ICurrentUser user) : IQueryHandler<ListerLieux, IReadOnlyList<LieuDto>>
{
    public async Task<Result<IReadOnlyList<LieuDto>>> HandleAsync(ListerLieux query, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationLire) is { } interdit)
        {
            return interdit;
        }

        return (await lieux.ListAsync(cancellationToken)).OrderBy(l => l.Nom, StringComparer.Ordinal).Select(l => l.ToDto()).ToList();
    }
}

/// <summary>PLA-01 : déclare une ressource planifiable et ses compétences.</summary>
public sealed record CreerRessource(string Type, string Libelle, string? ReferenceId, IReadOnlyList<string>? Competences, Guid? LieuId);

public sealed class CreerRessourceHandler(IRessourceRepository ressources, ILieuRepository lieux, IUnitOfWork unitOfWork, ICurrentUser user)
    : ICommandHandler<CreerRessource, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreerRessource command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationRessources) is { } interdit)
        {
            return interdit;
        }

        if (!Enumerations.TryParse<TypeRessource>(command.Type, out var type))
        {
            return Error.Validation("ressource.type-inconnu", $"Type de ressource inconnu : '{command.Type}'.");
        }

        if (command.LieuId is { } lieuId && await lieux.GetAsync(lieuId, cancellationToken) is null)
        {
            return Error.Validation("lieu.inconnu", "Lieu de rattachement inconnu.");
        }

        var ressource = Domaine.Essayer(() => Ressource.Creer(type, command.Libelle, command.ReferenceId, command.Competences, command.LieuId));
        if (!ressource.IsSuccess)
        {
            return ressource.Error!;
        }

        ressources.Add(ressource.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ressource.Value.Id;
    }
}

public sealed record ListerRessources(string? Type);

public sealed class ListerRessourcesHandler(IRessourceRepository ressources, ICurrentUser user) : IQueryHandler<ListerRessources, IReadOnlyList<RessourceDto>>
{
    public async Task<Result<IReadOnlyList<RessourceDto>>> HandleAsync(ListerRessources query, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationLire) is { } interdit)
        {
            return interdit;
        }

        var type = Enumerations.Optionnel<TypeRessource>(query.Type, "Type de ressource");
        if (!type.IsSuccess)
        {
            return type.Error!;
        }

        return (await ressources.ListAsync(type.Value, cancellationToken)).OrderBy(r => r.Libelle, StringComparer.Ordinal).Select(r => r.ToDto()).ToList();
    }
}

public sealed record ObtenirRessource(Guid Id);

public sealed class ObtenirRessourceHandler(IRessourceRepository ressources, ICurrentUser user) : IQueryHandler<ObtenirRessource, RessourceDto>
{
    public async Task<Result<RessourceDto>> HandleAsync(ObtenirRessource query, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationLire) is { } interdit)
        {
            return interdit;
        }

        var ressource = await ressources.GetAsync(query.Id, cancellationToken);
        return ressource is null ? Error.NotFound("ressource.inconnue", "Ressource inconnue.") : ressource.ToDto();
    }
}

public sealed record DefinirCompetences(Guid RessourceId, IReadOnlyList<string> Competences);

public sealed class DefinirCompetencesHandler(IRessourceRepository ressources, IUnitOfWork unitOfWork, ICurrentUser user)
    : ICommandHandler<DefinirCompetences, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DefinirCompetences command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationRessources) is { } interdit)
        {
            return interdit;
        }

        var ressource = await ressources.GetAsync(command.RessourceId, cancellationToken);
        if (ressource is null)
        {
            return Error.NotFound("ressource.inconnue", "Ressource inconnue.");
        }

        if (Domaine.Essayer(() => ressource.DefinirCompetences(command.Competences ?? [])) is { } erreur)
        {
            return erreur;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>PLA-09 : rattache l'agenda Microsoft 365 / Google d'une ressource humaine.</summary>
public sealed record RattacherAgendaExterne(Guid RessourceId, string Fournisseur, string Compte);

public sealed class RattacherAgendaExterneHandler(IRessourceRepository ressources, IUnitOfWork unitOfWork, ICurrentUser user)
    : ICommandHandler<RattacherAgendaExterne, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RattacherAgendaExterne command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationRessources) is { } interdit)
        {
            return interdit;
        }

        if (!Enumerations.TryParse<FournisseurAgenda>(command.Fournisseur, out var fournisseur))
        {
            return Error.Validation("agenda.fournisseur-inconnu", $"Fournisseur d'agenda inconnu : '{command.Fournisseur}'.");
        }

        var ressource = await ressources.GetAsync(command.RessourceId, cancellationToken);
        if (ressource is null)
        {
            return Error.NotFound("ressource.inconnue", "Ressource inconnue.");
        }

        if (Domaine.Essayer(() => ressource.RattacherAgendaExterne(fournisseur, command.Compte)) is { } erreur)
        {
            return erreur;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Indisponibilités : blocage des créneaux libres d'une ressource sur une période.</summary>
public sealed class Indisponibilites(ICreneauRepository creneaux)
{
    /// <summary>Bloque les créneaux libres qui mobilisent la ressource sur [debut, fin[ ; renvoie les créneaux réservés en conflit.</summary>
    public async Task<(int Bloques, IReadOnlyList<Creneau> Reserves)> BloquerAsync(Guid ressourceId, DateTimeOffset debut, DateTimeOffset fin,
        CancellationToken cancellationToken)
    {
        var concernes = await creneaux.ChevauchantsAsync([ressourceId], debut, fin, cancellationToken);
        var bloques = 0;
        foreach (var creneau in concernes.Where(c => c.Statut == StatutCreneau.Libre))
        {
            creneau.Bloquer();
            bloques++;
        }

        return (bloques, concernes.Where(c => c.Statut == StatutCreneau.Reserve).ToList());
    }
}

public sealed record ImportCongesDto(int Lus, int Crees, int Actualises, int RessourcesInconnues, int CreneauxBloques, IReadOnlyList<Guid> CreneauxReservesEnConflit);

/// <summary>
/// PLA-03 : import des congés depuis l'outil RH existant, en lecture seule (rien n'est jamais écrit vers l'outil RH).
/// Idempotent : un congé est identifié par sa référence RH ; un réimport actualise la période. Les créneaux libres
/// couverts sont bloqués ; les créneaux déjà réservés sont signalés pour une replanification (PLA-07).
/// </summary>
public sealed record ImporterConges(DateOnly Du, DateOnly Au);

public sealed class ImporterCongesHandler(
    IOutilRh outilRh,
    IRessourceRepository ressources,
    IAbsenceRepository absences,
    Indisponibilites indisponibilites,
    IUnitOfWork unitOfWork,
    ICurrentUser user) : ICommandHandler<ImporterConges, ImportCongesDto>
{
    public async Task<Result<ImportCongesDto>> HandleAsync(ImporterConges command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationRessources) is { } interdit)
        {
            return interdit;
        }

        if (command.Au < command.Du || command.Au.DayNumber - command.Du.DayNumber > 366)
        {
            return Error.Validation("conges.periode-invalide", "La période d'import couvre au plus 366 jours.");
        }

        var conges = await outilRh.LireCongesAsync(command.Du, command.Au, cancellationToken);
        var parReference = (await ressources.ListAsync(null, cancellationToken))
            .Where(r => r.ReferenceId is not null)
            .GroupBy(r => r.ReferenceId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        int crees = 0, actualises = 0, inconnues = 0, bloques = 0;
        var conflits = new List<Guid>();
        foreach (var conge in conges.Where(c => c.Fin > c.Debut))
        {
            if (!parReference.TryGetValue(conge.ReferenceRessource, out var ressource))
            {
                inconnues++;
                continue;
            }

            var existante = await absences.GetParReferenceAsync(SourceAbsence.OutilRh, conge.ReferenceExterne, cancellationToken);
            if (existante is null)
            {
                absences.Add(Absence.Creer(ressource.Id, conge.Debut, conge.Fin, SourceAbsence.OutilRh, conge.ReferenceExterne));
                crees++;
            }
            else if (existante.Actualiser(conge.Debut, conge.Fin))
            {
                actualises++;
            }
            else
            {
                continue;
            }

            var (b, reserves) = await indisponibilites.BloquerAsync(ressource.Id, conge.Debut, conge.Fin, cancellationToken);
            bloques += b;
            conflits.AddRange(reserves.Select(c => c.Id));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ImportCongesDto(conges.Count, crees, actualises, inconnues, bloques, conflits.Distinct().ToList());
    }
}

public sealed record ListerAbsences(Guid RessourceId, DateTimeOffset Du, DateTimeOffset Au);

public sealed class ListerAbsencesHandler(IAbsenceRepository absences, ICurrentUser user) : IQueryHandler<ListerAbsences, IReadOnlyList<AbsenceDto>>
{
    public async Task<Result<IReadOnlyList<AbsenceDto>>> HandleAsync(ListerAbsences query, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationLire) is { } interdit)
        {
            return interdit;
        }

        return (await absences.ListAsync(query.RessourceId, query.Du, query.Au, cancellationToken)).OrderBy(a => a.Debut).Select(a => a.ToDto()).ToList();
    }
}
