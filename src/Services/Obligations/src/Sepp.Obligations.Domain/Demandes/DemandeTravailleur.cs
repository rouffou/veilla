using Sepp.BuildingBlocks.Domain;

using Sepp.Obligations.Domain.Obligations;

namespace Sepp.Obligations.Domain.Demandes;

/// <summary>
/// Demande d'examen à l'initiative du travailleur, enregistrée par le SEPP (§5.1) : visite de pré-reprise pendant une
/// incapacité, ou consultation spontanée. Seuls le type et la date sont conservés, jamais le motif (ARC-06).
/// Elle déclenche une obligation à 10 jours ouvrables (SANTE.PRE_REPRISE.DELAI, SANTE.CONSULTATION_SPONTANEE.DELAI).
/// </summary>
public sealed class DemandeTravailleur : AggregateRoot
{
    private DemandeTravailleur()
    {
    }

    private DemandeTravailleur(Guid id, Guid personneId, Guid affilieId, TypeObligation type, DateOnly dateDemande, string enregistreePar) : base(id)
    {
        PersonneId = personneId;
        AffilieId = affilieId;
        Type = type;
        DateDemande = dateDemande;
        EnregistreePar = enregistreePar;
    }

    public Guid PersonneId { get; private set; }

    public Guid AffilieId { get; private set; }

    /// <summary><see cref="TypeObligation.VisitePreReprise"/> ou <see cref="TypeObligation.ConsultationSpontanee"/>.</summary>
    public TypeObligation Type { get; private set; }

    public DateOnly DateDemande { get; private set; }

    /// <summary>Identifiant (sub) de l'utilisateur qui a enregistré la demande.</summary>
    public string EnregistreePar { get; private set; } = string.Empty;

    public static DemandeTravailleur Enregistrer(Guid personneId, Guid affilieId, TypeObligation type, DateOnly dateDemande, DateOnly aujourdHui, string enregistreePar)
    {
        if (personneId == Guid.Empty || affilieId == Guid.Empty)
        {
            throw new DomainException("Le travailleur et l'affilié de la demande sont obligatoires.");
        }

        if (type is not (TypeObligation.VisitePreReprise or TypeObligation.ConsultationSpontanee))
        {
            throw new DomainException("Seules une visite de pré-reprise ou une consultation spontanée peuvent être demandées par le travailleur.");
        }

        if (dateDemande > aujourdHui)
        {
            throw new DomainException("La date de la demande ne peut pas être dans le futur.");
        }

        if (string.IsNullOrWhiteSpace(enregistreePar))
        {
            throw new DomainException("L'auteur de l'enregistrement est obligatoire.");
        }

        return new DemandeTravailleur(NewId(), personneId, affilieId, type, dateDemande, enregistreePar.Trim());
    }
}
