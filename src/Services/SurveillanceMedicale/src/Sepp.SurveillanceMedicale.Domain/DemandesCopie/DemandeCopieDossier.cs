using Sepp.BuildingBlocks.Domain;

namespace Sepp.SurveillanceMedicale.Domain.DemandesCopie;

/// <summary>Étapes d'une demande de copie du dossier de santé (SAN-43), visibles par le travailleur.</summary>
public enum StatutDemandeCopie
{
    /// <summary>Déposée par le travailleur, en attente du CPMT (délai légal).</summary>
    Deposee,

    /// <summary>Validée par le CPMT : le contenu est transmis au service Documents pour génération.</summary>
    Validee,

    /// <summary>Copie publiée au travailleur par le service Documents (téléchargeable dans « Mes documents »).</summary>
    Publiee,

    /// <summary>Refusée par le CPMT, pour un motif codé (jamais de texte libre).</summary>
    Refusee,
}

/// <summary>Motif codé d'un refus : aucun texte libre, pour ne rien laisser fuir de la zone médicale vers le portail.</summary>
public enum MotifRefusCopie
{
    /// <summary>Aucun dossier de santé n'existe pour la personne.</summary>
    AucunDossier,

    /// <summary>L'identité du demandeur n'a pas pu être confirmée ; le travailleur est invité à contacter le SEPP.</summary>
    IdentiteNonConfirmee,
}

/// <summary>
/// SAN-43, §3.3 (« Dossier de santé — travailleur : copie sur demande ») : demande de copie du dossier déposée par le
/// travailleur depuis le portail. Elle ne contient aucune donnée médicale : personne, dates, statut et identifiant du
/// document publié. Le CPMT la valide (le contenu part alors vers le service Documents, qui le publie au travailleur en
/// zone médicale) ou la refuse pour un motif codé. Une seule demande en cours par personne.
/// </summary>
public sealed class DemandeCopieDossier : AggregateRoot
{
    private DemandeCopieDossier()
    {
    }

    private DemandeCopieDossier(Guid id, Guid personneId, Guid? dossierId, DateTimeOffset deposeeLe, DateOnly dateLimite) : base(id)
    {
        PersonneId = Garde.Identifiant(personneId, "personne");
        DossierId = dossierId;
        DeposeeLe = deposeeLe;
        DateLimite = dateLimite;
        Statut = StatutDemandeCopie.Deposee;
    }

    public Guid PersonneId { get; private set; }

    /// <summary>Dossier de la personne au dépôt (ou à la validation) ; <c>null</c> si elle n'en a pas.</summary>
    public Guid? DossierId { get; private set; }

    public DateTimeOffset DeposeeLe { get; private set; }

    /// <summary>Date limite de traitement par le CPMT (délai légal paramétré, à valider).</summary>
    public DateOnly DateLimite { get; private set; }

    public StatutDemandeCopie Statut { get; private set; }

    public string? TraiteePar { get; private set; }

    public DateTimeOffset? TraiteeLe { get; private set; }

    public MotifRefusCopie? MotifRefus { get; private set; }

    /// <summary>Document publié au travailleur par le service Documents (zone médicale).</summary>
    public Guid? DocumentId { get; private set; }

    public DateTimeOffset? PublieeLe { get; private set; }

    /// <summary>En cours tant qu'elle n'est ni publiée ni refusée.</summary>
    public bool EstEnCours => Statut is StatutDemandeCopie.Deposee or StatutDemandeCopie.Validee;

    /// <param name="jourDepot">Date du dépôt en Belgique : point de départ du délai, en jours calendrier.</param>
    public static DemandeCopieDossier Deposer(Guid personneId, Guid? dossierId, DateTimeOffset deposeeLe, DateOnly jourDepot, int delaiJours) =>
        delaiJours < 1
            ? throw new DomainException("Le délai de traitement d'une demande de copie est d'au moins un jour.")
            : new(NewId(), personneId, dossierId, deposeeLe, jourDepot.AddDays(delaiJours));

    /// <summary>Le délai légal est dépassé et le CPMT n'a pas encore traité la demande.</summary>
    public bool EstEnRetard(DateOnly aujourdhui) => Statut == StatutDemandeCopie.Deposee && aujourdhui > DateLimite;

    /// <summary>Le CPMT valide l'export : la copie est transmise au service Documents (événement d'identifiants seulement).</summary>
    public void Valider(Guid dossierId, string cpmtId, DateTimeOffset le)
    {
        ExigerDeposee();
        DossierId = Garde.Identifiant(dossierId, "dossier");
        TraiteePar = Garde.Requis(cpmtId, "CPMT", 100);
        TraiteeLe = le;
        Statut = StatutDemandeCopie.Validee;
        Raise(new CopieDossierValideeLocal(Id, PersonneId, le));
    }

    public void Refuser(MotifRefusCopie motif, string cpmtId, DateTimeOffset le)
    {
        ExigerDeposee();
        if (!Enum.IsDefined(motif))
        {
            throw new DomainException("Motif de refus inconnu.");
        }

        MotifRefus = motif;
        TraiteePar = Garde.Requis(cpmtId, "CPMT", 100);
        TraiteeLe = le;
        Statut = StatutDemandeCopie.Refusee;
    }

    /// <summary>
    /// Le service Documents a publié la copie (<c>documents.document-publie</c>). Idempotent : une publication déjà
    /// enregistrée est ignorée ; une publication d'une demande non validée est refusée.
    /// </summary>
    /// <returns><c>true</c> si la demande a changé.</returns>
    public bool EnregistrerPublication(Guid documentId, DateTimeOffset le)
    {
        if (Statut == StatutDemandeCopie.Publiee)
        {
            return false;
        }

        if (Statut != StatutDemandeCopie.Validee)
        {
            throw new DomainException($"Seule une demande validée peut être publiée (statut : {Statut}).");
        }

        DocumentId = Garde.Identifiant(documentId, "document");
        PublieeLe = le;
        Statut = StatutDemandeCopie.Publiee;
        return true;
    }

    private void ExigerDeposee()
    {
        if (Statut != StatutDemandeCopie.Deposee)
        {
            throw new DomainException($"Cette demande de copie a déjà été traitée (statut : {Statut}).");
        }
    }
}

/// <summary>Copie validée par le CPMT : traduite en <c>surveillance-medicale.copie-dossier-validee</c> (identifiants seulement).</summary>
public sealed record CopieDossierValideeLocal(Guid DemandeId, Guid PersonneId, DateTimeOffset OccurredAt) : IDomainEvent;
