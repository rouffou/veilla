using Sepp.BuildingBlocks.Domain;

namespace Sepp.Reintegration.Domain.Echanges;

/// <summary>Correspondants eHealth du trajet (SAN-64, §3.2).</summary>
public enum DestinataireEHealth
{
    MedecinTraitant,
    MedecinConseil,
    CoordinateurRetourAuTravail,
}

public enum SensEchange
{
    Sortant,
    Entrant,
}

public enum ObjetEchange
{
    /// <summary>SAN-63 : information du médecin-conseil après le seuil d'absences aux rendez-vous.</summary>
    NotificationAbsences,
    InformationTrajet,
    DemandeInformation,
    Reponse,
    Autre,
}

public enum StatutEchange
{
    AEnvoyer,
    Envoye,

    /// <summary>Échec technique : nouvelle tentative au prochain passage, ou relance manuelle.</summary>
    EnErreur,

    /// <summary>Nombre maximal de tentatives atteint : relance manuelle seulement (alerte).</summary>
    Abandonne,

    /// <summary>Compensation : le trajet a été annulé avant l'envoi.</summary>
    Annule,
    Recu,
}

/// <summary>
/// SAN-64 : échange sécurisé via eHealth avec le médecin traitant, le médecin-conseil ou le coordinateur Retour au travail.
/// Le contenu est une donnée médicale, chiffrée au repos (ARC-45) et jamais journalisée ; l'envoi au médecin traitant exige
/// l'accord du travailleur (§3.2). L'envoi passe par le port eHealth : en attente, puis envoyé ou en erreur (relançable).
/// </summary>
public sealed class EchangeEHealth : AggregateRoot
{
    public const int LongueurContenu = 20_000;

    private EchangeEHealth()
    {
    }

    private EchangeEHealth(
        Guid incapaciteId, Guid? trajetId, Guid personneId, DestinataireEHealth destinataire, SensEchange sens, ObjetEchange objet, string contenu,
        string? referenceDestinataire, bool accordTravailleur, DateTimeOffset maintenant) : base(NewId())
    {
        IncapaciteId = incapaciteId;
        TrajetId = trajetId;
        PersonneId = personneId;
        Destinataire = destinataire;
        Sens = sens;
        Objet = objet;
        Contenu = Texte.Obligatoire(contenu, "Le contenu de l'échange", LongueurContenu);
        ReferenceDestinataire = Texte.Facultatif(referenceDestinataire, "La référence du destinataire", 100);
        AccordTravailleur = accordTravailleur;
        CreeLe = maintenant;
    }

    public Guid IncapaciteId { get; private set; }

    public Guid? TrajetId { get; private set; }

    public Guid PersonneId { get; private set; }

    public DestinataireEHealth Destinataire { get; private set; }

    public SensEchange Sens { get; private set; }

    public ObjetEchange Objet { get; private set; }

    public string Contenu { get; private set; } = string.Empty;

    /// <summary>Identifiant eHealth du correspondant (numéro INAMI, organisme…), jamais un NISS.</summary>
    public string? ReferenceDestinataire { get; private set; }

    public bool AccordTravailleur { get; private set; }

    public StatutEchange Statut { get; private set; }

    /// <summary>Référence du message sur le canal eHealth (clé d'idempotence d'un message reçu).</summary>
    public string? ReferenceEHealth { get; private set; }

    /// <summary>Pour un message reçu : échange sortant auquel il répond.</summary>
    public Guid? EnReponseA { get; private set; }

    public int Tentatives { get; private set; }

    public string? CodeErreur { get; private set; }

    public DateTimeOffset CreeLe { get; private set; }

    public DateTimeOffset? EnvoyeLe { get; private set; }

    public static EchangeEHealth Preparer(
        Guid incapaciteId, Guid? trajetId, Guid personneId, DestinataireEHealth destinataire, ObjetEchange objet, string contenu,
        string? referenceDestinataire, bool accordTravailleur, DateTimeOffset maintenant)
    {
        if (!Enum.IsDefined(destinataire) || !Enum.IsDefined(objet))
        {
            throw new DomainException("Destinataire ou objet d'échange inconnu.");
        }

        // §3.2 : échanges avec le médecin traitant « avec accord du travailleur ». Pour le médecin-conseil et le coordinateur,
        // l'échange découle des obligations du trajet (SAN-63) : exigence d'accord à valider par le département juridique.
        if (destinataire == DestinataireEHealth.MedecinTraitant && !accordTravailleur)
        {
            throw new DomainException("L'échange avec le médecin traitant exige l'accord du travailleur (§3.2).");
        }

        return new EchangeEHealth(incapaciteId, trajetId, personneId, destinataire, SensEchange.Sortant, objet, contenu, referenceDestinataire, accordTravailleur, maintenant);
    }

    /// <summary>Message reçu en réponse à un échange sortant (référence eHealth unique : réception idempotente).</summary>
    public static EchangeEHealth Recevoir(EchangeEHealth origine, string referenceEHealth, string contenu, DateTimeOffset recuLe)
    {
        var recu = new EchangeEHealth(origine.IncapaciteId, origine.TrajetId, origine.PersonneId, origine.Destinataire, SensEchange.Entrant, ObjetEchange.Reponse,
            contenu, origine.ReferenceDestinataire, origine.AccordTravailleur, recuLe)
        {
            Statut = StatutEchange.Recu,
            ReferenceEHealth = Texte.Obligatoire(referenceEHealth, "La référence eHealth", 100),
            EnReponseA = origine.Id,
        };
        return recu;
    }

    public bool EstAEnvoyer => Sens == SensEchange.Sortant && Statut is StatutEchange.AEnvoyer or StatutEchange.EnErreur;

    public void MarquerEnvoye(string referenceEHealth, DateTimeOffset le)
    {
        Exiger(EstAEnvoyer, "L'échange n'est pas en attente d'envoi.");
        Tentatives++;
        Statut = StatutEchange.Envoye;
        ReferenceEHealth = Texte.Obligatoire(referenceEHealth, "La référence eHealth", 100);
        EnvoyeLe = le;
        CodeErreur = null;
    }

    public void MarquerEchec(string codeErreur, int tentativesMaximum)
    {
        Exiger(EstAEnvoyer, "L'échange n'est pas en attente d'envoi.");
        Tentatives++;
        CodeErreur = Texte.Obligatoire(codeErreur, "Le code d'erreur", 100);
        Statut = Tentatives >= tentativesMaximum ? StatutEchange.Abandonne : StatutEchange.EnErreur;
    }

    /// <summary>Relance manuelle d'un échange abandonné ou en erreur.</summary>
    public void Relancer()
    {
        Exiger(Sens == SensEchange.Sortant && Statut is StatutEchange.Abandonne or StatutEchange.EnErreur, "Seul un envoi en erreur ou abandonné se relance.");
        Statut = StatutEchange.AEnvoyer;
        Tentatives = 0;
    }

    /// <summary>Compensation : un envoi non encore effectué est annulé avec son trajet.</summary>
    public bool Annuler()
    {
        if (!EstAEnvoyer && Statut != StatutEchange.Abandonne)
        {
            return false;
        }

        Statut = StatutEchange.Annule;
        return true;
    }

    private static void Exiger(bool condition, string message)
    {
        if (!condition)
        {
            throw new DomainException(message);
        }
    }
}
