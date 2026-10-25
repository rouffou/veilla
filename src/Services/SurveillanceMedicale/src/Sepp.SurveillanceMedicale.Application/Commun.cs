using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Domain.Calendar;
using Sepp.Contracts;
using Sepp.Contracts.Referentiels;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.SurveillanceMedicale.Domain.Conservation;
using Sepp.SurveillanceMedicale.Domain.Decisions;
using Sepp.SurveillanceMedicale.Domain.DemandesCopie;
using Sepp.SurveillanceMedicale.Domain.Dossiers;
using Sepp.SurveillanceMedicale.Domain.Examens;

namespace Sepp.SurveillanceMedicale.Application;

/// <summary>Parties du dossier nommées dans les traces d'audit (type d'objet <c>dossier-sante.&lt;partie&gt;</c>, identifiant du dossier).</summary>
public static class PartiesDossier
{
    public const string Objet = "dossier-sante";
    public const string Identification = "identification";
    public const string Dossier = "dossier";
    public const string Consultation = "consultation";
    public const string Expositions = "expositions";
    public const string PiecesJointes = "pieces-jointes";
    public const string Questionnaires = "questionnaires";
    public const string Examen = "examen";
    public const string Decision = "decision";
    public const string Vaccinations = "vaccinations";
    public const string MaladiesProfessionnelles = "maladies-professionnelles";
    public const string Transfert = "transfert";
    public const string Conservation = "conservation";
    public const string Export = "export";
    public const string Copie = "copie";

    public static string TypeObjet(string partie) => $"{Objet}.{partie}";
}

/// <summary>
/// Secret médical (§2.1, §3.3, NF-04) : point de passage obligé de tout accès au contenu d'un dossier de santé.
/// <list type="number">
/// <item>Permission de la matrice : <c>dossier-sante:lire</c> ou <c>dossier-sante:ecrire</c> (CPMT, CPMT dirigeant, infirmier).</item>
/// <item>Relation de soin : l'utilisateur est le CPMT gestionnaire du dossier, a un examen (en cours ou clôturé) dans
/// ce dossier, ou a vacciné la personne ; l'ouverture d'un examen sur un rendez-vous planifié pour la personne
/// établit la relation. Hors relation de soin, un motif est obligatoire (en-tête <c>X-Motif-Acces</c>).</item>
/// <item>Bris de glace (continuité, remplacement d'un médecin) : motif obligatoire, trace signalée et alerte au
/// CPMT dirigeant par le service Audit (<c>audit.bris-de-glace-signale</c>).</item>
/// <item>Journalisation de chaque accès (lecture, export, création, modification, suppression) par <see cref="IAuditTrail"/>,
/// zone médicale : une lecture est tracée et validée avant que la donnée soit rendue.</item>
/// </list>
/// </summary>
public sealed class GardeDossier(
    IDossierSanteRepository dossiers,
    IExamenRepository examens,
    ICurrentUser user,
    IContexteAcces contexte,
    IAuditTrail audit,
    IUnitOfWork unitOfWork)
{
    public static readonly Error Interdit = Error.Forbidden(
        "dossier-sante.interdit", "Le dossier de santé est réservé au CPMT et à l'infirmier (secret médical, §3.3).");

    public static readonly Error MotifObligatoire = Error.Forbidden(
        "dossier-sante.motif-obligatoire",
        "Accès hors relation de soin : un motif est obligatoire (en-tête X-Motif-Acces), ou un bris de glace motivé (X-Bris-De-Glace).");

    public static readonly Error Inconnu = Error.NotFound("dossier-sante.inconnu", "Dossier de santé inconnu.");

    public Task<Result<DossierSante>> LireAsync(Guid dossierId, string partie, CancellationToken cancellationToken) =>
        AccederAsync(dossierId, partie, ActionAudit.Lecture, false, cancellationToken);

    public Task<Result<DossierSante>> EcrireAsync(Guid dossierId, string partie, ActionAudit action, CancellationToken cancellationToken, bool relationEtablie = false) =>
        AccederAsync(dossierId, partie, action, relationEtablie, cancellationToken);

    /// <summary>Contrôle de la seule permission, avant de charger quoi que ce soit (même réponse que le dossier existe ou non).</summary>
    public Error? VerifierPermission(ActionAudit action) =>
        user.HasPermission(action is ActionAudit.Lecture or ActionAudit.Export ? Permissions.DossierSanteLire : Permissions.DossierSanteEcrire)
            ? null
            : Interdit;

    /// <summary>Trace d'un accès dont l'autorisation a été vérifiée autrement (création de dossier, purge, questionnaire pré-rempli).</summary>
    public void Journaliser(ActionAudit action, string partie, Guid dossierId)
    {
        var motif = MotifAcces.Normaliser(contexte.Motif);
        audit.Enregistrer(action, PartiesDossier.TypeObjet(partie), dossierId, motif, contexte.BrisDeGlace && motif is not null);
    }

    private async Task<Result<DossierSante>> AccederAsync(Guid dossierId, string partie, ActionAudit action, bool relationEtablie, CancellationToken cancellationToken)
    {
        if (VerifierPermission(action) is { } interdit)
        {
            return interdit;
        }

        if (MotifAcces.Verifier(contexte.Motif, contexte.BrisDeGlace) is { } motifInvalide)
        {
            return motifInvalide;
        }

        var dossier = await dossiers.GetAsync(dossierId, cancellationToken);
        if (dossier is null)
        {
            return Inconnu;
        }

        var motif = MotifAcces.Normaliser(contexte.Motif);
        if (motif is null && !relationEtablie && !await EstEnRelationDeSoinAsync(dossier, cancellationToken))
        {
            return MotifObligatoire;
        }

        var type = PartiesDossier.TypeObjet(partie);
        switch (action)
        {
            case ActionAudit.Lecture:
                await audit.EnregistrerLectureAsync(type, dossier.Id, motif, contexte.BrisDeGlace, cancellationToken);
                break;
            case ActionAudit.Export:
                audit.Enregistrer(ActionAudit.Export, type, dossier.Id, motif, contexte.BrisDeGlace);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                break;
            default:
                // Écrite par le SaveChangesAsync du cas d'usage, dans la transaction de la modification (ARC-32).
                audit.Enregistrer(action, type, dossier.Id, motif, contexte.BrisDeGlace);
                break;
        }

        return dossier;
    }

    /// <summary>Relation de soin (§3.3, ARC-41) : CPMT gestionnaire, professionnel d'un examen du dossier, ou vaccinateur.</summary>
    public async Task<bool> EstEnRelationDeSoinAsync(DossierSante dossier, CancellationToken cancellationToken) =>
        dossier.EstSuiviPar(user.UserId) || await examens.ExisteExamenDuProfessionnelAsync(dossier.Id, user.UserId, cancellationToken);
}

/// <summary>
/// Paramètres de la zone médicale non portés par le service Référentiels (valeurs par défaut documentées). Les durées
/// légales (conservation du dossier, délais de concertation et de recours) ne se configurent pas ici : elles viennent de
/// Référentiels (ARC-21), sinon de leur valeur initiale dans <c>ParametresLegauxParDefaut</c> (Sepp.Contracts).
/// </summary>
public sealed class OptionsSurveillanceMedicale
{
    /// <summary>SAN-43 : droit d'accès du travailleur (demande de copie du dossier).</summary>
    public OptionsCopieDossier CopieDossier { get; set; } = new();
}

/// <summary>
/// SAN-43, §3.3 « copie sur demande » : délai dans lequel le CPMT traite une demande de copie, en jours calendrier à compter
/// du dépôt. Le cahier des charges ne le fixe pas : 15 jours par défaut, repris de la loi du 22 août 2002 relative aux
/// droits du patient (art. 9) — <b>à valider</b> par le service juridique et le département médical.
/// </summary>
public sealed class OptionsCopieDossier
{
    public int DelaiJours { get; set; } = 15;
}

/// <summary>Politiques légales alimentées par le service Référentiels (ARC-21, ARC-24) et le calendrier belge (DAT-08).</summary>
public sealed class ParametresMedicaux(IProjectionRepository projections)
{
    public async Task<int> ConservationMinimumAnneesAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var parametre = await projections.ParametreApplicableAsync(PolitiqueConservationDossier.CodeParametreMinimum, date, cancellationToken);
        var annees = parametre?.Unite switch
        {
            "Annees" => (int)Math.Ceiling(parametre.Valeur),
            "Mois" => (int)Math.Ceiling(parametre.Valeur / 12),
            _ => ParametresLegauxParDefaut.Obtenir(PolitiqueConservationDossier.CodeParametreMinimum).Valeur,
        };
        return Math.Max(annees, PolitiqueConservationDossier.PlancherAnnees);
    }

    /// <summary>
    /// SAN-34 : délais de concertation et de recours (jours ouvrables) applicables à une date : paramètres reçus de
    /// Référentiels (<c>SANTE.CONCERTATION.*</c>, <c>SANTE.RECOURS.*</c>), sinon valeurs initiales du contrat.
    /// </summary>
    public async Task<DelaisRecours> DelaisRecoursAsync(DateOnly date, CancellationToken cancellationToken) =>
        new(
            await JoursOuvrablesAsync(CodesParametresLegaux.ConcertationIntroductionDelai, date, cancellationToken),
            await JoursOuvrablesAsync(CodesParametresLegaux.ConcertationIssueDelai, date, cancellationToken),
            await JoursOuvrablesAsync(CodesParametresLegaux.RecoursIntroductionDelai, date, cancellationToken),
            await JoursOuvrablesAsync(CodesParametresLegaux.RecoursDecisionDelai, date, cancellationToken));

    private async Task<int> JoursOuvrablesAsync(string code, DateOnly date, CancellationToken cancellationToken)
    {
        var parametre = await projections.ParametreApplicableAsync(code, date, cancellationToken);
        return parametre is { Unite: UnitesParametreLegal.JoursOuvrables, Valeur: >= 0 } && parametre.Valeur == decimal.Truncate(parametre.Valeur)
            ? (int)parametre.Valeur
            : ParametresLegauxParDefaut.Obtenir(code).Valeur;
    }

    /// <summary>
    /// SAN-34 : délais de concertation et de recours en jours ouvrables, avec les jours fériés légaux belges et les jours
    /// supplémentaires reçus du service Référentiels (<c>referentiels.jours-feries-modifies</c>, DAT-08).
    /// </summary>
    public async Task<PolitiqueRecours> PolitiqueRecoursAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var (debut, fin) = Calendrier.Annees(date);
        var supplementaires = await projections.ListerCalendriersAsync(debut, fin, cancellationToken);
        return new PolitiqueRecours(Calendrier.Belge(date, supplementaires.SelectMany(c => c.JoursSupplementaires)), await DelaisRecoursAsync(date, cancellationToken));
    }
}

public static class Calendrier
{
    private static readonly TimeZoneInfo Bruxelles = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    /// <summary>Date du jour en Belgique (les horodatages restent en UTC, DAT-08).</summary>
    public static DateOnly Aujourdhui(this TimeProvider horloge) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(horloge.GetUtcNow(), Bruxelles).DateTime);

    /// <summary>Années couvertes par le calendrier construit autour d'une date (les délais débordent sur l'année suivante).</summary>
    public static (int Debut, int Fin) Annees(DateOnly autour) => (autour.Year - 1, autour.Year + 2);

    /// <summary>
    /// Calendrier des jours ouvrables autour d'une date (DAT-08) : les dix jours fériés légaux belges calculés, plus les
    /// jours fériés supplémentaires (jours de remplacement, fêtes des Communautés) reçus du service Référentiels.
    /// </summary>
    public static BusinessCalendar Belge(DateOnly autour, IEnumerable<DateOnly>? joursSupplementaires = null)
    {
        var (debut, fin) = Annees(autour);
        var legaux = Enumerable.Range(debut, fin - debut + 1).SelectMany(BelgianPublicHolidays.For).Select(h => h.Date);
        return new BusinessCalendar(legaux.Concat(joursSupplementaires ?? []));
    }
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
                ExamenClotureLocal e => new ExamenCloture(e.ExamenId, e.PersonneId, e.AffilieId, e.TypeExamen, e.Date),
                // Catégorie : code partagé Sepp.Contracts.Decisions.CategoriesDecision (conformité testée, #293).
                DecisionTransmise e => new DecisionEmise(e.DecisionId, e.PersonneId, e.AffilieId, CodesDecision.Code(e.Categorie), [.. e.Mesures], e.ValideJusquAu, e.ExamenId),
                VaccinationEnregistree e => new VaccinationAdministree(e.VaccinationId, e.PersonneId, e.VaccinCode, e.Dose, e.Date),
                // SAN-43 : identifiants seulement ; Documents lit le contenu avec son compte technique (ARC-06).
                CopieDossierValideeLocal e => new CopieDossierValidee(e.DemandeId, e.PersonneId),
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

    public static Error? Permission(ICurrentUser user, string permission, string message) =>
        user.HasPermission(permission) ? null : Error.Forbidden("surveillance-medicale.interdit", message);
}
