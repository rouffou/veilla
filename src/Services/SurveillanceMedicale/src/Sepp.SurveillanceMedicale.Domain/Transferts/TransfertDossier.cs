using Sepp.BuildingBlocks.Domain;

namespace Sepp.SurveillanceMedicale.Domain.Transferts;

public enum DirectionTransfert
{
    Sortant,
    Entrant,
}

public enum MotifTransfert
{
    /// <summary>Le travailleur change d'employeur, affilié à un autre SEPP ou doté d'un SIPP avec section médicale.</summary>
    ChangementEmployeur,

    /// <summary>Fusion, scission ou transfert d'affilié entre SEPP (AFF-06).</summary>
    OperationAffilie,

    /// <summary>Demande du travailleur.</summary>
    DemandeTravailleur,
}

public enum StatutTransfert
{
    Demande,
    Exporte,
    Transmis,
    Recu,
    Integre,
    Rejete,
}

/// <summary>
/// SAN-42 : transfert sécurisé du dossier de santé vers un autre SEPP ou SIPP, ou réception d'un dossier entrant.
/// Le paquet (export structuré du dossier) est chiffré avec les clés de la zone médicale (ARC-45) et accompagné de
/// son empreinte SHA-256 ; le canal d'échange réel entre services de prévention (eHealthBox ou autre) reste à confirmer.
/// </summary>
public sealed class TransfertDossier : AggregateRoot
{
    private TransfertDossier()
    {
    }

    private TransfertDossier(Guid id, Guid dossierId, Guid personneId, DirectionTransfert direction, string contrepartie, MotifTransfert motif, DateOnly date, string demandePar)
        : base(id)
    {
        DossierId = Garde.Identifiant(dossierId, "dossier");
        PersonneId = Garde.Identifiant(personneId, "personne");
        Direction = direction;
        Contrepartie = Garde.Code(contrepartie, "SEPP ou SIPP contrepartie");
        Motif = motif;
        DateDemande = date;
        DemandePar = Garde.Requis(demandePar, "demandé par", 100);
    }

    public Guid DossierId { get; private set; }

    public Guid PersonneId { get; private set; }

    public DirectionTransfert Direction { get; private set; }

    /// <summary>Identifiant du SEPP ou du SIPP (par ex. son numéro BCE).</summary>
    public string Contrepartie { get; private set; } = string.Empty;

    public MotifTransfert Motif { get; private set; }

    public StatutTransfert Statut { get; private set; }

    public DateOnly DateDemande { get; private set; }

    public string DemandePar { get; private set; } = string.Empty;

    /// <summary>Export structuré du dossier (JSON), chiffré (ARC-45).</summary>
    public string? Paquet { get; private set; }

    /// <summary>SHA-256 (hexadécimal) du paquet en clair : contrôle d'intégrité à la réception.</summary>
    public string? Empreinte { get; private set; }

    public string? ReferenceCanal { get; private set; }

    public DateOnly? DateTransmission { get; private set; }

    public DateOnly? DateIntegration { get; private set; }

    public static TransfertDossier Demander(Guid dossierId, Guid personneId, string contrepartie, MotifTransfert motif, DateOnly date, string demandePar) =>
        new(NewId(), dossierId, personneId, DirectionTransfert.Sortant, contrepartie, motif, date, demandePar) { Statut = StatutTransfert.Demande };

    public static TransfertDossier Recevoir(
        Guid dossierId, Guid personneId, string contrepartie, MotifTransfert motif, DateOnly date, string recuPar, string paquet, string empreinte, string referenceCanal)
    {
        var transfert = new TransfertDossier(NewId(), dossierId, personneId, DirectionTransfert.Entrant, contrepartie, motif, date, recuPar)
        {
            Statut = StatutTransfert.Recu,
            Paquet = Garde.Requis(paquet, "paquet", 10_000_000),
            Empreinte = Garde.Requis(empreinte, "empreinte", 64),
            ReferenceCanal = Garde.Requis(referenceCanal, "référence du canal", 200),
        };
        return transfert;
    }

    public void Exporter(string paquet, string empreinte)
    {
        if (Direction != DirectionTransfert.Sortant || Statut is not (StatutTransfert.Demande or StatutTransfert.Exporte))
        {
            throw new DomainException("Seul un transfert sortant non transmis peut être exporté.");
        }

        Paquet = Garde.Requis(paquet, "paquet", 10_000_000);
        Empreinte = Garde.Requis(empreinte, "empreinte", 64);
        Statut = StatutTransfert.Exporte;
    }

    public void MarquerTransmis(string referenceCanal, DateOnly date)
    {
        if (Statut != StatutTransfert.Exporte)
        {
            throw new DomainException("Le dossier doit être exporté avant sa transmission.");
        }

        ReferenceCanal = Garde.Requis(referenceCanal, "référence du canal", 200);
        DateTransmission = date;
        Statut = StatutTransfert.Transmis;
    }

    /// <summary>Le dossier reçu est versé au dossier local (partie « dossier reçu »), après contrôle d'intégrité.</summary>
    public void Integrer(DateOnly date)
    {
        if (Direction != DirectionTransfert.Entrant || Statut != StatutTransfert.Recu)
        {
            throw new DomainException("Seul un dossier reçu, non encore traité, peut être intégré.");
        }

        Statut = StatutTransfert.Integre;
        DateIntegration = date;
    }

    public void Rejeter()
    {
        if (Statut is StatutTransfert.Transmis or StatutTransfert.Integre)
        {
            throw new DomainException("Un transfert transmis ou intégré ne peut plus être rejeté.");
        }

        Statut = StatutTransfert.Rejete;
    }
}
