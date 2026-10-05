using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Integrations.Domain.Flux;

namespace Sepp.Integrations.Application.Flux;

/// <summary>Entrée du tableau de suivi (INT-02). La charge utile n'est jamais exposée.</summary>
public sealed record EchangeFluxDto(
    Guid Id,
    TypeFlux Flux,
    SensFlux Sens,
    string TypeMessage,
    string? ReferenceExterne,
    DateTimeOffset RecuLe,
    DateTimeOffset? TraiteLe,
    DateTimeOffset? DerniereTentativeLe,
    StatutEchange Statut,
    int NombreEnregistrements,
    string? CodeErreur,
    string? MessageErreur,
    int Tentatives,
    bool ChargeUtileDisponible,
    DateTimeOffset? ChargeUtilePurgeeLe,
    bool Relancable)
{
    public static EchangeFluxDto De(EchangeFlux e) => new(
        e.Id, e.Flux, e.Sens, e.TypeMessage, e.ReferenceExterne, e.RecuLe, e.TraiteLe, e.DerniereTentativeLe, e.Statut,
        e.NombreEnregistrements, e.CodeErreur, e.MessageErreur, e.Tentatives, e.ChargeUtileDisponible, e.ChargeUtilePurgeeLe, e.EstATraiter);
}

public sealed record PageDto<T>(IReadOnlyList<T> Elements, int Page, int Taille, int Total);

/// <summary>Volumes d'un flux : par jour (heure belge) ou total de la période (<c>Jour</c> nul).</summary>
public sealed record VolumeFluxDto(
    TypeFlux Flux,
    SensFlux Sens,
    DateOnly? Jour,
    int Echanges,
    int Recus,
    int Traites,
    int Rejetes,
    int EnErreur,
    int Enregistrements);

public sealed record VolumesDto(DateOnly Du, DateOnly Au, IReadOnlyList<VolumeFluxDto> Totaux, IReadOnlyList<VolumeFluxDto> ParJour);

internal static class Acces
{
    public static Error? Verifier(ICurrentUser utilisateur) =>
        utilisateur.HasPermission(Permissions.IntegrationsAdministrer)
            ? null
            : Error.Forbidden("integrations.interdit", "Le suivi et le pilotage des flux sont réservés au gestionnaire et à l'administrateur fonctionnel.");
}

internal static class Periode
{
    public const int JoursMaximum = 366;

    private static readonly TimeZoneInfo Bruxelles = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    public static DateOnly Aujourdhui(TimeProvider horloge) => Jour(horloge.GetUtcNow());

    public static DateOnly Jour(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Bruxelles).DateTime);

    /// <summary>Début (inclus) d'un jour belge en UTC.</summary>
    public static DateTimeOffset Debut(DateOnly jour)
    {
        var local = jour.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, Bruxelles.GetUtcOffset(local)).ToUniversalTime();
    }

    public static Result<(DateOnly Du, DateOnly Au)> Valider(DateOnly? du, DateOnly? au, TimeProvider horloge)
    {
        var fin = au ?? Aujourdhui(horloge);
        var debut = du ?? fin.AddDays(-29);
        if (debut > fin)
        {
            return Error.Validation("periode.invalide", "La date de début doit précéder la date de fin.");
        }

        return fin.DayNumber - debut.DayNumber >= JoursMaximum
            ? Error.Validation("periode.trop-longue", $"La période est limitée à {JoursMaximum} jours.")
            : (debut, fin);
    }
}

/// <summary>INT-02 : journal des échanges, filtré par flux, statut et période (dates belges incluses).</summary>
public sealed record ListerJournal(TypeFlux? Flux, IReadOnlySet<StatutEchange>? Statuts, DateOnly? Du, DateOnly? Au, int Page = 1, int Taille = 50);

public sealed class ListerJournalHandler(IJournalFluxRepository journal, ICurrentUser utilisateur)
    : IQueryHandler<ListerJournal, PageDto<EchangeFluxDto>>
{
    public async Task<Result<PageDto<EchangeFluxDto>>> HandleAsync(ListerJournal query, CancellationToken cancellationToken)
    {
        if (Acces.Verifier(utilisateur) is { } refus)
        {
            return refus;
        }

        if (query.Page < 1 || query.Taille is < 1 or > 200)
        {
            return Error.Validation("pagination.invalide", "Page à partir de 1, taille de 1 à 200.");
        }

        var (elements, total) = await journal.RechercherAsync(new CriteresJournal(
            query.Flux, query.Statuts, query.Du is { } du ? Periode.Debut(du) : null, query.Au is { } au ? Periode.Debut(au.AddDays(1)) : null,
            query.Page, query.Taille), cancellationToken);
        return new PageDto<EchangeFluxDto>(elements.Select(EchangeFluxDto.De).ToList(), query.Page, query.Taille, total);
    }
}

public sealed record ObtenirEchange(Guid Id);

public sealed class ObtenirEchangeHandler(IJournalFluxRepository journal, ICurrentUser utilisateur) : IQueryHandler<ObtenirEchange, EchangeFluxDto>
{
    public async Task<Result<EchangeFluxDto>> HandleAsync(ObtenirEchange query, CancellationToken cancellationToken)
    {
        if (Acces.Verifier(utilisateur) is { } refus)
        {
            return refus;
        }

        var echange = await journal.GetAsync(query.Id, cancellationToken);
        return echange is null ? Error.NotFound("echange.inconnu", "Échange inconnu.") : EchangeFluxDto.De(echange);
    }
}

/// <summary>INT-02 : volumes par flux et par jour sur une période (30 derniers jours par défaut).</summary>
public sealed record ObtenirVolumes(DateOnly? Du, DateOnly? Au);

public sealed class ObtenirVolumesHandler(IJournalFluxRepository journal, ICurrentUser utilisateur, TimeProvider horloge)
    : IQueryHandler<ObtenirVolumes, VolumesDto>
{
    public async Task<Result<VolumesDto>> HandleAsync(ObtenirVolumes query, CancellationToken cancellationToken)
    {
        if (Acces.Verifier(utilisateur) is { } refus)
        {
            return refus;
        }

        var periode = Periode.Valider(query.Du, query.Au, horloge);
        if (!periode.IsSuccess)
        {
            return periode.Error!;
        }

        var (du, au) = periode.Value;
        var lignes = await journal.VolumesAsync(Periode.Debut(du), Periode.Debut(au.AddDays(1)), cancellationToken);
        var parJour = lignes
            .GroupBy(l => (l.Flux, l.Sens, Jour: Periode.Jour(l.RecuLe)))
            .Select(g => Volume(g.Key.Flux, g.Key.Sens, g.Key.Jour, g))
            .OrderBy(v => v.Flux).ThenBy(v => v.Sens).ThenBy(v => v.Jour)
            .ToList();
        var totaux = lignes
            .GroupBy(l => (l.Flux, l.Sens))
            .Select(g => Volume(g.Key.Flux, g.Key.Sens, null, g))
            .OrderBy(v => v.Flux).ThenBy(v => v.Sens)
            .ToList();
        return new VolumesDto(du, au, totaux, parJour);
    }

    private static VolumeFluxDto Volume(TypeFlux flux, SensFlux sens, DateOnly? jour, IEnumerable<LigneVolume> lignes)
    {
        var liste = lignes.ToList();
        return new VolumeFluxDto(flux, sens, jour, liste.Count,
            liste.Count(l => l.Statut == StatutEchange.Recu),
            liste.Count(l => l.Statut == StatutEchange.Traite),
            liste.Count(l => l.Statut == StatutEchange.Rejete),
            liste.Count(l => l.Statut == StatutEchange.EnErreur),
            liste.Sum(l => l.NombreEnregistrements));
    }
}

/// <summary>INT-02 : relance manuelle d'un échange rejeté ou en erreur ; sans effet sur un échange déjà traité.</summary>
public sealed record RelancerEchange(Guid Id);

public sealed class RelancerEchangeHandler(IJournalFluxRepository journal, TraitementEchanges traitement, ICurrentUser utilisateur)
    : ICommandHandler<RelancerEchange, EchangeFluxDto>
{
    public async Task<Result<EchangeFluxDto>> HandleAsync(RelancerEchange command, CancellationToken cancellationToken)
    {
        if (Acces.Verifier(utilisateur) is { } refus)
        {
            return refus;
        }

        var echange = await journal.GetAsync(command.Id, cancellationToken);
        if (echange is null)
        {
            return Error.NotFound("echange.inconnu", "Échange inconnu.");
        }

        // Idempotence : relancer un échange traité ne refait rien et renvoie son état.
        if (echange.Statut == StatutEchange.Traite)
        {
            return EchangeFluxDto.De(echange);
        }

        if (!echange.ChargeUtileDisponible)
        {
            return Error.Conflict("echange.charge-utile-purgee",
                "La charge utile de cet échange a été purgée (durée de conservation écoulée) : il doit être redemandé à l'organisme.");
        }

        await traitement.TraiterAsync(echange, cancellationToken);
        return EchangeFluxDto.De(echange);
    }
}

/// <summary>Lancement à la demande d'un flux (en plus du traitement planifié).</summary>
public sealed record LancerFlux(TypeFlux Flux);

public sealed class LancerFluxHandler(ExecutionFlux execution, ICurrentUser utilisateur) : ICommandHandler<LancerFlux, RapportExecutionDto>
{
    public async Task<Result<RapportExecutionDto>> HandleAsync(LancerFlux command, CancellationToken cancellationToken)
    {
        if (Acces.Verifier(utilisateur) is { } refus)
        {
            return refus;
        }

        return Enum.IsDefined(command.Flux)
            ? await execution.ExecuterAsync(command.Flux, cancellationToken)
            : Error.Validation("flux.inconnu", $"Flux inconnu : {command.Flux}.");
    }
}

/// <summary>Consultation BCE à la demande pour une entreprise.</summary>
public sealed record ConsulterBce(string NumeroBce);

public sealed class ConsulterBceHandler(ExecutionFlux execution, ICurrentUser utilisateur) : ICommandHandler<ConsulterBce, EchangeFluxDto>
{
    public async Task<Result<EchangeFluxDto>> HandleAsync(ConsulterBce command, CancellationToken cancellationToken)
    {
        if (Acces.Verifier(utilisateur) is { } refus)
        {
            return refus;
        }

        var echange = await execution.ConsulterBceAsync(command.NumeroBce, cancellationToken);
        return echange.IsSuccess ? EchangeFluxDto.De(echange.Value) : echange.Error!;
    }
}
