namespace Sepp.Contracts.Affilies
{
    [EventContract("affilies.affilie-cree", 1)]
    public sealed record AffilieCree(Guid AffilieId, string NumeroBce, string CategorieTarifaire) : IntegrationEvent;

    [EventContract("affilies.affilie-modifie", 1)]
    public sealed record AffilieModifie(Guid AffilieId, string NumeroBce, string CategorieTarifaire, string Statut) : IntegrationEvent;
}

namespace Sepp.Contracts.Personnes
{
    [EventContract("personnes.occupation-debutee", 1)]
    public sealed record OccupationDebutee(Guid OccupationId, Guid PersonneId, Guid AffilieId, DateOnly DateDebut) : IntegrationEvent;

    [EventContract("personnes.occupation-terminee", 1)]
    public sealed record OccupationTerminee(Guid OccupationId, Guid PersonneId, Guid AffilieId, DateOnly DateFin) : IntegrationEvent;

    [EventContract("personnes.affectation-modifiee", 1)]
    public sealed record AffectationModifiee(Guid AffectationId, Guid PersonneId, Guid PosteId, DateOnly DateDebut, DateOnly? DateFin) : IntegrationEvent;

    /// <summary>
    /// AFF-23, AFF-24 : un état particulier ouvre une protection ou une surveillance (examen, mesures liées aux
    /// risques du poste). Seule une catégorie générique sort du service (<c>PROTECTION_MATERNITE</c>,
    /// <c>TRAVAIL_DE_NUIT</c>, <c>JEUNE_TRAVAILLEUR</c>) : jamais « grossesse » ni « allaitement » en clair (ARC-06).
    /// Publié à la déclaration puis à chaque changement de période (état courant, clé : EtatParticulierId).
    /// </summary>
    [EventContract("personnes.etat-particulier-declare", 1)]
    public sealed record EtatParticulierDeclare(Guid EtatParticulierId, Guid PersonneId, string Categorie, DateOnly DateDebut, DateOnly? DateFin) : IntegrationEvent;
}

namespace Sepp.Contracts.PostesRisques
{
    [EventContract("postes-risques.profil-risque-poste-modifie", 1)]
    public sealed record ProfilRisquePosteModifie(Guid PosteId, Guid AffilieId, IReadOnlyList<string> CodesRisques, DateOnly ValideDu) : IntegrationEvent;
}

namespace Sepp.Contracts.Obligations
{
    [EventContract("obligations.obligation-creee", 1)]
    public sealed record ObligationCreee(Guid ObligationId, Guid PersonneId, Guid AffilieId, string TypeExamen, DateOnly DateDue, DateOnly? DateLimite) : IntegrationEvent;

    [EventContract("obligations.obligation-echue", 1)]
    public sealed record ObligationEchue(Guid ObligationId, Guid PersonneId, Guid AffilieId, string TypeExamen, DateOnly DateLimite) : IntegrationEvent;
}

namespace Sepp.Contracts.Planification
{
    [EventContract("planification.rendez-vous-planifie", 1)]
    public sealed record RendezVousPlanifie(Guid RendezVousId, Guid PersonneId, Guid AffilieId, DateTimeOffset Debut, IReadOnlyList<Guid> ObligationIds) : IntegrationEvent;

    [EventContract("planification.rendez-vous-annule", 1)]
    public sealed record RendezVousAnnule(Guid RendezVousId, Guid PersonneId, string Motif) : IntegrationEvent;
}

namespace Sepp.Contracts.SurveillanceMedicale
{
    /// <summary>Clôture d'un examen : type et date uniquement, jamais le contenu clinique (ARC-06).</summary>
    [EventContract("surveillance-medicale.examen-cloture", 1)]
    public sealed record ExamenCloture(Guid ExamenId, Guid PersonneId, Guid AffilieId, string TypeExamen, DateOnly Date) : IntegrationEvent;

    /// <summary>
    /// Décision d'évaluation de santé : seules la catégorie et les mesures sortent de la zone médicale (§2.1, ARC-06).
    /// <c>ExamenId</c> relie la décision à l'examen clôturé (saga « examen de reprise », ARC-33) ; champ ajouté de façon
    /// compatible (ARC-34) : facultatif, <c>null</c> pour un producteur antérieur, ignoré par les consommateurs antérieurs.
    /// </summary>
    [EventContract("surveillance-medicale.decision-emise", 1)]
    public sealed record DecisionEmise(Guid DecisionId, Guid PersonneId, Guid AffilieId, string Categorie, IReadOnlyList<string> CodesMesures, DateOnly? ValideJusquAu, Guid? ExamenId = null) : IntegrationEvent;

    [EventContract("surveillance-medicale.vaccination-administree", 1)]
    public sealed record VaccinationAdministree(Guid VaccinationId, Guid PersonneId, string CodeVaccin, int Dose, DateOnly Date) : IntegrationEvent;
}

namespace Sepp.Contracts.Reintegration
{
    [EventContract("reintegration.trajet-demarre", 1)]
    public sealed record TrajetDemarre(Guid TrajetId, Guid PersonneId, Guid AffilieId, string Initiateur, DateOnly DateDemande) : IntegrationEvent;

    [EventContract("reintegration.trajet-termine", 1)]
    public sealed record TrajetTermine(Guid TrajetId, Guid PersonneId, Guid AffilieId, string Statut, DateOnly DateFin) : IntegrationEvent;
}

namespace Sepp.Contracts.Integrations
{
    /// <summary>Notification d'incapacité reçue de l'employeur ou du médecin-conseil (SAN-60).</summary>
    [EventContract("integrations.incapacite-notifiee", 1)]
    public sealed record IncapaciteNotifiee(Guid IncapaciteId, Guid PersonneId, Guid AffilieId, DateOnly DateDebut, string Source) : IntegrationEvent;
}

namespace Sepp.Contracts.BffEmployeur
{
    /// <summary>
    /// Reprise annoncée par l'employeur sur le portail (§14.6). Conservé pour rétrocompatibilité (ADR 0008) : le
    /// déclencheur de la saga « examen de reprise » est désormais l'appel synchrone du BFF à l'API d'Obligations, qui publie
    /// <c>obligations.reprise-enregistree</c> ; le consommateur d'Obligations continue d'accepter cet événement (origine
    /// <c>Evenement</c>). Sans identifiant : pas d'annulation possible par ce contrat.
    /// </summary>
    [EventContract("bff-employeur.reprise-annoncee", 1)]
    public sealed record RepriseAnnoncee(Guid PersonneId, Guid AffilieId, DateOnly DateReprise, DateOnly DebutAbsence) : IntegrationEvent;
}

namespace Sepp.Contracts.Prevention
{
    [EventContract("prevention.mesurage-enregistre", 1)]
    public sealed record MesurageEnregistre(Guid MesurageId, Guid GroupeExpositionId, Guid AffilieId, string Agent, string Niveau, DateOnly Date) : IntegrationEvent;

    [EventContract("prevention.mesure-prevention-creee", 1)]
    public sealed record MesurePreventionCreee(Guid MesureId, Guid AffilieId, string SourceType, DateOnly? Echeance) : IntegrationEvent;
}

namespace Sepp.Contracts.Prestations
{
    [EventContract("prestations.prestation-enregistree", 1)]
    public sealed record PrestationEnregistree(Guid PrestationId, Guid AffilieId, string Discipline, string TypePrestation, decimal Unites, DateOnly Date) : IntegrationEvent;
}

namespace Sepp.Contracts.Documents
{
    /// <summary>
    /// Document publié. <c>ObjetType</c>/<c>ObjetId</c> désignent l'objet métier dont le document est issu (par exemple
    /// <c>decision</c> et le <c>DecisionId</c>, saga « examen de reprise », ARC-33) ; champs ajoutés de façon compatible
    /// (ARC-34) : facultatifs, <c>null</c> pour un producteur antérieur, ignorés par les consommateurs antérieurs.
    /// </summary>
    [EventContract("documents.document-publie", 1)]
    public sealed record DocumentPublie(Guid DocumentId, string Zone, string TypeDestinataire, Guid DestinataireId, string CodeModele, string? ObjetType = null, Guid? ObjetId = null) : IntegrationEvent;
}
