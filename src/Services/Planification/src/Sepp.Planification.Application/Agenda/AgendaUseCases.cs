using System.Globalization;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Planification.Domain;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Ressources;

namespace Sepp.Planification.Application.Agenda;

public sealed record DureeStandardDto(Guid Id, string TypeActe, Guid? RessourceId, int DureeMinutes);

public sealed record PlageDto(
    DayOfWeek Jour,
    TimeOnly Debut,
    TimeOnly Fin,
    string TypeActe,
    int DureeMinutes,
    bool ReserveUrgence,
    bool OuvertEnLigne,
    IReadOnlyList<Guid> RessourcesAssociees);

public sealed record ModeleAgendaDto(
    Guid Id,
    Guid RessourceId,
    Guid LieuId,
    DateOnly ValideDu,
    DateOnly? ValideJusquAu,
    IReadOnlyList<DayOfWeek> JoursPresence,
    IReadOnlyList<PlageDto> Plages);

public sealed record CreneauDto(
    Guid Id,
    Guid RessourceId,
    Guid LieuId,
    DateTimeOffset Debut,
    DateTimeOffset Fin,
    string TypeActe,
    StatutCreneau Statut,
    bool ReserveUrgence,
    bool OuvertEnLigne,
    Guid? SessionId,
    IReadOnlyList<Guid> RessourcesMobilisees);

internal static class AgendaMapping
{
    public static ModeleAgendaDto ToDto(this ModeleAgenda m) => new(
        m.Id, m.RessourceId, m.LieuId, m.Validite.ValidFrom, m.Validite.ValidTo, m.JoursPresence,
        m.Plages.OrderBy(p => p.Jour).ThenBy(p => p.Debut)
            .Select(p => new PlageDto(p.Jour, p.Debut, p.Fin, p.TypeActe, p.DureeMinutes, p.ReserveUrgence, p.OuvertEnLigne, p.RessourcesAssociees))
            .ToList());

    public static CreneauDto ToDto(this Creneau c) =>
        new(c.Id, c.RessourceId, c.LieuId, c.Debut, c.Fin, c.TypeActe, c.Statut, c.ReserveUrgence, c.OuvertEnLigne, c.SessionId, c.RessourcesMobilisees.ToList());

    /// <summary>
    /// PLA-03 : le planificateur et le responsable de centre gèrent tous les modèles ; un CPMT ne gère que ceux de sa
    /// propre ressource (référence = identifiant de l'utilisateur).
    /// </summary>
    public static Error? PeutGererModeles(ICurrentUser user, Ressource ressource)
    {
        if (Acces.Exiger(user, Permissions.PlanificationModelesAgenda) is { } interdit)
        {
            return interdit;
        }

        return user.HasPermission(Permissions.PlanificationGerer) || string.Equals(ressource.ReferenceId, user.UserId, StringComparison.Ordinal)
            ? null
            : Error.Forbidden("modele-agenda.ressource-d-autrui", "Un CPMT ne gère que ses propres modèles d'agenda et durées standard.");
    }
}

/// <summary>PLA-03 : durée standard d'un type d'acte, globale (sans ressource) ou propre à un CPMT.</summary>
public sealed record DefinirDureeStandard(string TypeActe, int DureeMinutes, Guid? RessourceId);

public sealed class DefinirDureeStandardHandler(IDureeStandardRepository durees, IRessourceRepository ressources, IUnitOfWork unitOfWork, ICurrentUser user)
    : ICommandHandler<DefinirDureeStandard, Guid>
{
    public async Task<Result<Guid>> HandleAsync(DefinirDureeStandard command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationModelesAgenda) is { } interdit)
        {
            return interdit;
        }

        if (command.RessourceId is { } ressourceId)
        {
            var ressource = await ressources.GetAsync(ressourceId, cancellationToken);
            if (ressource is null)
            {
                return Error.NotFound("ressource.inconnue", "Ressource inconnue.");
            }

            if (AgendaMapping.PeutGererModeles(user, ressource) is { } refus)
            {
                return refus;
            }
        }
        else if (!user.HasPermission(Permissions.PlanificationGerer))
        {
            return Error.Forbidden("duree-standard.globale", "Seul le planificateur ou le responsable de centre définit les durées par défaut.");
        }

        var typeActe = Domaine.Essayer(() => CodeMetier.Normaliser(command.TypeActe, "Type d'acte"));
        if (!typeActe.IsSuccess)
        {
            return typeActe.Error!;
        }

        var existante = (await durees.ListAsync(typeActe.Value, cancellationToken)).FirstOrDefault(d => d.RessourceId == command.RessourceId);
        if (existante is not null)
        {
            if (Domaine.Essayer(() => existante.Modifier(command.DureeMinutes)) is { } erreur)
            {
                return erreur;
            }
        }
        else
        {
            var creee = Domaine.Essayer(() => DureeStandard.Creer(typeActe.Value, command.RessourceId, command.DureeMinutes));
            if (!creee.IsSuccess)
            {
                return creee.Error!;
            }

            durees.Add(existante = creee.Value);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return existante.Id;
    }
}

public sealed record ListerDureesStandard;

public sealed class ListerDureesStandardHandler(IDureeStandardRepository durees, ICurrentUser user) : IQueryHandler<ListerDureesStandard, IReadOnlyList<DureeStandardDto>>
{
    public async Task<Result<IReadOnlyList<DureeStandardDto>>> HandleAsync(ListerDureesStandard query, CancellationToken cancellationToken)
    {
        if (!user.HasPermission(Permissions.PlanificationLire) && !user.HasPermission(Permissions.PlanificationModelesAgenda))
        {
            return Error.Forbidden("planification.interdit", "Vous n'avez pas le droit d'effectuer cette opération.");
        }

        return (await durees.ListAsync(null, cancellationToken))
            .OrderBy(d => d.TypeActe, StringComparer.Ordinal).ThenBy(d => d.RessourceId)
            .Select(d => new DureeStandardDto(d.Id, d.TypeActe, d.RessourceId, d.DureeMinutes)).ToList();
    }
}

/// <summary>Plage d'un modèle : jour (« Monday » ou « lundi »), heures « HH:mm » en heure belge ; durée standard si omise.</summary>
public sealed record NouvellePlage(
    string Jour,
    string Debut,
    string Fin,
    string TypeActe,
    int? DureeMinutes,
    bool ReserveUrgence,
    bool OuvertEnLigne,
    IReadOnlyList<Guid>? RessourcesAssociees);

/// <summary>PLA-03 : nouveau modèle d'agenda d'une ressource dans un lieu ; le modèle ouvert précédent est clôturé (DAT-04).</summary>
public sealed record CreerModeleAgenda(Guid RessourceId, Guid LieuId, DateOnly ValideDu, DateOnly? ValideJusquAu, IReadOnlyList<NouvellePlage> Plages);

public sealed class CreerModeleAgendaHandler(
    IModeleAgendaRepository modeles,
    IRessourceRepository ressources,
    ILieuRepository lieux,
    IDureeStandardRepository durees,
    IUnitOfWork unitOfWork,
    ICurrentUser user) : ICommandHandler<CreerModeleAgenda, Guid>
{
    private static readonly Dictionary<string, DayOfWeek> JoursFrancais = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lundi"] = DayOfWeek.Monday,
        ["mardi"] = DayOfWeek.Tuesday,
        ["mercredi"] = DayOfWeek.Wednesday,
        ["jeudi"] = DayOfWeek.Thursday,
        ["vendredi"] = DayOfWeek.Friday,
        ["samedi"] = DayOfWeek.Saturday,
        ["dimanche"] = DayOfWeek.Sunday,
    };

    public async Task<Result<Guid>> HandleAsync(CreerModeleAgenda command, CancellationToken cancellationToken)
    {
        var ressource = await ressources.GetAsync(command.RessourceId, cancellationToken);
        if (ressource is null)
        {
            return Acces.Exiger(user, Permissions.PlanificationModelesAgenda) ?? Error.NotFound("ressource.inconnue", "Ressource inconnue.");
        }

        if (AgendaMapping.PeutGererModeles(user, ressource) is { } refus)
        {
            return refus;
        }

        if (await lieux.GetAsync(command.LieuId, cancellationToken) is null)
        {
            return Error.Validation("lieu.inconnu", "Lieu inconnu.");
        }

        if (command.Plages is not { Count: > 0 })
        {
            return Error.Validation("modele-agenda.vide", "Un modèle d'agenda comporte au moins une plage.");
        }

        var validite = Domaine.Essayer(() => new Validity(command.ValideDu, command.ValideJusquAu));
        if (!validite.IsSuccess)
        {
            return validite.Error!;
        }

        var toutesDurees = await durees.ListAsync(null, cancellationToken);
        var modele = ModeleAgenda.Creer(ressource.Id, command.LieuId, validite.Value);
        foreach (var plage in command.Plages)
        {
            if (!TryJour(plage.Jour, out var jour) || !TryHeure(plage.Debut, out var debut) || !TryHeure(plage.Fin, out var fin))
            {
                return Error.Validation("modele-agenda.plage-invalide",
                    $"Plage invalide : jour '{plage.Jour}', heures '{plage.Debut}'-'{plage.Fin}' (format HH:mm).");
            }

            var typeActe = Domaine.Essayer(() => CodeMetier.Normaliser(plage.TypeActe, "Type d'acte"));
            if (!typeActe.IsSuccess)
            {
                return typeActe.Error!;
            }

            var duree = plage.DureeMinutes ?? DureeStandard.Resoudre(toutesDurees, typeActe.Value, ressource.Id);
            if (duree is null)
            {
                return Error.Validation("modele-agenda.duree-inconnue",
                    $"Aucune durée standard n'est définie pour {typeActe.Value} : précisez la durée de la plage.");
            }

            if (Domaine.Essayer(() =>
                {
                    modele.AjouterPlage(jour, debut, fin, typeActe.Value, duree.Value, plage.ReserveUrgence, plage.OuvertEnLigne, plage.RessourcesAssociees);
                }) is { } erreur)
            {
                return erreur;
            }
        }

        foreach (var precedent in (await modeles.ListAsync(ressource.Id, cancellationToken))
                     .Where(m => m.LieuId == command.LieuId && m.Validite.Overlaps(validite.Value)))
        {
            if (!precedent.Validite.IsOpen || precedent.Validite.ValidFrom >= command.ValideDu)
            {
                return Error.Conflict("modele-agenda.chevauchement",
                    $"Un modèle de cette ressource dans ce lieu couvre déjà la période (à partir du {precedent.Validite.ValidFrom:yyyy-MM-dd}).");
            }

            precedent.Cloturer(command.ValideDu);
        }

        modeles.Add(modele);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return modele.Id;
    }

    private static bool TryJour(string valeur, out DayOfWeek jour) =>
        JoursFrancais.TryGetValue(valeur.Trim(), out jour) || (Enum.TryParse(valeur.Trim(), ignoreCase: true, out jour) && !int.TryParse(valeur, out _));

    private static bool TryHeure(string valeur, out TimeOnly heure) =>
        TimeOnly.TryParseExact(valeur.Trim(), ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out heure);
}

public sealed record ListerModelesAgenda(Guid? RessourceId);

public sealed class ListerModelesAgendaHandler(IModeleAgendaRepository modeles, ICurrentUser user) : IQueryHandler<ListerModelesAgenda, IReadOnlyList<ModeleAgendaDto>>
{
    public async Task<Result<IReadOnlyList<ModeleAgendaDto>>> HandleAsync(ListerModelesAgenda query, CancellationToken cancellationToken)
    {
        if (!user.HasPermission(Permissions.PlanificationLire) && !user.HasPermission(Permissions.PlanificationModelesAgenda))
        {
            return Error.Forbidden("planification.interdit", "Vous n'avez pas le droit d'effectuer cette opération.");
        }

        return (await modeles.ListAsync(query.RessourceId, cancellationToken))
            .OrderBy(m => m.RessourceId).ThenBy(m => m.Validite.ValidFrom).Select(m => m.ToDto()).ToList();
    }
}

public sealed record GenerationCreneauxDto(int Crees, int DejaExistants, int Bloques, IReadOnlyList<DateTimeOffset> Conflits);

/// <summary>
/// PLA-03 : génère les créneaux d'un modèle sur une période (au plus 92 jours), hors week-ends et jours fériés.
/// Idempotent : un créneau identique déjà présent est conservé. Un créneau qui chevaucherait un autre créneau d'une des
/// ressources mobilisées n'est pas créé (conflit signalé) ; un créneau couvert par une indisponibilité est créé bloqué.
/// </summary>
public sealed record GenererCreneaux(Guid ModeleAgendaId, DateOnly Du, DateOnly Au);

public sealed class GenererCreneauxHandler(
    IModeleAgendaRepository modeles,
    IRessourceRepository ressources,
    ICreneauRepository creneaux,
    IAbsenceRepository absences,
    ParametresPlanification parametres,
    IUnitOfWork unitOfWork,
    ICurrentUser user) : ICommandHandler<GenererCreneaux, GenerationCreneauxDto>
{
    public async Task<Result<GenerationCreneauxDto>> HandleAsync(GenererCreneaux command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationGerer) is { } interdit)
        {
            return interdit;
        }

        if (command.Au < command.Du || command.Au.DayNumber - command.Du.DayNumber > 92)
        {
            return Error.Validation("creneaux.periode-invalide", "La génération couvre au plus 92 jours.");
        }

        var modele = await modeles.GetAsync(command.ModeleAgendaId, cancellationToken);
        if (modele is null)
        {
            return Error.NotFound("modele-agenda.inconnu", "Modèle d'agenda inconnu.");
        }

        var ressource = await ressources.GetAsync(modele.RessourceId, cancellationToken);
        if (ressource is not { Active: true })
        {
            return Error.Validation("ressource.inactive", "La ressource du modèle est inactive.");
        }

        var calendrier = parametres.Calendrier(command.Du, command.Au);
        var prevus = modele.Projeter(command.Du, command.Au, calendrier.IsBusinessDay).ToList();
        if (prevus.Count == 0)
        {
            return new GenerationCreneauxDto(0, 0, 0, []);
        }

        var mobilisees = prevus.SelectMany(p => p.RessourcesAssociees).Append(modele.RessourceId).Distinct().ToList();
        var debut = prevus.Min(p => p.Debut);
        var fin = prevus.Max(p => p.Fin);
        var existants = (await creneaux.ChevauchantsAsync(mobilisees, debut, fin, cancellationToken)).ToList();
        var indisponibilites = new List<Domain.Ressources.Absence>();
        foreach (var r in mobilisees)
        {
            indisponibilites.AddRange(await absences.ListAsync(r, debut, fin, cancellationToken));
        }

        int crees = 0, deja = 0, bloques = 0;
        var conflits = new List<DateTimeOffset>();
        foreach (var prevu in prevus)
        {
            var ressourcesPrevu = prevu.RessourcesAssociees.Append(modele.RessourceId).ToHashSet();
            var chevauchants = existants.Where(c => c.Chevauche(prevu.Debut, prevu.Fin) && c.RessourcesMobilisees.Any(ressourcesPrevu.Contains)).ToList();
            if (chevauchants.Any(c => c.RessourceId == modele.RessourceId && c.Debut == prevu.Debut && c.Fin == prevu.Fin && c.TypeActe == prevu.TypeActe))
            {
                deja++;
                continue;
            }

            if (chevauchants.Count > 0)
            {
                conflits.Add(prevu.Debut);
                continue;
            }

            var creneau = Creneau.Creer(modele.RessourceId, modele.LieuId, prevu.Debut, prevu.Fin, prevu.TypeActe, prevu.ReserveUrgence, prevu.OuvertEnLigne,
                prevu.RessourcesAssociees, modele.Id);
            if (indisponibilites.Any(a => ressourcesPrevu.Contains(a.RessourceId) && a.Chevauche(prevu.Debut, prevu.Fin)))
            {
                creneau.Bloquer();
                bloques++;
            }

            creneaux.Add(creneau);
            existants.Add(creneau);
            crees++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new GenerationCreneauxDto(crees, deja, bloques, conflits);
    }
}

public sealed record ListerCreneaux(DateTimeOffset Du, DateTimeOffset Au, Guid? RessourceId, Guid? LieuId, string? Statut);

public sealed class ListerCreneauxHandler(ICreneauRepository creneaux, ICurrentUser user) : IQueryHandler<ListerCreneaux, IReadOnlyList<CreneauDto>>
{
    public async Task<Result<IReadOnlyList<CreneauDto>>> HandleAsync(ListerCreneaux query, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationLire) is { } interdit)
        {
            return interdit;
        }

        if (query.Au <= query.Du || query.Au - query.Du > TimeSpan.FromDays(93))
        {
            return Error.Validation("creneaux.periode-invalide", "La période consultée couvre au plus 93 jours.");
        }

        var statut = Enumerations.Optionnel<StatutCreneau>(query.Statut, "Statut de créneau");
        if (!statut.IsSuccess)
        {
            return statut.Error!;
        }

        var resultat = await creneaux.RechercherAsync(
            new CritereCreneaux(query.Du, query.Au) { Ressource = query.RessourceId, LieuId = query.LieuId, Statut = statut.Value },
            cancellationToken);
        return resultat.OrderBy(c => c.Debut).ThenBy(c => c.RessourceId).Select(c => c.ToDto()).ToList();
    }
}

/// <summary>Retire un créneau libre ou bloqué de l'agenda (libère ses ressources).</summary>
public sealed record RetirerCreneau(Guid CreneauId);

public sealed class RetirerCreneauHandler(ICreneauRepository creneaux, IUnitOfWork unitOfWork, ICurrentUser user) : ICommandHandler<RetirerCreneau, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RetirerCreneau command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationGerer) is { } interdit)
        {
            return interdit;
        }

        var creneau = await creneaux.GetAsync(command.CreneauId, cancellationToken);
        if (creneau is null)
        {
            return Error.NotFound("creneau.inconnu", "Créneau inconnu.");
        }

        try
        {
            creneau.Retirer();
        }
        catch (DomainException ex)
        {
            return Error.Conflict("creneau.reserve", ex.Message);
        }

        creneaux.Remove(creneau);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
