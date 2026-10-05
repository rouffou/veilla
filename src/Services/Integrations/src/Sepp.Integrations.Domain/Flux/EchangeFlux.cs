using Sepp.BuildingBlocks.Domain;

namespace Sepp.Integrations.Domain.Flux;

/// <summary>Flux externe couvert par la couche d'intégration (§12, Lot 1).</summary>
public enum TypeFlux
{
    /// <summary>Banque-Carrefour des Entreprises : entreprises et unités d'établissement.</summary>
    Bce,

    /// <summary>DIMONA / DmfA via la BCSS : entrées et sorties des travailleurs des affiliés (AFF-20).</summary>
    Dimona,

    /// <summary>BCSS — identification (registre national) et mutations.</summary>
    RegistreNational,
}

public enum SensFlux
{
    Entrant,
    Sortant,
}

/// <summary>Statut d'un échange dans le tableau de suivi des flux (INT-02).</summary>
public enum StatutEchange
{
    /// <summary>Reçu et journalisé, en attente de traitement.</summary>
    Recu,

    /// <summary>Traité avec succès (effet appliqué dans le service propriétaire).</summary>
    Traite,

    /// <summary>Refusé pour une raison métier (données invalides, employeur inconnu…) ; relançable après correction.</summary>
    Rejete,

    /// <summary>Échec technique (service indisponible, délai dépassé…) ; relançable.</summary>
    EnErreur,
}

/// <summary>
/// Échange avec un système externe, journalisé dans <c>journal_flux</c> (§12 « journalisée et rejouable », INT-02).
/// La charge utile (format canonique interne) est conservée pour permettre la relance, chiffrée au repos (ARC-45),
/// puis purgée après traitement réussi ou à l'échéance de conservation. Le message d'erreur ne contient aucune
/// donnée personnelle : toute suite de chiffres ayant la forme d'un NISS est masquée (DAT-06).
/// </summary>
public sealed class EchangeFlux : AggregateRoot
{
    public const int LongueurMaximaleMessage = 500;

    private EchangeFlux()
    {
    }

    private EchangeFlux(Guid id, TypeFlux flux, SensFlux sens, string typeMessage, string cleIdempotence, string? referenceExterne,
        int nombreEnregistrements, string chargeUtile, DateTimeOffset recuLe) : base(id)
    {
        Flux = flux;
        Sens = sens;
        TypeMessage = typeMessage;
        CleIdempotence = cleIdempotence;
        ReferenceExterne = referenceExterne;
        NombreEnregistrements = nombreEnregistrements;
        ChargeUtile = chargeUtile;
        RecuLe = recuLe;
        Statut = StatutEchange.Recu;
    }

    public TypeFlux Flux { get; private set; }

    public SensFlux Sens { get; private set; }

    /// <summary>Nature du message dans le flux (ex. <c>dimona.entree</c>, <c>bce.entreprise</c>).</summary>
    public string TypeMessage { get; private set; } = string.Empty;

    /// <summary>Clé unique par flux : une même réception n'est journalisée qu'une fois (idempotence de la réception).</summary>
    public string CleIdempotence { get; private set; } = string.Empty;

    /// <summary>Référence de l'organisme (référence DIMONA, numéro BCE…), jamais un NISS.</summary>
    public string? ReferenceExterne { get; private set; }

    public DateTimeOffset RecuLe { get; private set; }

    public DateTimeOffset? TraiteLe { get; private set; }

    public DateTimeOffset? DerniereTentativeLe { get; private set; }

    public StatutEchange Statut { get; private set; }

    public int NombreEnregistrements { get; private set; }

    public string? CodeErreur { get; private set; }

    public string? MessageErreur { get; private set; }

    /// <summary>Nombre de tentatives de traitement (premier traitement et relances).</summary>
    public int Tentatives { get; private set; }

    /// <summary>Message au format canonique, chiffré en base ; <c>null</c> une fois purgé.</summary>
    public string? ChargeUtile { get; private set; }

    public DateTimeOffset? ChargeUtilePurgeeLe { get; private set; }

    public bool ChargeUtileDisponible => ChargeUtile is not null;

    public static EchangeFlux Recevoir(TypeFlux flux, SensFlux sens, string typeMessage, string cleIdempotence, string? referenceExterne,
        int nombreEnregistrements, string chargeUtile, DateTimeOffset recuLe)
    {
        if (!Enum.IsDefined(flux) || !Enum.IsDefined(sens))
        {
            throw new DomainException("Flux ou sens inconnu.");
        }

        if (nombreEnregistrements < 0)
        {
            throw new DomainException("Le nombre d'enregistrements ne peut pas être négatif.");
        }

        if (string.IsNullOrWhiteSpace(chargeUtile))
        {
            throw new DomainException("La charge utile d'un échange est obligatoire.");
        }

        return new EchangeFlux(NewId(), flux, sens, Texte.Obligatoire(typeMessage, "Le type de message", 50),
            Texte.Obligatoire(cleIdempotence, "La clé d'idempotence", 200), Texte.Facultatif(referenceExterne, "La référence externe", 100),
            nombreEnregistrements, chargeUtile, recuLe);
    }

    /// <summary>Un échange se (re)traite tant qu'il n'est pas traité et que sa charge utile est disponible.</summary>
    public bool EstATraiter => Statut != StatutEchange.Traite && ChargeUtileDisponible;

    /// <summary>Début d'une tentative de traitement (premier passage ou relance manuelle, INT-02).</summary>
    public void DebuterTentative(DateTimeOffset maintenant)
    {
        if (Statut == StatutEchange.Traite)
        {
            throw new DomainException("L'échange est déjà traité.");
        }

        if (!ChargeUtileDisponible)
        {
            throw new DomainException("La charge utile de l'échange a été purgée : il ne peut plus être rejoué.");
        }

        Tentatives++;
        DerniereTentativeLe = maintenant;
    }

    public void MarquerTraite(int nombreEnregistrements, DateTimeOffset maintenant)
    {
        if (nombreEnregistrements < 0)
        {
            throw new DomainException("Le nombre d'enregistrements ne peut pas être négatif.");
        }

        Statut = StatutEchange.Traite;
        NombreEnregistrements = nombreEnregistrements;
        TraiteLe = maintenant;
        CodeErreur = null;
        MessageErreur = null;
    }

    public void Rejeter(string code, string message, DateTimeOffset maintenant) =>
        Echouer(StatutEchange.Rejete, code, message, maintenant);

    public void MarquerEnErreur(string code, string message, DateTimeOffset maintenant) =>
        Echouer(StatutEchange.EnErreur, code, message, maintenant);

    /// <summary>Supprime la charge utile (minimisation, RGPD) ; l'entrée du journal est conservée.</summary>
    public bool PurgerChargeUtile(DateTimeOffset maintenant)
    {
        if (!ChargeUtileDisponible)
        {
            return false;
        }

        ChargeUtile = null;
        ChargeUtilePurgeeLe = maintenant;
        return true;
    }

    private void Echouer(StatutEchange statut, string code, string message, DateTimeOffset maintenant)
    {
        Statut = statut;
        TraiteLe = maintenant;
        var codeNormalise = string.IsNullOrWhiteSpace(code) ? "integrations.erreur-inconnue" : code.Trim();
        CodeErreur = Texte.Tronquer(DonneesPersonnelles.Masquer(codeNormalise), 100);
        MessageErreur = Texte.Tronquer(DonneesPersonnelles.Masquer(string.IsNullOrWhiteSpace(message) ? codeNormalise : message.Trim()), LongueurMaximaleMessage);
    }
}
