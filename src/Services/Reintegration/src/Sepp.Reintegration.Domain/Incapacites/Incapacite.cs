using Sepp.BuildingBlocks.Domain;

namespace Sepp.Reintegration.Domain.Incapacites;

/// <summary>Émetteur de la notification d'incapacité (§15.3 <c>incapacite.source</c>).</summary>
public enum SourceIncapacite
{
    Employeur,
    MedecinConseil,

    /// <summary>Notification reçue hors flux eHealth (courrier, appel) et saisie par le CPMT ou l'infirmier.</summary>
    SaisieManuelle,
}

public enum StatutIncapacite
{
    EnCours,

    /// <summary>Le travailleur a repris le travail (fin de l'incapacité).</summary>
    Terminee,

    /// <summary>Notification erronée : l'incapacité est retirée (compensation : le trajet en cours est annulé).</summary>
    Annulee,
}

/// <summary>
/// SAN-61 : résultat de l'estimation du potentiel de travail selon la méthode standardisée. Codes provisoires, <b>à valider</b>
/// par le département médical (la méthode et ses issues sont fixées par la réglementation de 2026).
/// </summary>
public enum ResultatEstimation
{
    /// <summary>Reprise envisageable du travail convenu, éventuellement adapté.</summary>
    RepriseTravailConvenu,

    /// <summary>Reprise envisageable d'un autre travail ou d'un travail adapté : un trajet de réintégration est indiqué.</summary>
    TrajetIndique,

    /// <summary>Pas de reprise envisageable à ce stade (nouvelle estimation ultérieure).</summary>
    RepriseNonEnvisageable,

    /// <summary>Estimation impossible (travailleur injoignable, absent) : la réévaluation est à planifier.</summary>
    EstimationImpossible,
}

public sealed record IncapaciteEnregistree(Guid IncapaciteId, Guid PersonneId, Guid AffilieId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// SAN-60 : incapacité saisie à la main (hors flux eHealth), à publier pour Obligations (<c>reintegration.incapacite-saisie</c>).
/// Identifiants et dates seulement : la saisie ne porte aucun contenu médical (ARC-06).
/// </summary>
public sealed record IncapaciteSaisieManuellement(Guid IncapaciteId, Guid PersonneId, Guid AffilieId, DateOnly DateDebut, DateOnly? DateFinPrevue, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Compensation : l'incapacité est annulée, le trajet qui s'y rattache doit l'être aussi.</summary>
public sealed record IncapaciteAnnulee(Guid IncapaciteId, Guid? TrajetId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>
/// SAN-61 : estimation du potentiel de travail (§15.3 <c>estimation_potentiel</c>), par le CPMT ou l'infirmier. Le commentaire
/// est une donnée médicale, chiffrée au repos avec les clés de la zone médicale (ARC-45).
/// </summary>
public sealed class EstimationPotentiel : Entity
{
    private EstimationPotentiel()
    {
    }

    internal EstimationPotentiel(DateOnly date, ResultatEstimation resultat, string methode, string auteurId, string? commentaire) : base(NewId())
    {
        Date = date;
        Resultat = resultat;
        Methode = methode;
        AuteurId = auteurId;
        Commentaire = commentaire;
    }

    public DateOnly Date { get; private set; }

    public ResultatEstimation Resultat { get; private set; }

    /// <summary>Code de la méthode standardisée employée (référentiel à valider par le département médical).</summary>
    public string Methode { get; private set; } = string.Empty;

    public string AuteurId { get; private set; } = string.Empty;

    public string? Commentaire { get; private set; }
}

/// <summary>
/// Dossier de réintégration d'une incapacité de travail (§15.3 <c>incapacite</c>, SAN-60, SAN-61), point d'entrée du
/// contrôle d'accès (secret médical) : notifiée par l'employeur ou le médecin-conseil (flux eHealth d'Intégrations,
/// <c>integrations.incapacite-notifiee</c>) ou saisie. Échéances légales : information du travailleur, estimation du potentiel
/// de travail et démarrage du trajet obligatoire (employeurs d'au moins <c>REINTEGRATION.SEUIL_TRAVAILLEURS</c> travailleurs).
/// </summary>
public sealed class Incapacite : ProcessusAEcheances
{
    public const int LongueurUtilisateur = 100;
    public const int LongueurCommentaire = 4000;

    private readonly List<EstimationPotentiel> _estimations = [];

    private Incapacite()
    {
    }

    private Incapacite(Guid id, Guid personneId, Guid affilieId, DateOnly debut, DateOnly? finPrevue, SourceIncapacite source, DateTimeOffset notifieeLe) : base(id)
    {
        if (personneId == Guid.Empty || affilieId == Guid.Empty)
        {
            throw new DomainException("Le travailleur et l'affilié d'une incapacité sont obligatoires.");
        }

        PersonneId = personneId;
        AffilieId = affilieId;
        Source = source;
        Statut = StatutIncapacite.EnCours;
        Dater(debut, finPrevue);
        NotifieeLe = notifieeLe;
        Raise(new IncapaciteEnregistree(id, personneId, affilieId, notifieeLe));
    }

    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    public DateOnly DateDebut { get; private set; }

    /// <summary>Fin prévue (dernier jour inclus), selon la dernière notification.</summary>
    public DateOnly? DateFinPrevue { get; private set; }

    /// <summary>Dernier jour d'incapacité, connu à la reprise du travail.</summary>
    public DateOnly? DateFin { get; private set; }

    public SourceIncapacite Source { get; private set; }

    public StatutIncapacite Statut { get; private set; }

    /// <summary>Horodatage de la dernière notification appliquée (une notification plus ancienne est ignorée).</summary>
    public DateTimeOffset NotifieeLe { get; private set; }

    /// <summary>CPMT ou infirmier qui a pris le dossier en charge (relation de soin, §3.3).</summary>
    public string? GestionnaireId { get; private set; }

    /// <summary>Effectif de l'employeur, saisi pour juger du trajet obligatoire (inconnu : l'échéance reste suivie, à vérifier).</summary>
    public int? EffectifEmployeur { get; private set; }

    public DateOnly? TravailleurInformeLe { get; private set; }

    public Guid? DocumentInformationId { get; private set; }

    public Guid? TrajetId { get; private set; }

    public DateOnly? TrajetDemandeLe { get; private set; }

    public IReadOnlyList<EstimationPotentiel> Estimations => _estimations;

    public bool EstEnCours => Statut == StatutIncapacite.EnCours;

    /// <summary>Notification reçue du flux eHealth : l'identifiant est celui de l'événement (consommation idempotente).</summary>
    public static Incapacite Notifier(Guid id, Guid personneId, Guid affilieId, DateOnly debut, DateOnly? finPrevue, SourceIncapacite source, DateTimeOffset notifieeLe) =>
        new(id, personneId, affilieId, debut, finPrevue, source, notifieeLe);

    /// <summary>Saisie manuelle par le CPMT ou l'infirmier, qui en devient gestionnaire.</summary>
    public static Incapacite Saisir(Guid personneId, Guid affilieId, DateOnly debut, DateOnly? finPrevue, string gestionnaireId, DateTimeOffset maintenant)
    {
        var incapacite = new Incapacite(NewId(), personneId, affilieId, debut, finPrevue, SourceIncapacite.SaisieManuelle, maintenant);
        incapacite.PrendreEnCharge(gestionnaireId);
        incapacite.Raise(new IncapaciteSaisieManuellement(incapacite.Id, personneId, affilieId, incapacite.DateDebut, incapacite.DateFinPrevue, maintenant));
        return incapacite;
    }

    /// <summary>Nouvelle notification de la même incapacité (prolongation, correction) : état courant, la plus récente l'emporte.</summary>
    public bool Actualiser(Guid personneId, Guid affilieId, DateOnly debut, DateOnly? finPrevue, SourceIncapacite source, DateTimeOffset notifieeLe)
    {
        if (notifieeLe < NotifieeLe || Statut == StatutIncapacite.Annulee)
        {
            return false;
        }

        if (personneId != PersonneId || affilieId != AffilieId)
        {
            throw new DomainException("Une notification ne peut pas changer le travailleur ni l'affilié d'une incapacité.");
        }

        var change = debut != DateDebut || finPrevue != DateFinPrevue || source != Source;
        Dater(debut, finPrevue);
        Source = source;
        NotifieeLe = notifieeLe;
        return change;
    }

    public void PrendreEnCharge(string gestionnaireId) =>
        GestionnaireId = Texte.Obligatoire(gestionnaireId, "Le gestionnaire du dossier", LongueurUtilisateur);

    public bool EstSuiviePar(string utilisateurId) =>
        string.Equals(GestionnaireId, utilisateurId, StringComparison.Ordinal) || _estimations.Any(e => e.AuteurId == utilisateurId);

    public void DefinirEffectifEmployeur(int effectif)
    {
        if (effectif < 0)
        {
            throw new DomainException("L'effectif de l'employeur ne peut pas être négatif.");
        }

        EffectifEmployeur = effectif;
    }

    /// <summary>SAN-60 : le travailleur a été informé (document envoyé, référence facultative au service Documents).</summary>
    public void EnregistrerInformationTravailleur(DateOnly date, Guid? documentId)
    {
        ExigerEnCours();
        if (date < DateDebut)
        {
            throw new DomainException("L'information du travailleur ne peut pas précéder le début de l'incapacité.");
        }

        TravailleurInformeLe = date;
        DocumentInformationId = documentId;
    }

    /// <summary>SAN-61 : estimation du potentiel de travail (CPMT ou infirmier).</summary>
    public EstimationPotentiel EstimerPotentiel(DateOnly date, ResultatEstimation resultat, string methode, string auteurId, string? commentaire)
    {
        ExigerEnCours();
        if (date < DateDebut)
        {
            throw new DomainException("L'estimation du potentiel de travail ne peut pas précéder le début de l'incapacité.");
        }

        if (!Enum.IsDefined(resultat))
        {
            throw new DomainException("Résultat d'estimation inconnu.");
        }

        var estimation = new EstimationPotentiel(date, resultat, Texte.Obligatoire(methode, "La méthode d'estimation", 60),
            Texte.Obligatoire(auteurId, "L'auteur de l'estimation", LongueurUtilisateur), Texte.Facultatif(commentaire, "Le commentaire", LongueurCommentaire));
        _estimations.Add(estimation);
        return estimation;
    }

    /// <summary>Un trajet de réintégration est demandé pour cette incapacité (un seul trajet actif à la fois).</summary>
    public void LierTrajet(Guid trajetId, DateOnly dateDemande)
    {
        ExigerEnCours();
        TrajetId = trajetId;
        TrajetDemandeLe = dateDemande;
    }

    /// <summary>Le trajet lié est clos : un nouveau trajet pourra être demandé.</summary>
    public void DelierTrajet(Guid trajetId)
    {
        if (TrajetId == trajetId)
        {
            TrajetId = null;
        }
    }

    /// <summary>Reprise du travail : fin de l'incapacité. Idempotent.</summary>
    public bool Terminer(DateOnly dernierJour)
    {
        if (Statut != StatutIncapacite.EnCours)
        {
            return false;
        }

        if (dernierJour < DateDebut.AddDays(-1))
        {
            throw new DomainException("La fin de l'incapacité ne peut pas précéder son début.");
        }

        DateFin = dernierJour;
        Statut = StatutIncapacite.Terminee;
        return true;
    }

    /// <summary>Notification erronée : compensation, le trajet rattaché doit être annulé (événement de domaine).</summary>
    public bool Annuler(DateTimeOffset maintenant)
    {
        if (Statut == StatutIncapacite.Annulee)
        {
            return false;
        }

        Statut = StatutIncapacite.Annulee;
        Raise(new IncapaciteAnnulee(Id, TrajetId, maintenant));
        return true;
    }

    public override IReadOnlyList<Echeance> Echeances(PolitiqueReintegration politique)
    {
        if (!EstEnCours)
        {
            return [];
        }

        var echeances = new List<Echeance>
        {
            new(TypeEcheance.InformationTravailleur, politique.InformationTravailleur.AjouterA(DateDebut, politique.Calendrier), TravailleurInformeLe,
                "REINTEGRATION.INFORMATION_TRAVAILLEUR"),
            new(TypeEcheance.EstimationPotentiel, politique.EstimationPotentiel.AjouterA(DateDebut, politique.Calendrier),
                _estimations.Count == 0 ? null : _estimations.Min(e => e.Date), "REINTEGRATION.ESTIMATION_POTENTIEL"),
        };

        // Trajet obligatoire : employeurs d'au moins N travailleurs ; effectif inconnu, l'échéance reste suivie (à vérifier).
        if (EffectifEmployeur is not { } effectif || effectif >= politique.SeuilTravailleurs)
        {
            echeances.Add(new(TypeEcheance.TrajetObligatoire, politique.TrajetObligatoire.AjouterA(DateDebut, politique.Calendrier), TrajetDemandeLe,
                "REINTEGRATION.TRAJET_OBLIGATOIRE"));
        }

        return echeances;
    }

    private void Dater(DateOnly debut, DateOnly? finPrevue)
    {
        if (finPrevue is { } fin && fin < debut)
        {
            throw new DomainException("La fin prévue de l'incapacité ne peut pas précéder son début.");
        }

        DateDebut = debut;
        DateFinPrevue = finPrevue;
    }

    private void ExigerEnCours()
    {
        if (!EstEnCours)
        {
            throw new DomainException("L'incapacité n'est plus en cours.");
        }
    }
}
