using System.Globalization;

using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Domain.Calendar;
using Sepp.Obligations.Domain.Calcul;

namespace Sepp.Obligations.Domain.Reprises;

/// <summary>Origine de l'annonce d'une reprise (<c>RepriseEnregistree.Origine</c>).</summary>
public enum OrigineReprise
{
    PortailEmployeur,
    Interne,

    /// <summary>Annonce reçue par l'événement <c>bff-employeur.reprise-annoncee</c> (rétrocompatibilité).</summary>
    Evenement,
}

/// <summary>Statut dérivé des jalons du processus (ARC-33, POR-04) ; l'ordre d'arrivée des jalons est quelconque.</summary>
public enum StatutReprise
{
    Annoncee,
    ObligationOuverte,
    Planifiee,
    NonCouverte,
    Convoquee,
    ExamenRealise,

    /// <summary>Décision connue alors que l'examen ne l'est pas encore (ordre d'arrivée inversé).</summary>
    DecisionEmise,
    Terminee,

    /// <summary>Absence inférieure au minimum légal : pas d'examen de reprise.</summary>
    ExamenNonRequis,
    Annulee,

    /// <summary>Le travailleur a quitté l'entreprise (ou l'obligation a été annulée) : le processus n'a plus d'objet.</summary>
    SansObjet,
}

/// <summary>Minuteries du processus (traitées par TraiterMinuteriesReprise).</summary>
public enum TypeMinuterie
{
    EcheanceMenacee,
    HorsDelai,
    RendezVousSansCloture,
    DecisionEnAttente,
    Expiration,
}

/// <summary><c>RepriseEnregistree.Statut</c>.</summary>
public enum StatutPublicationReprise
{
    Enregistree,
    Modifiee,
    Annulee,
    NonRequise,
}

/// <summary><c>PlanificationUrgenteDemandee.Motif</c>.</summary>
public enum MotifUrgence
{
    Absence,
    AnnulationRendezVous,
    ConvocationNonRemise,
}

/// <summary>Une reprise est enregistrée, modifiée, annulée ou jugée non requise : publiée en RepriseEnregistree.</summary>
public sealed record RepriseEnregistreeDomaine(
    Guid RepriseId, Guid PersonneId, Guid AffilieId, DateOnly DateReprise, DateOnly DebutAbsence, OrigineReprise Origine,
    StatutPublicationReprise Statut, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Une obligation de reprise doit être replanifiée en urgence : publiée en PlanificationUrgenteDemandee si le mode automatique est activé.</summary>
public sealed record ReplanificationUrgenteRequise(
    Guid RepriseId, Guid? ObligationId, Guid? RendezVousId, Guid PersonneId, Guid AffilieId, MotifUrgence Motif, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record AlerteReprise(TypeAlerte Type, DateOnly? Depuis, string Message);

/// <summary>
/// ARC-33, POR-04 : gestionnaire de processus persistant de l'examen de reprise du travail. Il ne porte que des identifiants,
/// des dates et des statuts (ARC-06) : il suit l'obligation <c>EXAMEN_REPRISE</c>, son rendez-vous, sa convocation, l'examen
/// et la décision. Les jalons sont idempotents et acceptés dans n'importe quel ordre ; le statut en est dérivé. La date
/// limite est recopiée de l'obligation (le moteur d'échéances reste seul juge, jamais recalculée ici).
/// </summary>
public sealed class ProcessusReprise : AggregateRoot
{
    public const string ObjetDecision = "decision";
    public const string MotifObligationLevee = "ObligationLevee";

    private ProcessusReprise()
    {
    }

    private ProcessusReprise(Guid id, Guid personneId, Guid affilieId, DateOnly dateReprise, DateOnly debutAbsence, OrigineReprise origine) : base(id)
    {
        PersonneId = personneId;
        AffilieId = affilieId;
        DateReprise = dateReprise;
        DebutAbsence = debutAbsence;
        Origine = origine;
        Statut = StatutReprise.Annoncee;
    }

    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    public DateOnly DateReprise { get; private set; }

    public DateOnly DebutAbsence { get; private set; }

    public OrigineReprise Origine { get; private set; }

    public StatutReprise Statut { get; private set; }

    public Guid? ObligationId { get; private set; }

    /// <summary>Date limite de l'obligation, recopiée (jamais recalculée par le processus).</summary>
    public DateOnly? DateLimite { get; private set; }

    public Guid? RendezVousId { get; private set; }

    public DateTimeOffset? DebutRendezVous { get; private set; }

    /// <summary>Rendez-vous auxquels la personne ne s'est pas présentée (identifiants : le décompte est idempotent).</summary>
    public IReadOnlyList<Guid> RendezVousAbsents { get; private set; } = [];

    public int NombreAbsences => RendezVousAbsents.Count;

    public Guid? ConvocationRendezVousId { get; private set; }

    public DateTimeOffset? ConvocationEnvoyeeLe { get; private set; }

    public DateTimeOffset? ConvocationNonRemiseLe { get; private set; }

    public bool UrgenceNonCouverte { get; private set; }

    /// <summary>Un nouveau rendez-vous est à prendre (absence ou annulation du rendez-vous).</summary>
    public bool ReplanificationRequise { get; private set; }

    public MotifUrgence? MotifReplanification { get; private set; }

    public Guid? ExamenId { get; private set; }

    public DateOnly? ExamenLe { get; private set; }

    public Guid? DecisionId { get; private set; }

    public DateTimeOffset? DecisionLe { get; private set; }

    public Guid? DocumentEmployeurId { get; private set; }

    public bool ExamenNonRequis { get; private set; }

    public bool SansObjet { get; private set; }

    /// <summary>Examen réalisé après la date limite, ou date limite dépassée sans examen (minuterie).</summary>
    public bool HorsDelai { get; private set; }

    public DateTimeOffset? AnnuleeLe { get; private set; }

    /// <summary>Code du motif d'annulation, jamais un texte libre (ARC-06).</summary>
    public string? MotifAnnulation { get; private set; }

    /// <summary>Prochaine minuterie à traiter (jour belge) ; <c>null</c> si aucune.</summary>
    public DateOnly? ProchaineEcheance { get; private set; }

    public TypeMinuterie? TypeMinuterie { get; private set; }

    /// <summary>Minuteries déjà déclenchées (codes de <see cref="Reprises.TypeMinuterie"/>), pour ne les signaler qu'une fois.</summary>
    public IReadOnlyList<string> MinuteriesDeclenchees { get; private set; } = [];

    public bool EstAnnulee => AnnuleeLe is not null;

    /// <summary>Processus encore suivi : ni annulé, ni sans objet, ni sans examen requis.</summary>
    public bool EstActif => !EstAnnulee && !ExamenNonRequis && !SansObjet;

    public bool ConvocationNonRemise =>
        ConvocationNonRemiseLe is { } abandonnee && (ConvocationEnvoyeeLe is not { } envoyee || abandonnee > envoyee);

    public bool EnRetardAu(DateOnly aujourdHui) => EstActif && ExamenId is null && DateLimite is { } limite && limite < aujourdHui;

    public static ProcessusReprise Enregistrer(
        Guid personneId, Guid affilieId, DateOnly dateReprise, DateOnly debutAbsence, OrigineReprise origine, DateTimeOffset maintenant)
    {
        if (personneId == Guid.Empty || affilieId == Guid.Empty)
        {
            throw new DomainException("Le travailleur et l'affilié d'une reprise sont obligatoires.");
        }

        ControlerAbsence(debutAbsence, dateReprise);
        var processus = new ProcessusReprise(NewId(), personneId, affilieId, dateReprise, debutAbsence, origine);
        processus.Raise(new RepriseEnregistreeDomaine(
            processus.Id, personneId, affilieId, dateReprise, debutAbsence, origine, StatutPublicationReprise.Enregistree, maintenant));
        return processus;
    }

    /// <summary>La date de reprise est la clé de la reprise ; seule la date de début d'absence se corrige.</summary>
    /// <returns><c>true</c> si la date a changé.</returns>
    public bool Modifier(DateOnly debutAbsence, DateTimeOffset maintenant)
    {
        if (EstAnnulee)
        {
            throw new DomainException("Une reprise annulée ne peut plus être modifiée.");
        }

        if (ExamenId is not null)
        {
            throw new DomainException("L'examen de reprise est clôturé : la reprise ne peut plus être modifiée.");
        }

        ControlerAbsence(debutAbsence, DateReprise);
        if (debutAbsence == DebutAbsence)
        {
            return false;
        }

        DebutAbsence = debutAbsence;
        Raise(new RepriseEnregistreeDomaine(Id, PersonneId, AffilieId, DateReprise, DebutAbsence, Origine, StatutPublicationReprise.Modifiee, maintenant));
        return true;
    }

    /// <summary>Compensation : refusée une fois l'examen clôturé. Idempotente.</summary>
    /// <returns><c>true</c> si le processus vient d'être annulé.</returns>
    public bool Annuler(string motif, DateTimeOffset maintenant)
    {
        if (EstAnnulee)
        {
            return false;
        }

        if (ExamenId is not null)
        {
            throw new DomainException("L'examen de reprise est clôturé : la reprise ne peut plus être annulée.");
        }

        AnnuleeLe = maintenant;
        MotifAnnulation = motif;
        ProchaineEcheance = null;
        TypeMinuterie = null;
        Deriver();
        Raise(new RepriseEnregistreeDomaine(Id, PersonneId, AffilieId, DateReprise, DebutAbsence, Origine, StatutPublicationReprise.Annulee, maintenant));
        return true;
    }

    /// <summary>Jalon : l'obligation <c>EXAMEN_REPRISE</c> est ouverte (ou rouverte, ou sa date limite a changé).</summary>
    public bool LierObligation(Guid obligationId, DateOnly? dateLimite)
    {
        var change = ObligationId != obligationId || DateLimite != dateLimite || ExamenNonRequis || SansObjet;
        if (!change)
        {
            return false;
        }

        if (DateLimite != dateLimite)
        {
            Retirer(Reprises.TypeMinuterie.EcheanceMenacee);
            Retirer(Reprises.TypeMinuterie.HorsDelai);
        }

        ObligationId = obligationId;
        DateLimite = dateLimite;
        ExamenNonRequis = false;
        SansObjet = false;
        Deriver();
        return true;
    }

    /// <summary>Jalon : un rendez-vous couvre l'obligation ; un autre rendez-vous (ou une autre date) remplace le précédent.</summary>
    public bool PlanifierRendezVous(Guid rendezVousId, DateTimeOffset debut)
    {
        if (RendezVousAbsents.Contains(rendezVousId) || (RendezVousId == rendezVousId && DebutRendezVous == debut && !ReplanificationRequise))
        {
            return false;
        }

        Retirer(Reprises.TypeMinuterie.RendezVousSansCloture);
        RendezVousId = rendezVousId;
        DebutRendezVous = debut;
        ReplanificationRequise = false;
        MotifReplanification = null;
        Deriver();
        return true;
    }

    /// <summary>Jalon : absence au rendez-vous (SAN-13). Idempotent par rendez-vous ; le rendez-vous courant est abandonné.</summary>
    public bool EnregistrerAbsence(Guid rendezVousId, DateTimeOffset maintenant)
    {
        if (RendezVousAbsents.Contains(rendezVousId))
        {
            return false;
        }

        RendezVousAbsents = [.. RendezVousAbsents, rendezVousId];
        if (RendezVousId is null || RendezVousId == rendezVousId)
        {
            AbandonnerRendezVous();
            DemanderReplanification(MotifUrgence.Absence, rendezVousId, maintenant);
        }

        Deriver();
        return true;
    }

    /// <summary>Jalon : le rendez-vous courant est annulé. <c>ObligationLevee</c> est une compensation : pas de replanification.</summary>
    public bool RendezVousAnnule(Guid rendezVousId, string? motif, DateTimeOffset maintenant)
    {
        if (RendezVousId != rendezVousId)
        {
            return false;
        }

        AbandonnerRendezVous();
        if (!string.Equals(motif, MotifObligationLevee, StringComparison.Ordinal))
        {
            DemanderReplanification(MotifUrgence.AnnulationRendezVous, rendezVousId, maintenant);
        }

        Deriver();
        return true;
    }

    /// <summary>Jalon : la convocation du rendez-vous a été remise au canal d'envoi (SAN-10). L'envoi le plus récent gagne.</summary>
    public bool EnregistrerConvocation(Guid rendezVousId, DateTimeOffset envoyeeLe)
    {
        if (ConvocationEnvoyeeLe is { } connue && envoyeeLe <= connue && ConvocationRendezVousId == rendezVousId)
        {
            return false;
        }

        ConvocationRendezVousId = rendezVousId;
        ConvocationEnvoyeeLe = envoyeeLe;
        Deriver();
        return true;
    }

    /// <summary>La convocation enregistrée ne concerne plus la date actuelle du rendez-vous (replanification sans nouvel envoi).</summary>
    public bool ReinitialiserConvocation()
    {
        if (ConvocationEnvoyeeLe is null)
        {
            return false;
        }

        ConvocationEnvoyeeLe = null;
        ConvocationRendezVousId = null;
        Deriver();
        return true;
    }

    /// <summary>Jalon : la convocation n'a pas pu être remise (SAN-10) ; alerte, et replanification urgente si le mode automatique est activé.</summary>
    public bool EnregistrerConvocationNonRemise(Guid rendezVousId, DateTimeOffset le, DateTimeOffset maintenant)
    {
        if (ConvocationNonRemiseLe is { } connue && le <= connue)
        {
            return false;
        }

        var etaitNonRemise = ConvocationNonRemise;
        ConvocationNonRemiseLe = le;
        if (!etaitNonRemise && ConvocationNonRemise)
        {
            Raise(new ReplanificationUrgenteRequise(Id, ObligationId, rendezVousId, PersonneId, AffilieId, MotifUrgence.ConvocationNonRemise, maintenant));
        }

        return true;
    }

    /// <summary>Jalon : aucun créneau d'urgence avant l'échéance légale (PLA-06).</summary>
    public bool SignalerUrgenceNonCouverte()
    {
        if (UrgenceNonCouverte)
        {
            return false;
        }

        UrgenceNonCouverte = true;
        Deriver();
        return true;
    }

    /// <summary>Jalon : l'examen de reprise est clôturé (l'obligation est réalisée).</summary>
    public bool EnregistrerExamen(Guid examenId, DateOnly date)
    {
        if (ExamenId == examenId && ExamenLe == date)
        {
            return false;
        }

        ExamenId = examenId;
        ExamenLe = date;
        ProchaineEcheance = null;
        TypeMinuterie = null;
        Deriver();
        return true;
    }

    /// <summary>Jalon : la décision est émise. La valeur la plus récente remplace la précédente.</summary>
    public bool EnregistrerDecision(Guid decisionId, DateTimeOffset recueLe)
    {
        if (DecisionLe is { } connue && recueLe < connue)
        {
            return false;
        }

        if (DecisionId == decisionId && DecisionLe == recueLe)
        {
            return false;
        }

        DecisionId = decisionId;
        DecisionLe = recueLe;
        Deriver();
        return true;
    }

    /// <summary>Jalon : l'exemplaire de la décision destiné à l'employeur est publié (DocumentPublie).</summary>
    public bool EnregistrerDocumentEmployeur(Guid documentId)
    {
        if (DocumentEmployeurId == documentId)
        {
            return false;
        }

        DocumentEmployeurId = documentId;
        return true;
    }

    /// <summary>Branche : absence inférieure au minimum légal. Publie <c>RepriseEnregistree(NonRequise)</c> une seule fois.</summary>
    public bool MarquerExamenNonRequis(DateTimeOffset maintenant)
    {
        if (ExamenNonRequis || EstAnnulee || ExamenId is not null)
        {
            return false;
        }

        ExamenNonRequis = true;
        SansObjet = false;
        ProchaineEcheance = null;
        TypeMinuterie = null;
        Deriver();
        Raise(new RepriseEnregistreeDomaine(Id, PersonneId, AffilieId, DateReprise, DebutAbsence, Origine, StatutPublicationReprise.NonRequise, maintenant));
        return true;
    }

    /// <summary>Branche : le travailleur a quitté l'entreprise, ou l'obligation a été annulée.</summary>
    public bool MarquerSansObjet()
    {
        if (SansObjet || EstAnnulee || ExamenId is not null || ExamenNonRequis)
        {
            return false;
        }

        SansObjet = true;
        ProchaineEcheance = null;
        TypeMinuterie = null;
        Deriver();
        return true;
    }

    /// <summary>Programme la prochaine minuterie non encore déclenchée. Appelé après chaque changement d'état.</summary>
    /// <returns><c>true</c> si la minuterie programmée a changé.</returns>
    public bool Reprogrammer(PolitiqueSuiviReprise politique, BusinessCalendar calendrier)
    {
        DateOnly? date = null;
        TypeMinuterie? type = null;
        if (EstActif)
        {
            foreach (var candidate in Candidates(politique, calendrier).OrderBy(c => c.Date).ThenBy(c => c.Type).Take(1))
            {
                date = candidate.Date;
                type = candidate.Type;
            }
        }

        if (date == ProchaineEcheance && type == TypeMinuterie)
        {
            return false;
        }

        ProchaineEcheance = date;
        TypeMinuterie = type;
        return true;
    }

    /// <summary>
    /// Déclenche les minuteries échues au jour donné (chacune une seule fois) : l'alerte en découle (<see cref="Alertes"/>),
    /// l'échéance dépassée sans examen marque le processus « hors délai ». Reprogramme ensuite la suivante.
    /// </summary>
    public IReadOnlyList<TypeMinuterie> DeclencherMinuteries(DateOnly aujourdHui, PolitiqueSuiviReprise politique, BusinessCalendar calendrier)
    {
        var declenchees = new List<TypeMinuterie>();
        if (EstActif)
        {
            foreach (var (date, type) in Candidates(politique, calendrier).Where(c => c.Date <= aujourdHui).OrderBy(c => c.Date).ThenBy(c => c.Type))
            {
                MinuteriesDeclenchees = [.. MinuteriesDeclenchees, type.ToString()];
                declenchees.Add(type);
            }
        }

        Deriver();
        Reprogrammer(politique, calendrier);
        return declenchees;
    }

    /// <summary>Alertes en cours du processus (POR-04), dérivées de l'état et des minuteries déclenchées.</summary>
    public IReadOnlyList<AlerteReprise> Alertes()
    {
        var alertes = new List<AlerteReprise>();
        if (!EstActif)
        {
            return alertes;
        }

        var sansExamen = ExamenId is null;
        if (sansExamen && Declenchee(Reprises.TypeMinuterie.EcheanceMenacee) && DateLimite is { } limite)
        {
            alertes.Add(new(TypeAlerte.RepriseEcheanceMenacee, limite, $"Examen de reprise : la date limite du {Jour(limite)} approche sans examen réalisé."));
        }

        if (HorsDelai)
        {
            alertes.Add(new(TypeAlerte.RepriseHorsDelai, DateLimite, DateLimite is { } l
                ? $"Examen de reprise hors délai (date limite : {Jour(l)})."
                : "Examen de reprise hors délai."));
        }

        if (sansExamen && ConvocationNonRemise)
        {
            alertes.Add(new(TypeAlerte.RepriseConvocationNonRemise, ConvocationNonRemiseLe is { } n ? JourBelge.De(n) : null, "La convocation à l'examen de reprise n'a pas pu être remise."));
        }

        if (sansExamen && UrgenceNonCouverte && RendezVousId is null)
        {
            alertes.Add(new(TypeAlerte.RepriseUrgenceNonCouverte, DateLimite, "Aucun créneau disponible avant la date limite de l'examen de reprise."));
        }

        if (sansExamen && ReplanificationRequise)
        {
            alertes.Add(new(TypeAlerte.RepriseReplanificationRequise, DateLimite, MotifReplanification == MotifUrgence.Absence
                ? "Absence au rendez-vous de l'examen de reprise : un nouveau rendez-vous est à prendre."
                : "Rendez-vous de l'examen de reprise annulé : un nouveau rendez-vous est à prendre."));
        }

        if (sansExamen && DebutRendezVous is { } debut && DateLimite is { } dateLimite && JourBelge.De(debut) > dateLimite)
        {
            alertes.Add(new(TypeAlerte.RepriseRendezVousApresDateLimite, JourBelge.De(debut), $"Le rendez-vous de l'examen de reprise ({Jour(JourBelge.De(debut))}) dépasse la date limite du {Jour(dateLimite)}."));
        }

        if (sansExamen && Declenchee(Reprises.TypeMinuterie.RendezVousSansCloture) && DebutRendezVous is { } passe)
        {
            alertes.Add(new(TypeAlerte.RepriseRendezVousSansCloture, JourBelge.De(passe), $"Le rendez-vous du {Jour(JourBelge.De(passe))} est passé sans clôture d'examen."));
        }

        if (ExamenId is not null && DecisionId is null && Declenchee(Reprises.TypeMinuterie.DecisionEnAttente))
        {
            alertes.Add(new(TypeAlerte.RepriseDecisionEnAttente, ExamenLe, "Examen de reprise clôturé sans décision émise dans le délai de suivi."));
        }

        if (Declenchee(Reprises.TypeMinuterie.Expiration))
        {
            alertes.Add(new(TypeAlerte.RepriseExpiree, DateReprise, "Le processus de reprise a dépassé sa durée de suivi administrative."));
        }

        return alertes;
    }

    private IEnumerable<(DateOnly Date, TypeMinuterie Type)> Candidates(PolitiqueSuiviReprise politique, BusinessCalendar calendrier)
    {
        if (ExamenId is null && ObligationId is not null && DateLimite is { } limite)
        {
            if (politique.AlerteAvantEcheanceJoursOuvrables > 0 && !Declenchee(Reprises.TypeMinuterie.EcheanceMenacee))
            {
                yield return (calendrier.SubtractBusinessDays(limite, politique.AlerteAvantEcheanceJoursOuvrables), Reprises.TypeMinuterie.EcheanceMenacee);
            }

            if (!Declenchee(Reprises.TypeMinuterie.HorsDelai))
            {
                yield return (limite.AddDays(1), Reprises.TypeMinuterie.HorsDelai);
            }
        }

        if (ExamenId is null && DebutRendezVous is { } debut && !Declenchee(Reprises.TypeMinuterie.RendezVousSansCloture))
        {
            yield return (calendrier.AddBusinessDays(JourBelge.De(debut), Math.Max(politique.RendezVousSansClotureJoursOuvrables, 1)), Reprises.TypeMinuterie.RendezVousSansCloture);
        }

        if (ExamenId is not null && DecisionId is null && ExamenLe is { } examen && politique.DelaiDecisionJoursOuvrables is { } delai && delai > 0
            && !Declenchee(Reprises.TypeMinuterie.DecisionEnAttente))
        {
            yield return (calendrier.AddBusinessDays(examen, delai), Reprises.TypeMinuterie.DecisionEnAttente);
        }

        if (politique.ExpirationJours is { } jours && jours > 0 && !Declenchee(Reprises.TypeMinuterie.Expiration) && (ExamenId is null || DecisionId is null))
        {
            yield return (DateReprise.AddDays(jours), Reprises.TypeMinuterie.Expiration);
        }
    }

    private bool Declenchee(TypeMinuterie type) => MinuteriesDeclenchees.Contains(type.ToString());

    private void Retirer(TypeMinuterie type) => MinuteriesDeclenchees = [.. MinuteriesDeclenchees.Where(m => m != type.ToString())];

    private void AbandonnerRendezVous()
    {
        RendezVousId = null;
        DebutRendezVous = null;
        ConvocationEnvoyeeLe = null;
        ConvocationRendezVousId = null;
        Retirer(Reprises.TypeMinuterie.RendezVousSansCloture);
    }

    private void DemanderReplanification(MotifUrgence motif, Guid rendezVousId, DateTimeOffset maintenant)
    {
        ReplanificationRequise = true;
        MotifReplanification = motif;
        Raise(new ReplanificationUrgenteRequise(Id, ObligationId, rendezVousId, PersonneId, AffilieId, motif, maintenant));
    }

    private void Deriver()
    {
        HorsDelai = Declenchee(Reprises.TypeMinuterie.HorsDelai) || (ExamenLe is { } examen && DateLimite is { } limite && examen > limite);
        Statut = Calculer();
    }

    private StatutReprise Calculer()
    {
        if (EstAnnulee)
        {
            return StatutReprise.Annulee;
        }

        if (ExamenId is not null)
        {
            return DecisionId is not null ? StatutReprise.Terminee : StatutReprise.ExamenRealise;
        }

        if (DecisionId is not null)
        {
            return StatutReprise.DecisionEmise;
        }

        if (ExamenNonRequis)
        {
            return StatutReprise.ExamenNonRequis;
        }

        if (SansObjet)
        {
            return StatutReprise.SansObjet;
        }

        if (ConvocationEnvoyeeLe is not null && RendezVousId is not null)
        {
            return StatutReprise.Convoquee;
        }

        if (RendezVousId is not null)
        {
            return StatutReprise.Planifiee;
        }

        if (UrgenceNonCouverte)
        {
            return StatutReprise.NonCouverte;
        }

        return ObligationId is not null ? StatutReprise.ObligationOuverte : StatutReprise.Annoncee;
    }

    private static void ControlerAbsence(DateOnly debutAbsence, DateOnly dateReprise)
    {
        if (debutAbsence > dateReprise)
        {
            throw new DomainException("Le début de l'absence ne peut pas suivre la date de reprise.");
        }
    }

    private static string Jour(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
