using Sepp.BuildingBlocks.Domain;

namespace Sepp.Obligations.Domain.Obligations;

/// <summary>
/// Obligation de surveillance de la santé d'un travailleur chez un affilié (§15.3 obligation) : type d'examen, risques,
/// date due, date limite, statut (SAN-02), rendez-vous qui la couvre, et l'historique de ses calculs (trace_calcul).
/// Zone standard : uniquement des types d'examens, des dates et des statuts, jamais de contenu médical.
/// </summary>
public sealed class Obligation : AggregateRoot
{
    public const int LongueurCle = 300;

    private readonly List<TraceCalcul> _traces = [];

    private Obligation()
    {
    }

    private Obligation(Guid id, Guid personneId, Guid affilieId, string cle) : base(id)
    {
        PersonneId = personneId;
        AffilieId = affilieId;
        Cle = cle;
    }

    /// <summary>Travailleur (identifiant du service Personnes uniquement, DAT-06).</summary>
    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    /// <summary>Clé stable du déclencheur (exposition, cycle ou événement) : unique par travailleur.</summary>
    public string Cle { get; private set; } = string.Empty;

    public TypeObligation Type { get; private set; }

    public OrigineObligation Origine { get; private set; }

    /// <summary>Codes des risques à l'origine de l'obligation (§15.3 risque_ids), vide pour un événement sans risque.</summary>
    public IReadOnlyList<string> CodesRisques { get; private set; } = [];

    /// <summary>Date à partir de laquelle l'examen est dû.</summary>
    public DateOnly DateDue { get; private set; }

    /// <summary>Date au plus tard ; <c>null</c> quand la réglementation ne fixe pas de délai.</summary>
    public DateOnly? DateLimite { get; private set; }

    public StatutObligation Statut { get; private set; }

    public Guid? RendezVousId { get; private set; }

    public DateTimeOffset? DateRendezVous { get; private set; }

    public DateOnly? DateRealisation { get; private set; }

    /// <summary>Examen clôturé qui a réalisé l'obligation (identifiant du service Surveillance médicale).</summary>
    public Guid? ExamenId { get; private set; }

    public DateOnly? DateReport { get; private set; }

    public MotifAnnulation? MotifAnnulation { get; private set; }

    /// <summary>Jour où le dépassement de la date limite a été signalé (ObligationEchue), pour ne le signaler qu'une fois.</summary>
    public DateOnly? EchueSignaleeLe { get; private set; }

    public IReadOnlyList<TraceCalcul> Traces => _traces.AsReadOnly();

    public bool EstOuverte => MachineEtatsObligation.EstOuvert(Statut);

    /// <summary>Échéance de référence : la date limite, à défaut la date due.</summary>
    public DateOnly Echeance => DateLimite ?? DateDue;

    public bool EstEnRetardAu(DateOnly date) => EstOuverte && DateLimite is { } limite && limite < date;

    /// <summary>Crée l'obligation calculée par le moteur (SAN-01) ; ouverte, ou directement réalisée si un examen la satisfait déjà.</summary>
    public static Obligation Creer(Guid personneId, EcheanceCalculee echeance, DateTimeOffset maintenant)
    {
        if (personneId == Guid.Empty || echeance.AffilieId == Guid.Empty)
        {
            throw new DomainException("Le travailleur et l'affilié d'une obligation sont obligatoires.");
        }

        if (string.IsNullOrWhiteSpace(echeance.Cle) || echeance.Cle.Length > LongueurCle)
        {
            throw new DomainException($"La clé d'une obligation est obligatoire ({LongueurCle} caractères maximum).");
        }

        var obligation = new Obligation(NewId(), personneId, echeance.AffilieId, echeance.Cle);
        obligation.AppliquerEcheance(echeance, maintenant);
        if (echeance.Realisation is { } realisation)
        {
            obligation.Statut = StatutObligation.Realise;
            obligation.DateRealisation = realisation.Date;
            obligation.ExamenId = realisation.ExamenId;
        }
        else
        {
            obligation.Statut = StatutObligation.APlanifier;
            obligation.RaiseOuverte(maintenant);
        }

        return obligation;
    }

    /// <summary>
    /// SAN-04 : applique le résultat d'un recalcul. Met à jour l'échéance d'une obligation ouverte (une trace par
    /// changement), la réalise si un examen la satisfait, et rouvre une obligation annulée par un recalcul précédent
    /// ou clôturée par une sortie. Une obligation réalisée, ou annulée par une décision, n'est pas modifiée.
    /// </summary>
    /// <returns><c>true</c> si l'obligation a changé.</returns>
    public bool Actualiser(EcheanceCalculee echeance, DateTimeOffset maintenant)
    {
        if (echeance.Cle != Cle || echeance.AffilieId != AffilieId)
        {
            throw new DomainException("Le résultat du calcul ne concerne pas cette obligation.");
        }

        if (Statut == StatutObligation.Realise)
        {
            // Définitif : seul l'examen qui réalise l'obligation peut être précisé (le plus ancien examen qualifiant),
            // pour que le résultat ne dépende pas de l'ordre dans lequel les examens ont été reçus.
            if (echeance.Realisation is { } examen && (examen.Date != DateRealisation || examen.ExamenId != ExamenId))
            {
                DateRealisation = examen.Date;
                ExamenId = examen.ExamenId;
                return true;
            }

            return false;
        }

        if (Statut == StatutObligation.Annule && MotifAnnulation != Obligations.MotifAnnulation.Recalcul)
        {
            return false;
        }

        var change = AppliquerEcheance(echeance, maintenant);
        if (echeance.Realisation is { } realisation)
        {
            Realiser(realisation);
            return true;
        }

        if (Statut is StatutObligation.Annule or StatutObligation.SortiEntreprise)
        {
            Changer(StatutObligation.APlanifier);
            MotifAnnulation = null;
            RaiseOuverte(maintenant);
            return true;
        }

        return change;
    }

    /// <summary>Un examen du type attendu a été clôturé (ExamenCloture) : l'obligation est réalisée, quel que soit son statut ouvert.</summary>
    public void Realiser(Realisation realisation)
    {
        Changer(StatutObligation.Realise);
        DateRealisation = realisation.Date;
        ExamenId = realisation.ExamenId;
        MotifAnnulation = null;
    }

    /// <summary>Un rendez-vous couvre l'obligation (RendezVousPlanifie) ; un nouveau rendez-vous remplace le précédent.</summary>
    public void Planifier(Guid rendezVousId, DateTimeOffset debut)
    {
        if (rendezVousId == Guid.Empty)
        {
            throw new DomainException("L'identifiant du rendez-vous est obligatoire.");
        }

        if (MachineEtatsObligation.EstPlanifie(Statut) && RendezVousId == rendezVousId && DateRendezVous == debut)
        {
            return;
        }

        Changer(StatutObligation.Planifie);
        RendezVousId = rendezVousId;
        DateRendezVous = debut;
    }

    /// <summary>Le rendez-vous a été annulé (RendezVousAnnule) : l'obligation est de nouveau à planifier.</summary>
    public void LibererRendezVous()
    {
        Changer(StatutObligation.APlanifier);
        RendezVousId = null;
        DateRendezVous = null;
    }

    public void Convoquer()
    {
        if (RendezVousId is null)
        {
            throw new DomainException("Une obligation sans rendez-vous ne peut pas être convoquée.");
        }

        Changer(StatutObligation.Convoque);
    }

    /// <summary>Le travailleur ne s'est pas présenté au rendez-vous : à reconvoquer (SAN-13).</summary>
    public void MarquerAbsent()
    {
        if (RendezVousId is null)
        {
            throw new DomainException("Une absence ne peut être notée que pour un rendez-vous.");
        }

        Changer(StatutObligation.Absent);
    }

    /// <summary>Reporte l'examen à une date ultérieure ; l'obligation reste due.</summary>
    public void Reporter(DateOnly nouvelleDate, DateOnly aujourdHui)
    {
        if (nouvelleDate <= aujourdHui)
        {
            throw new DomainException("La date de report doit être postérieure à aujourd'hui.");
        }

        Changer(StatutObligation.Reporte);
        DateReport = nouvelleDate;
    }

    /// <summary>Absence justifiée : à reconvoquer.</summary>
    public void Excuser() => Changer(StatutObligation.Excuse);

    public void Annuler(MotifAnnulation motif)
    {
        Changer(StatutObligation.Annule);
        MotifAnnulation = motif;
        RendezVousId = null;
        DateRendezVous = null;
    }

    /// <summary>Le travailleur n'est plus occupé chez l'affilié (OccupationTerminee).</summary>
    public void SortirDeLEntreprise()
    {
        Changer(StatutObligation.SortiEntreprise);
        RendezVousId = null;
        DateRendezVous = null;
    }

    /// <summary>
    /// Signale une seule fois qu'une obligation ouverte a dépassé sa date limite (ObligationEchue). Une nouvelle date
    /// limite (recalcul) permet un nouveau signalement.
    /// </summary>
    public bool SignalerEchue(DateOnly aujourdHui)
    {
        if (!EstEnRetardAu(aujourdHui) || EchueSignaleeLe is not null)
        {
            return false;
        }

        EchueSignaleeLe = aujourdHui;
        Raise(new ObligationDevenueEchue(Id, PersonneId, AffilieId, Type, DateLimite!.Value, DateTimeOffset.UtcNow));
        return true;
    }

    private bool AppliquerEcheance(EcheanceCalculee echeance, DateTimeOffset maintenant)
    {
        var codes = echeance.CodesRisques.Select(c => c.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var derniere = _traces.Count == 0 ? null : _traces.MaxBy(t => t.Numero);
        var identique = _traces.Count > 0
                        && Type == echeance.Type
                        && Origine == echeance.Origine
                        && DateDue == echeance.DateDue
                        && DateLimite == echeance.DateLimite
                        && CodesRisques.SequenceEqual(codes)
                        && derniere!.Justification().MemeContenu(echeance.Justification);
        if (identique)
        {
            return false;
        }

        if (DateLimite != echeance.DateLimite)
        {
            EchueSignaleeLe = null;
        }

        Type = echeance.Type;
        Origine = echeance.Origine;
        CodesRisques = codes;
        DateDue = echeance.DateDue;
        DateLimite = echeance.DateLimite;
        _traces.Add(TraceCalcul.Creer(_traces.Count + 1, echeance.Justification, maintenant));
        return true;
    }

    private void Changer(StatutObligation cible)
    {
        if (!MachineEtatsObligation.EstAutorisee(Statut, cible))
        {
            throw new DomainException($"Une obligation au statut « {Statut} » ne peut pas passer au statut « {cible} ».");
        }

        Statut = cible;
    }

    private void RaiseOuverte(DateTimeOffset maintenant) =>
        Raise(new ObligationOuverte(Id, PersonneId, AffilieId, Type, DateDue, DateLimite, maintenant));
}

/// <summary>
/// Trace d'un calcul (§15.3 trace_calcul) : règle et version appliquées, explication, entrées (« clé=valeur ») et date.
/// Une trace est ajoutée à la création et à chaque changement d'échéance : l'historique explique toute l'évolution.
/// </summary>
public sealed class TraceCalcul : Entity
{
    private TraceCalcul()
    {
    }

    private TraceCalcul(Guid id, int numero, string regle, int? regleVersion, string explication, IReadOnlyList<string> entrees, DateTimeOffset dateCalcul) : base(id)
    {
        Numero = numero;
        Regle = regle;
        RegleVersion = regleVersion;
        Explication = explication;
        Entrees = entrees;
        DateCalcul = dateCalcul;
    }

    /// <summary>Rang du calcul dans l'historique de l'obligation (1 = création) : ordonne les traces même à date identique.</summary>
    public int Numero { get; private set; }

    public string Regle { get; private set; } = string.Empty;

    public int? RegleVersion { get; private set; }

    public string Explication { get; private set; } = string.Empty;

    /// <summary>Entrées du calcul au format « clé=valeur », dans l'ordre du calcul.</summary>
    public IReadOnlyList<string> Entrees { get; private set; } = [];

    public DateTimeOffset DateCalcul { get; private set; }

    public IReadOnlyList<KeyValuePair<string, string>> EntreesStructurees() =>
        Entrees.Select(e =>
        {
            var i = e.IndexOf('=', StringComparison.Ordinal);
            return i < 0 ? new KeyValuePair<string, string>(e, string.Empty) : new KeyValuePair<string, string>(e[..i], e[(i + 1)..]);
        }).ToList();

    public Justification Justification() => new(Regle, RegleVersion, Explication, EntreesStructurees());

    internal static TraceCalcul Creer(int numero, Justification justification, DateTimeOffset dateCalcul) =>
        new(
            NewId(),
            numero,
            Tronquer(justification.Regle, 200),
            justification.RegleVersion,
            Tronquer(justification.Explication, 1000),
            justification.Entrees.Select(e => Tronquer($"{e.Key}={e.Value}", 500)).ToList(),
            dateCalcul);

    private static string Tronquer(string valeur, int longueur) => valeur.Length <= longueur ? valeur : valeur[..longueur];
}

/// <summary>L'obligation est due (création ou réouverture) : publiée en ObligationCreee.</summary>
public sealed record ObligationOuverte(
    Guid ObligationId, Guid PersonneId, Guid AffilieId, TypeObligation Type, DateOnly DateDue, DateOnly? DateLimite, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>La date limite d'une obligation ouverte est dépassée : publiée en ObligationEchue.</summary>
public sealed record ObligationDevenueEchue(
    Guid ObligationId, Guid PersonneId, Guid AffilieId, TypeObligation Type, DateOnly DateLimite, DateTimeOffset OccurredAt) : IDomainEvent;
