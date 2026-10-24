using Sepp.BuildingBlocks.Domain;

namespace Sepp.Reintegration.Domain.Trajets;

/// <summary>Initiateur de la demande de trajet (§15.3 <c>trajet.initiateur</c>).</summary>
public enum InitiateurTrajet
{
    Travailleur,
    MedecinTraitant,
    MedecinConseil,
    Employeur,
    Cpmt,
}

/// <summary>Statut dérivé des jalons du trajet (SAN-62).</summary>
public enum StatutTrajet
{
    Demande,
    Invite,
    Evalue,
    FormulaireEtabli,
    PlanDepose,
    RapportMotiveDepose,
    ReponseTravailleurRecue,
    Termine,
    Annule,
}

/// <summary>
/// Conclusion du formulaire d'évaluation de réintégration (SAN-62). Codes provisoires, <b>à valider</b> par les départements
/// médical et juridique (réglementation de 2026) ; seule la conclusion sort vers l'employeur, jamais la note médicale.
/// </summary>
public enum ConclusionEvaluation
{
    /// <summary>Le travailleur pourra reprendre un travail adapté ou un autre travail : l'employeur établit un plan.</summary>
    TravailAdapteOuAutrePossible,

    /// <summary>Aucun travail adapté ni autre travail envisageable : l'employeur établit un rapport motivé.</summary>
    AucunTravailEnvisageable,
}

public enum ReponseEmployeur
{
    PlanReintegration,
    RapportMotive,
}

public enum ReponseTravailleur
{
    Acceptation,
    Refus,
}

/// <summary>Issue d'un trajet terminé.</summary>
public enum IssueTrajet
{
    PlanAccepte,
    PlanRefuse,
    RapportMotive,
    Annule,
}

/// <summary>Étapes historisées du trajet (§15.3 <c>etape_trajet</c>).</summary>
public enum TypeEtape
{
    Demande,
    Invitation,
    Evaluation,
    Formulaire,
    PlanReintegration,
    RapportMotive,
    ReponseTravailleur,
    AbsenceRendezVous,
    InformationMedecinConseil,
    Fin,
    Annulation,
}

public sealed record TrajetDemarreLocal(Guid TrajetId, Guid IncapaciteId, Guid PersonneId, Guid AffilieId, InitiateurTrajet Initiateur, DateOnly DateDemande, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record TrajetTermineLocal(Guid TrajetId, Guid IncapaciteId, Guid PersonneId, Guid AffilieId, IssueTrajet Issue, DateOnly DateFin, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>SAN-63 : le seuil d'absences aux rendez-vous est atteint, le médecin-conseil doit être informé (une seule fois par trajet).</summary>
public sealed record SeuilAbsencesAtteint(Guid TrajetId, Guid IncapaciteId, Guid PersonneId, int NombreAbsences, DateOnly Date, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class EtapeTrajet : Entity
{
    private EtapeTrajet()
    {
    }

    internal EtapeTrajet(TypeEtape type, DateOnly date, DateOnly? echeance, Guid? documentId) : base(NewId())
    {
        Type = type;
        Date = date;
        Echeance = echeance;
        DocumentId = documentId;
    }

    public TypeEtape Type { get; private set; }

    public DateOnly Date { get; private set; }

    /// <summary>Échéance de l'étape suivante ouverte par celle-ci (invitation, réponse du travailleur…).</summary>
    public DateOnly? Echeance { get; private set; }

    /// <summary>Document produit ou reçu (service Documents, DAT-02).</summary>
    public Guid? DocumentId { get; private set; }
}

/// <summary>SAN-63 : absence à un rendez-vous du trajet (§15.3 <c>absence_rdv</c>).</summary>
public sealed class AbsenceRendezVous : Entity
{
    private AbsenceRendezVous()
    {
    }

    internal AbsenceRendezVous(Guid? rendezVousId, DateOnly dateRendezVous) : base(NewId())
    {
        RendezVousId = rendezVousId;
        DateRendezVous = dateRendezVous;
    }

    /// <summary>Rendez-vous du service Planification (absence constatée par événement) ; <c>null</c> pour une saisie.</summary>
    public Guid? RendezVousId { get; private set; }

    public DateOnly DateRendezVous { get; private set; }

    public bool NotifieMedecinConseil { get; private set; }

    internal void MarquerNotifie() => NotifieMedecinConseil = true;
}

/// <summary>
/// SAN-62, ARC-33, ADR 0008 : trajet formel de réintégration, processus persistant (§15.3 <c>trajet</c>) — demande, invitation
/// (recommandée), évaluation, formulaire d'évaluation de réintégration, plan de réintégration ou rapport motivé de l'employeur,
/// acceptation ou refus du travailleur (<c>REINTEGRATION.REPONSE_PLAN.DELAI</c>), fin. Les dates limites sont recopiées à
/// l'étape qui les ouvre ; les minuteries en base signalent les échéances proches et dépassées. Compensation : annulation
/// (demande retirée, incapacité annulée), qui publie <c>TrajetTermine(ANNULE)</c>.
/// </summary>
public sealed class Trajet : ProcessusAEcheances
{
    public const int LongueurNote = 8000;

    private readonly List<EtapeTrajet> _etapes = [];
    private readonly List<AbsenceRendezVous> _absences = [];

    private Trajet()
    {
    }

    private Trajet(Guid incapaciteId, Guid personneId, Guid affilieId, InitiateurTrajet initiateur, DateOnly dateDemande, DateOnly dateLimiteInvitation) : base(NewId())
    {
        IncapaciteId = incapaciteId;
        PersonneId = personneId;
        AffilieId = affilieId;
        Initiateur = initiateur;
        DateDemande = dateDemande;
        DateLimiteInvitation = dateLimiteInvitation;
        Statut = StatutTrajet.Demande;
    }

    public Guid IncapaciteId { get; private set; }

    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    public InitiateurTrajet Initiateur { get; private set; }

    public DateOnly DateDemande { get; private set; }

    /// <summary>Date limite d'invitation à l'évaluation (demande + <c>REINTEGRATION.INVITATION.DELAI</c>), recopiée à la demande.</summary>
    public DateOnly DateLimiteInvitation { get; private set; }

    public StatutTrajet Statut { get; private set; }

    public DateOnly? InvitationEnvoyeeLe { get; private set; }

    public bool InvitationRecommandee { get; private set; }

    public DateOnly? EvalueLe { get; private set; }

    public string? EvaluateurId { get; private set; }

    /// <summary>Note médicale de l'évaluation : chiffrée au repos (ARC-45), jamais hors zone médicale.</summary>
    public string? NoteEvaluation { get; private set; }

    public DateOnly? FormulaireLe { get; private set; }

    public ConclusionEvaluation? Conclusion { get; private set; }

    public Guid? FormulaireDocumentId { get; private set; }

    public ReponseEmployeur? ReponseEmployeur { get; private set; }

    public DateOnly? ReponseEmployeurLe { get; private set; }

    /// <summary>Date limite de réponse du travailleur au plan (dépôt + <c>REINTEGRATION.REPONSE_PLAN.DELAI</c>), recopiée au dépôt.</summary>
    public DateOnly? EcheanceReponseTravailleur { get; private set; }

    public ReponseTravailleur? ReponseTravailleur { get; private set; }

    public DateOnly? ReponseTravailleurLe { get; private set; }

    public DateOnly? FinLe { get; private set; }

    public IssueTrajet? Issue { get; private set; }

    public DateOnly? MedecinConseilInformeLe { get; private set; }

    public IReadOnlyList<EtapeTrajet> Etapes => _etapes;

    public IReadOnlyList<AbsenceRendezVous> Absences => _absences;

    public bool EstClos => Statut is StatutTrajet.Termine or StatutTrajet.Annule;

    public static Trajet Demander(
        Guid incapaciteId, Guid personneId, Guid affilieId, InitiateurTrajet initiateur, DateOnly dateDemande, PolitiqueReintegration politique, DateTimeOffset maintenant)
    {
        if (incapaciteId == Guid.Empty || personneId == Guid.Empty || affilieId == Guid.Empty || !Enum.IsDefined(initiateur))
        {
            throw new DomainException("L'incapacité, le travailleur, l'affilié et l'initiateur du trajet sont obligatoires.");
        }

        var trajet = new Trajet(incapaciteId, personneId, affilieId, initiateur, dateDemande, politique.Invitation.AjouterA(dateDemande, politique.Calendrier));
        trajet._etapes.Add(new EtapeTrajet(TypeEtape.Demande, dateDemande, trajet.DateLimiteInvitation, null));
        trajet.Raise(new TrajetDemarreLocal(trajet.Id, incapaciteId, personneId, affilieId, initiateur, dateDemande, maintenant));
        return trajet;
    }

    public bool EstEvaluePar(string utilisateurId) => string.Equals(EvaluateurId, utilisateurId, StringComparison.Ordinal);

    /// <summary>Invitation du travailleur à l'évaluation (envoi recommandé, SAN-62).</summary>
    public void Inviter(DateOnly date, bool recommandee, Guid? documentId)
    {
        ExigerOuvert();
        Exiger(EvalueLe is null, "Le travailleur a déjà été évalué.");
        Exiger(date >= DateDemande, "L'invitation ne peut pas précéder la demande.");
        InvitationEnvoyeeLe = date;
        InvitationRecommandee = recommandee;
        _etapes.Add(new EtapeTrajet(TypeEtape.Invitation, date, null, documentId));
        Deriver();
    }

    /// <summary>Évaluation de réintégration par le CPMT (note médicale chiffrée).</summary>
    public void Evaluer(DateOnly date, string evaluateurId, string? note)
    {
        ExigerOuvert();
        Exiger(InvitationEnvoyeeLe is { } invitation && date >= invitation, "L'évaluation suit l'invitation du travailleur.");
        Exiger(FormulaireLe is null, "Le formulaire d'évaluation est déjà établi.");
        EvalueLe = date;
        EvaluateurId = Texte.Obligatoire(evaluateurId, "L'évaluateur", 100);
        NoteEvaluation = Texte.Facultatif(note, "La note d'évaluation", LongueurNote);
        _etapes.Add(new EtapeTrajet(TypeEtape.Evaluation, date, null, null));
        Deriver();
    }

    /// <summary>Formulaire d'évaluation de réintégration : conclusion communiquée à l'employeur et au travailleur.</summary>
    public void EtablirFormulaire(DateOnly date, ConclusionEvaluation conclusion, Guid? documentId, PolitiqueReintegration politique)
    {
        ExigerOuvert();
        Exiger(EvalueLe is { } evaluation && date >= evaluation, "Le formulaire suit l'évaluation de réintégration.");
        Exiger(ReponseEmployeur is null, "L'employeur a déjà répondu au formulaire.");
        Exiger(Enum.IsDefined(conclusion), "Conclusion d'évaluation inconnue.");
        FormulaireLe = date;
        Conclusion = conclusion;
        FormulaireDocumentId = documentId;
        _etapes.Add(new EtapeTrajet(TypeEtape.Formulaire, date, politique.PlanEmployeur?.AjouterA(date, politique.Calendrier), documentId));
        Deriver();
    }

    /// <summary>Plan de réintégration ou rapport motivé de l'employeur (référence au document, aucun contenu).</summary>
    public void DeposerReponseEmployeur(ReponseEmployeur reponse, DateOnly date, Guid? documentId, PolitiqueReintegration politique)
    {
        ExigerOuvert();
        Exiger(FormulaireLe is { } formulaire && date >= formulaire, "La réponse de l'employeur suit le formulaire d'évaluation.");
        Exiger(ReponseTravailleur is null, "Le travailleur a déjà répondu au plan.");
        Exiger(Enum.IsDefined(reponse), "Réponse de l'employeur inconnue.");
        ReponseEmployeur = reponse;
        ReponseEmployeurLe = date;
        EcheanceReponseTravailleur = reponse == Trajets.ReponseEmployeur.PlanReintegration ? politique.ReponsePlan.AjouterA(date, politique.Calendrier) : null;
        _etapes.Add(new EtapeTrajet(reponse == Trajets.ReponseEmployeur.PlanReintegration ? TypeEtape.PlanReintegration : TypeEtape.RapportMotive,
            date, EcheanceReponseTravailleur, documentId));
        Deriver();
    }

    /// <summary>Acceptation ou refus du plan par le travailleur (SAN-62).</summary>
    public void EnregistrerReponseTravailleur(ReponseTravailleur reponse, DateOnly date)
    {
        ExigerOuvert();
        Exiger(ReponseEmployeur == Trajets.ReponseEmployeur.PlanReintegration && ReponseEmployeurLe is { } plan && date >= plan,
            "Le travailleur répond à un plan de réintégration déposé.");
        Exiger(ReponseTravailleur is null, "Le travailleur a déjà répondu au plan.");
        Exiger(Enum.IsDefined(reponse), "Réponse du travailleur inconnue.");
        ReponseTravailleur = reponse;
        ReponseTravailleurLe = date;
        _etapes.Add(new EtapeTrajet(TypeEtape.ReponseTravailleur, date, null, null));
        Deriver();
    }

    /// <summary>Fin du trajet : l'issue doit correspondre aux jalons (plan accepté ou refusé, rapport motivé).</summary>
    public void Terminer(DateOnly date, IssueTrajet issue, DateTimeOffset maintenant)
    {
        ExigerOuvert();
        Exiger(issue switch
        {
            IssueTrajet.PlanAccepte => ReponseTravailleur == Trajets.ReponseTravailleur.Acceptation,
            IssueTrajet.PlanRefuse => ReponseTravailleur == Trajets.ReponseTravailleur.Refus,
            IssueTrajet.RapportMotive => ReponseEmployeur == Trajets.ReponseEmployeur.RapportMotive,
            _ => false,
        }, "L'issue du trajet ne correspond pas à ses étapes (l'annulation a sa propre action).");
        Exiger(date >= (ReponseTravailleurLe ?? ReponseEmployeurLe ?? DateDemande), "La fin du trajet ne peut pas précéder sa dernière étape.");
        Clore(date, issue, TypeEtape.Fin, maintenant);
    }

    /// <summary>Compensation : arrêt du trajet avant son terme. Idempotent ; refusé une fois le trajet terminé.</summary>
    public bool Annuler(DateOnly date, DateTimeOffset maintenant)
    {
        if (Statut == StatutTrajet.Annule)
        {
            return false;
        }

        Exiger(Statut != StatutTrajet.Termine, "Le trajet est terminé : il ne peut plus être annulé.");
        Clore(date < DateDemande ? DateDemande : date, IssueTrajet.Annule, TypeEtape.Annulation, maintenant);
        return true;
    }

    /// <summary>
    /// SAN-63 : absence à un rendez-vous du trajet, idempotente par rendez-vous (ou par date pour une saisie). Au seuil
    /// <c>REINTEGRATION.ABSENCES_MEDECIN_CONSEIL</c>, le médecin-conseil est à informer, une seule fois.
    /// </summary>
    /// <returns><c>true</c> si l'absence est nouvelle.</returns>
    public bool EnregistrerAbsence(Guid? rendezVousId, DateOnly dateRendezVous, DateOnly aujourdHui, PolitiqueReintegration politique, DateTimeOffset maintenant)
    {
        if (EstClos)
        {
            return false;
        }

        if (_absences.Any(a => rendezVousId is not null ? a.RendezVousId == rendezVousId : a.RendezVousId is null && a.DateRendezVous == dateRendezVous))
        {
            return false;
        }

        _absences.Add(new AbsenceRendezVous(rendezVousId, dateRendezVous));
        _etapes.Add(new EtapeTrajet(TypeEtape.AbsenceRendezVous, dateRendezVous, null, null));
        if (MedecinConseilInformeLe is null && politique.AbsencesMedecinConseil > 0 && _absences.Count >= politique.AbsencesMedecinConseil)
        {
            MedecinConseilInformeLe = aujourdHui;
            foreach (var absence in _absences)
            {
                absence.MarquerNotifie();
            }

            _etapes.Add(new EtapeTrajet(TypeEtape.InformationMedecinConseil, aujourdHui, null, null));
            Raise(new SeuilAbsencesAtteint(Id, IncapaciteId, PersonneId, _absences.Count, aujourdHui, maintenant));
        }

        return true;
    }

    public override IReadOnlyList<Echeance> Echeances(PolitiqueReintegration politique)
    {
        if (EstClos)
        {
            return [];
        }

        var echeances = new List<Echeance> { new(TypeEcheance.InvitationEvaluation, DateLimiteInvitation, InvitationEnvoyeeLe, "REINTEGRATION.INVITATION.DELAI") };
        if (FormulaireLe is { } formulaire && politique.PlanEmployeur is { } delai)
        {
            echeances.Add(new(TypeEcheance.PlanEmployeur, delai.AjouterA(formulaire, politique.Calendrier), ReponseEmployeurLe, "Reintegration:Suivi:DelaiPlanEmployeur (à valider)"));
        }

        if (EcheanceReponseTravailleur is { } reponse)
        {
            echeances.Add(new(TypeEcheance.ReponseTravailleur, reponse, ReponseTravailleurLe, "REINTEGRATION.REPONSE_PLAN.DELAI"));
        }

        return echeances;
    }

    private void Clore(DateOnly date, IssueTrajet issue, TypeEtape etape, DateTimeOffset maintenant)
    {
        FinLe = date;
        Issue = issue;
        _etapes.Add(new EtapeTrajet(etape, date, null, null));
        Deriver();
        Raise(new TrajetTermineLocal(Id, IncapaciteId, PersonneId, AffilieId, issue, date, maintenant));
    }

    private void Deriver() => Statut = Issue switch
    {
        IssueTrajet.Annule => StatutTrajet.Annule,
        not null => StatutTrajet.Termine,
        _ when ReponseTravailleur is not null => StatutTrajet.ReponseTravailleurRecue,
        _ when ReponseEmployeur == Trajets.ReponseEmployeur.RapportMotive => StatutTrajet.RapportMotiveDepose,
        _ when ReponseEmployeur == Trajets.ReponseEmployeur.PlanReintegration => StatutTrajet.PlanDepose,
        _ when FormulaireLe is not null => StatutTrajet.FormulaireEtabli,
        _ when EvalueLe is not null => StatutTrajet.Evalue,
        _ when InvitationEnvoyeeLe is not null => StatutTrajet.Invite,
        _ => StatutTrajet.Demande,
    };

    private void ExigerOuvert() => Exiger(!EstClos, "Le trajet est clos.");

    private static void Exiger(bool condition, string message)
    {
        if (!condition)
        {
            throw new DomainException(message);
        }
    }
}
