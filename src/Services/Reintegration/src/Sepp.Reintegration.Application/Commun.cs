using System.Globalization;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Domain.Calendar;
using Sepp.Contracts;
using Sepp.Contracts.Reintegration;
using Sepp.Reintegration.Domain;
using Sepp.Reintegration.Domain.Incapacites;
using Sepp.Reintegration.Domain.Trajets;

namespace Sepp.Reintegration.Application;

/// <summary>Parties du dossier de réintégration nommées dans les traces d'audit (<c>reintegration.&lt;partie&gt;</c>, identifiant de l'incapacité).</summary>
public static class PartiesDossier
{
    public const string Objet = "reintegration";
    public const string Recherche = "recherche";
    public const string Incapacite = "incapacite";
    public const string Estimation = "estimation";
    public const string Trajet = "trajet";
    public const string Absences = "absences";
    public const string EHealth = "ehealth";
    public const string TableauDeBord = "tableau-de-bord";
    public const string Suivi = "suivi";

    public static string TypeObjet(string partie) => $"{Objet}.{partie}";
}

/// <summary>
/// Secret médical (§2.1, §3.3, NF-04) : point de passage obligé de tout accès au contenu d'un dossier de réintégration, sur
/// le modèle du dossier de santé (service Surveillance médicale).
/// <list type="number">
/// <item>Permission de la matrice : <c>reintegration:lire</c> ou <c>reintegration:ecrire</c> (CPMT, CPMT dirigeant, infirmier).</item>
/// <item>Relation de soin : l'utilisateur est le gestionnaire du dossier (saisie ou prise en charge), l'auteur d'une estimation
/// du potentiel de travail ou l'évaluateur d'un trajet de l'incapacité. Hors relation de soin, un motif est obligatoire
/// (en-tête <c>X-Motif-Acces</c>).</item>
/// <item>Bris de glace (<c>X-Bris-De-Glace</c>) : motif obligatoire, trace signalée et alerte au CPMT dirigeant par le service Audit.</item>
/// <item>Journalisation de chaque accès par <see cref="IAuditTrail"/>, zone médicale, identifiant de l'incapacité : une lecture
/// est tracée et validée avant que la donnée soit rendue ; une écriture est tracée dans sa transaction (ARC-32).</item>
/// </list>
/// </summary>
public sealed class GardeReintegration(
    IIncapaciteRepository incapacites,
    ITrajetRepository trajets,
    ICurrentUser user,
    IContexteAcces contexte,
    IAuditTrail audit,
    IUnitOfWork unitOfWork)
{
    public static readonly Error Interdit = Error.Forbidden(
        "reintegration.interdit", "Le dossier de réintégration est réservé au CPMT et à l'infirmier (secret médical, §3.3).");

    public static readonly Error MotifObligatoire = Error.Forbidden(
        "reintegration.motif-obligatoire",
        "Accès hors relation de soin : un motif est obligatoire (en-tête X-Motif-Acces), ou un bris de glace motivé (X-Bris-De-Glace).");

    public static readonly Error Inconnu = Error.NotFound("reintegration.inconnu", "Dossier de réintégration inconnu.");

    public static readonly Error TrajetInconnu = Error.NotFound("reintegration.trajet-inconnu", "Trajet de réintégration inconnu.");

    public string UtilisateurId => user.UserId;

    public Error? VerifierPermission(ActionAudit action) =>
        user.HasPermission(action is ActionAudit.Lecture or ActionAudit.Export ? Permissions.ReintegrationLire : Permissions.ReintegrationEcrire)
            ? null
            : Interdit;

    public Task<Result<Incapacite>> LireAsync(Guid incapaciteId, string partie, CancellationToken cancellationToken) =>
        AccederAsync(incapaciteId, partie, ActionAudit.Lecture, false, cancellationToken);

    public Task<Result<Incapacite>> EcrireAsync(Guid incapaciteId, string partie, ActionAudit action, CancellationToken cancellationToken, bool relationEtablie = false) =>
        AccederAsync(incapaciteId, partie, action, relationEtablie, cancellationToken);

    /// <summary>Accès à un trajet : contrôlé et tracé sur son incapacité.</summary>
    public async Task<Result<(Trajet Trajet, Incapacite Incapacite)>> TrajetAsync(Guid trajetId, string partie, ActionAudit action, CancellationToken cancellationToken)
    {
        if (VerifierPermission(action) is { } interdit)
        {
            return interdit;
        }

        var trajet = await trajets.GetAsync(trajetId, cancellationToken);
        if (trajet is null)
        {
            return TrajetInconnu;
        }

        var incapacite = await AccederAsync(trajet.IncapaciteId, partie, action, false, cancellationToken);
        return incapacite.IsSuccess ? (trajet, incapacite.Value) : incapacite.Error!;
    }

    /// <summary>Trace d'un accès dont l'autorisation a été vérifiée autrement (création, accès externe dans son périmètre).</summary>
    public void Journaliser(ActionAudit action, string partie, Guid incapaciteId)
    {
        var motif = MotifAcces.Normaliser(contexte.Motif);
        audit.Enregistrer(action, PartiesDossier.TypeObjet(partie), incapaciteId, motif, contexte.BrisDeGlace && motif is not null);
    }

    public async Task<bool> EstEnRelationDeSoinAsync(Incapacite incapacite, CancellationToken cancellationToken) =>
        incapacite.EstSuiviePar(user.UserId) || await trajets.ExisteEvaluationParAsync(incapacite.Id, user.UserId, cancellationToken);

    private async Task<Result<Incapacite>> AccederAsync(Guid incapaciteId, string partie, ActionAudit action, bool relationEtablie, CancellationToken cancellationToken)
    {
        if (VerifierPermission(action) is { } interdit)
        {
            return interdit;
        }

        if (MotifAcces.Verifier(contexte.Motif, contexte.BrisDeGlace) is { } motifInvalide)
        {
            return motifInvalide;
        }

        var incapacite = await incapacites.GetAsync(incapaciteId, cancellationToken);
        if (incapacite is null)
        {
            return Inconnu;
        }

        var motif = MotifAcces.Normaliser(contexte.Motif);
        if (motif is null && !relationEtablie && !await EstEnRelationDeSoinAsync(incapacite, cancellationToken))
        {
            return MotifObligatoire;
        }

        var type = PartiesDossier.TypeObjet(partie);
        if (action == ActionAudit.Lecture)
        {
            await audit.EnregistrerLectureAsync(type, incapacite.Id, motif, contexte.BrisDeGlace, cancellationToken);
        }
        else
        {
            // Écrite par le SaveChangesAsync du cas d'usage, dans la transaction de la modification (ARC-32).
            audit.Enregistrer(action, type, incapacite.Id, motif, contexte.BrisDeGlace);
        }

        return incapacite;
    }

    /// <summary>Valide immédiatement les traces ajoutées par <see cref="Journaliser"/> pour une lecture (avant de rendre la donnée).</summary>
    public Task ValiderTracesAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);
}

/// <summary>
/// Accès des externes au suivi du trajet (§3.3) : statuts, échéances et conclusion du formulaire, jamais de donnée médicale.
/// Employeur et SIPP limités à leur affilié (claim <c>affilie_id</c>), travailleur à lui-même (claim <c>personne_id</c>) ; le
/// gestionnaire (interne) n'a pas de périmètre. Chaque accès est tracé dans la partition médicale du journal d'audit.
/// </summary>
public sealed class AccesSuivi(ICurrentUser user, IContexteAcces contexte)
{
    public static readonly Error HorsPerimetre = Error.NotFound("reintegration.hors-perimetre", "Aucun trajet de réintégration dans votre périmètre.");

    public bool EstExterneEmployeur => user.Roles.Contains(Roles.Employeur) || user.Roles.Contains(Roles.Sipp);

    public bool PeutVoirAffilie(Guid affilieId) => !EstExterneEmployeur || contexte.AffilieIdJeton == affilieId;

    public bool EstLeTravailleur(Guid personneId) => user.Roles.Contains(Roles.Travailleur) && contexte.PersonneIdJeton == personneId;
}

/// <summary>Paramètres légaux par défaut, tant que le service Référentiels n'a pas publié <c>REINTEGRATION.*</c>.</summary>
/// <remarks>
/// Valeurs identiques au catalogue initial de Référentiels et au §2.1 du cahier des charges (réforme du 1er janvier 2026) :
/// elles ne remplacent pas les paramètres publiés et restent <b>à valider</b> par le département juridique.
/// </remarks>
public sealed class DelaisParDefaut
{
    public int InformationTravailleurSemaines { get; set; } = 4;

    public int EstimationPotentielSemaines { get; set; } = 8;

    public int TrajetObligatoireMois { get; set; } = 6;

    public int SeuilTravailleurs { get; set; } = 20;

    public int AbsencesMedecinConseil { get; set; } = 2;

    public int InvitationJoursCalendrier { get; set; } = 49;

    public int ReponsePlanJoursCalendrier { get; set; } = 14;
}

/// <summary>Réglages d'organisation du suivi (pas des valeurs légales) : section <c>Reintegration:Suivi</c>, tous <b>à valider</b>.</summary>
public sealed class ReglagesSuivi
{
    /// <summary>SAN-65 : alerte N jours ouvrables avant chaque échéance ; 0 désactive l'alerte anticipée.</summary>
    public int AlerteAvantEcheanceJoursOuvrables { get; set; } = 5;

    /// <summary>Délai de l'employeur pour le plan ou le rapport motivé (jours calendrier) ; non fixé par le cahier : <c>null</c> = non suivi.</summary>
    public int? DelaiPlanEmployeurJoursCalendrier { get; set; }

    /// <summary>SAN-64 : tentatives d'envoi eHealth avant abandon (relance manuelle ensuite).</summary>
    public int TentativesEnvoiMaximum { get; set; } = 5;
}

public sealed class OptionsReintegration
{
    public DelaisParDefaut DelaisParDefaut { get; set; } = new();

    public ReglagesSuivi Suivi { get; set; } = new();
}

/// <summary>Politique légale en vigueur à une date : paramètres de Référentiels (ARC-21) et calendrier belge (DAT-08).</summary>
public sealed class ParametresReintegration(IProjectionRepository projections, OptionsReintegration options)
{
    public const string InformationTravailleur = "REINTEGRATION.INFORMATION_TRAVAILLEUR";
    public const string EstimationPotentiel = "REINTEGRATION.ESTIMATION_POTENTIEL";
    public const string TrajetObligatoire = "REINTEGRATION.TRAJET_OBLIGATOIRE";
    public const string SeuilTravailleurs = "REINTEGRATION.SEUIL_TRAVAILLEURS";
    public const string AbsencesMedecinConseil = "REINTEGRATION.ABSENCES_MEDECIN_CONSEIL";
    public const string Invitation = "REINTEGRATION.INVITATION.DELAI";
    public const string ReponsePlan = "REINTEGRATION.REPONSE_PLAN.DELAI";

    public static readonly IReadOnlySet<string> Suivis = new HashSet<string>(StringComparer.Ordinal)
    {
        InformationTravailleur, EstimationPotentiel, TrajetObligatoire, SeuilTravailleurs, AbsencesMedecinConseil, Invitation, ReponsePlan,
    };

    private readonly Dictionary<DateOnly, PolitiqueReintegration> _cache = [];

    public ReglagesSuivi Suivi => options.Suivi;

    public async Task<PolitiqueReintegration> PolitiqueAsync(DateOnly date, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(date, out var connue))
        {
            return connue;
        }

        var d = options.DelaisParDefaut;
        var (debut, fin) = (date.Year - 1, date.Year + 2);
        var supplementaires = await projections.ListerCalendriersAsync(debut, fin, cancellationToken);
        var calendrier = new BusinessCalendar(Enumerable.Range(debut, fin - debut + 1).SelectMany(BelgianPublicHolidays.For).Select(h => h.Date)
            .Concat(supplementaires.SelectMany(c => c.JoursSupplementaires)));
        var politique = new PolitiqueReintegration(
            await DureeAsync(InformationTravailleur, date, new(d.InformationTravailleurSemaines, UniteDelai.Semaines), cancellationToken),
            await DureeAsync(EstimationPotentiel, date, new(d.EstimationPotentielSemaines, UniteDelai.Semaines), cancellationToken),
            await DureeAsync(TrajetObligatoire, date, new(d.TrajetObligatoireMois, UniteDelai.Mois), cancellationToken),
            await NombreAsync(SeuilTravailleurs, date, d.SeuilTravailleurs, cancellationToken),
            await NombreAsync(AbsencesMedecinConseil, date, d.AbsencesMedecinConseil, cancellationToken),
            await DureeAsync(Invitation, date, new(d.InvitationJoursCalendrier, UniteDelai.JoursCalendrier), cancellationToken),
            await DureeAsync(ReponsePlan, date, new(d.ReponsePlanJoursCalendrier, UniteDelai.JoursCalendrier), cancellationToken),
            options.Suivi.DelaiPlanEmployeurJoursCalendrier is { } plan ? new DureeLegale(plan, UniteDelai.JoursCalendrier) : null,
            Math.Max(options.Suivi.AlerteAvantEcheanceJoursOuvrables, 0),
            calendrier);
        _cache[date] = politique;
        return politique;
    }

    private async Task<DureeLegale> DureeAsync(string code, DateOnly date, DureeLegale defaut, CancellationToken cancellationToken)
    {
        var parametre = await projections.ParametreApplicableAsync(code, date, cancellationToken);
        return parametre is not null && Enum.TryParse<UniteDelai>(parametre.Unite, out var unite) && parametre.Valeur >= 0
            ? new DureeLegale((int)Math.Ceiling(parametre.Valeur), unite)
            : defaut;
    }

    private async Task<int> NombreAsync(string code, DateOnly date, int defaut, CancellationToken cancellationToken)
    {
        var parametre = await projections.ParametreApplicableAsync(code, date, cancellationToken);
        return parametre is { Unite: "Nombre", Valeur: >= 0 } ? (int)Math.Ceiling(parametre.Valeur) : defaut;
    }
}

public static class Calendrier
{
    private static readonly TimeZoneInfo Bruxelles = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    /// <summary>Date du jour en Belgique (les horodatages restent en UTC, DAT-08).</summary>
    public static DateOnly Aujourdhui(this TimeProvider horloge) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(horloge.GetUtcNow(), Bruxelles).DateTime);
}

/// <summary>Codes partagés des contrats (<see cref="CodesReintegration"/>) : le domaine garde ses énumérations (il ne référence pas les contrats).</summary>
public static class CodesContrats
{
    public static string Code(this InitiateurTrajet initiateur) => initiateur switch
    {
        InitiateurTrajet.Travailleur => CodesReintegration.InitiateursTrajet.Travailleur,
        InitiateurTrajet.MedecinTraitant => CodesReintegration.InitiateursTrajet.MedecinTraitant,
        InitiateurTrajet.MedecinConseil => CodesReintegration.InitiateursTrajet.MedecinConseil,
        InitiateurTrajet.Employeur => CodesReintegration.InitiateursTrajet.Employeur,
        InitiateurTrajet.Cpmt => CodesReintegration.InitiateursTrajet.Cpmt,
        _ => throw new DomainException($"Initiateur inconnu : {initiateur}."),
    };

    public static string Code(this IssueTrajet issue) => issue switch
    {
        IssueTrajet.PlanAccepte => CodesReintegration.StatutsFinTrajet.PlanAccepte,
        IssueTrajet.PlanRefuse => CodesReintegration.StatutsFinTrajet.PlanRefuse,
        IssueTrajet.RapportMotive => CodesReintegration.StatutsFinTrajet.RapportMotive,
        IssueTrajet.Annule => CodesReintegration.StatutsFinTrajet.Annule,
        _ => throw new DomainException($"Issue inconnue : {issue}."),
    };

    public static string Code(this SourceIncapacite source) => source switch
    {
        SourceIncapacite.Employeur => CodesReintegration.SourcesIncapacite.Employeur,
        SourceIncapacite.MedecinConseil => CodesReintegration.SourcesIncapacite.MedecinConseil,
        SourceIncapacite.SaisieManuelle => CodesReintegration.SourcesIncapacite.SaisieManuelle,
        _ => throw new DomainException($"Source inconnue : {source}."),
    };

    /// <summary>Code reçu dans <c>IncapaciteNotifiee.Source</c> ; un code inconnu est traité comme une notification de l'employeur.</summary>
    public static SourceIncapacite Source(string? code) => code switch
    {
        CodesReintegration.SourcesIncapacite.MedecinConseil => SourceIncapacite.MedecinConseil,
        CodesReintegration.SourcesIncapacite.SaisieManuelle => SourceIncapacite.SaisieManuelle,
        _ => SourceIncapacite.Employeur,
    };
}

/// <summary>Traduction des événements de domaine en événements d'intégration : identifiants, dates, codes (ARC-06).</summary>
public static class EvenementsIntegration
{
    public static void Publier(AggregateRoot agregat, IIntegrationEventOutbox outbox)
    {
        foreach (var evenement in agregat.DomainEvents)
        {
            IntegrationEvent? integration = evenement switch
            {
                TrajetDemarreLocal e => new TrajetDemarre(e.TrajetId, e.PersonneId, e.AffilieId, e.Initiateur.Code(), e.DateDemande),
                TrajetTermineLocal e => new TrajetTermine(e.TrajetId, e.PersonneId, e.AffilieId, e.Issue.Code(), e.DateFin),
                IncapaciteSaisieManuellement e => new IncapaciteSaisie(
                    e.IncapaciteId, e.PersonneId, e.AffilieId, e.DateDebut, CodesReintegration.SourcesIncapacite.SaisieManuelle, e.DateFinPrevue),
                _ => null,
            };

            if (integration is not null)
            {
                outbox.Add(integration);
            }
        }
    }
}

internal static class Regles
{
    /// <summary>Exécute une règle métier et traduit sa violation en erreur de validation.</summary>
    public static Result<T> Appliquer<T>(string code, Func<T> regle)
    {
        try
        {
            return regle();
        }
        catch (DomainException ex)
        {
            return Error.Validation(code, ex.Message);
        }
    }

    public static Result<Unit> Appliquer(string code, Action regle) =>
        Appliquer(code, () =>
        {
            regle();
            return Unit.Value;
        });

    public static string Jour(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
