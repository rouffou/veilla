# Catalogue des événements d'intégration

Source de vérité : `src/Contracts/Sepp.Contracts`. Ce catalogue reprend le §15.4 du cahier des charges ; un test (`tests/Sepp.Contracts.Tests`) vérifie qu'il est à jour.

Règles :

- Une rubrique Service Bus par service producteur (préfixe du nom) ; le nom complet versionné est porté par la propriété `Subject` du message (ARC-34).
- Contenu limité aux identifiants, dates, statuts et catégories : aucune donnée clinique, psychosociale ou d'identité, aucun NISS (ARC-06, DAT-06). Vérifié automatiquement.
- Toute rupture de compatibilité crée une nouvelle version (`.v2`) publiée en parallèle de la précédente pendant au moins une version.
- Enveloppe commune : `eventId` (UUID v7, clé d'idempotence ARC-31), `occurredAt`, `correlationId` (ARC-47).
- **Exception : rubrique `audit`** (NF-04). Les traces d'accès aux données sensibles sont un contrat unique, `audit.acces-donnee-sensible`, publié par *tous* les services qui servent une lecture ou une modification de donnée sensible (via `IAuditTrail` du socle, écrit dans l'outbox du service) sur la rubrique dédiée `audit`, et consommé par le service Audit. Un contrat par producteur (`<service>.acces-donnee-sensible`) imposerait un type par service et un abonnement du service Audit à toutes les rubriques ; la rubrique partagée ne demande qu'un abonnement et, pour chaque service, un droit d'émission sur `audit`. Le service émetteur est porté par le champ `Service`, la zone de sensibilité (partition du journal) par `Zone`. Le motif est un texte libre court (300 caractères) qui ne doit contenir aucune donnée de santé ; l'alerte `audit.bris-de-glace-signale` ne le reprend pas.

| Producteur | Contrat | Classe | Champs | Consommateurs (§15.4) |
|---|---|---|---|---|
| affilies | `affilies.affilie-cree.v1` | AffilieCree | AffilieId, NumeroBce, CategorieTarifaire | Postes et risques, Prestations, Reporting |
| affilies | `affilies.affilie-modifie.v1` | AffilieModifie | AffilieId, NumeroBce, CategorieTarifaire, Statut | Postes et risques, Prestations, Reporting |
| audit (tous les services) | `audit.acces-donnee-sensible.v1` | AccesDonneeSensible | Service, Zone, UtilisateurId, Role, Action, ObjetType, ObjetId, Motif, BrisDeGlace (horodatage = `occurredAt`) | Audit (journal infalsifiable NF-04) |
| audit | `audit.bris-de-glace-signale.v1` | BrisDeGlaceSignale | EntreeAuditId, Zone, Service, UtilisateurId, ObjetType, ObjetId | Communications (alerte au CPMT dirigeant / CPAP dirigeant, §3.3) |
| bff-employeur | `bff-employeur.reprise-annoncee.v1` | RepriseAnnoncee | PersonneId, AffilieId, DateReprise, DebutAbsence | Réintégration, Obligations |
| documents | `documents.document-publie.v1` | DocumentPublie | DocumentId, Zone, TypeDestinataire, DestinataireId, CodeModele | Communications |
| integrations | `integrations.incapacite-notifiee.v1` | IncapaciteNotifiee | IncapaciteId, PersonneId, AffilieId, DateDebut, Source | Réintégration, Obligations |
| obligations | `obligations.obligation-creee.v1` | ObligationCreee | ObligationId, PersonneId, AffilieId, TypeExamen, DateDue, DateLimite | Planification, BFF, Reporting |
| obligations | `obligations.obligation-echue.v1` | ObligationEchue | ObligationId, PersonneId, AffilieId, TypeExamen, DateLimite | Planification, BFF, Reporting |
| personnes | `personnes.affectation-modifiee.v1` | AffectationModifiee | AffectationId, PersonneId, PosteId, DateDebut, DateFin | Obligations |
| personnes | `personnes.occupation-debutee.v1` | OccupationDebutee | OccupationId, PersonneId, AffilieId, DateDebut | Obligations, Planification, Reporting |
| personnes | `personnes.occupation-terminee.v1` | OccupationTerminee | OccupationId, PersonneId, AffilieId, DateFin | Obligations, Planification, Reporting |
| planification | `planification.rendez-vous-annule.v1` | RendezVousAnnule | RendezVousId, PersonneId, Motif | Communications, Obligations |
| planification | `planification.rendez-vous-planifie.v1` | RendezVousPlanifie | RendezVousId, PersonneId, AffilieId, Debut, ObligationIds | Communications, Obligations |
| postes-risques | `postes-risques.profil-risque-poste-modifie.v1` | ProfilRisquePosteModifie | PosteId, AffilieId, CodesRisques, ValideDu | Obligations, Reporting |
| prestations | `prestations.prestation-enregistree.v1` | PrestationEnregistree | PrestationId, AffilieId, Discipline, TypePrestation, Unites, Date | Reporting, Intégrations |
| prevention | `prevention.mesurage-enregistre.v1` | MesurageEnregistre | MesurageId, GroupeExpositionId, AffilieId, Agent, Niveau, Date | Surveillance médicale, Reporting |
| prevention | `prevention.mesure-prevention-creee.v1` | MesurePreventionCreee | MesureId, AffilieId, SourceType, Echeance | Reporting, BFF employeur |
| referentiels | `referentiels.jours-feries-modifies.v1` | JoursFeriesModifies | Annee | Services calculant des délais légaux (DAT-08) |
| referentiels | `referentiels.nomenclature-modifiee.v1` | NomenclatureModifiee | NomenclatureId, Code, Version | Services utilisant la nomenclature |
| referentiels | `referentiels.parametre-legal-modifie.v1` | ParametreLegalModifie | Code, Valeur, Unite, ValideDu, ValideJusquAu | Tous les services (politiques légales, ARC-21) |
| reintegration | `reintegration.trajet-demarre.v1` | TrajetDemarre | TrajetId, PersonneId, AffilieId, Initiateur, DateDemande | Intégrations, Prestations |
| reintegration | `reintegration.trajet-termine.v1` | TrajetTermine | TrajetId, PersonneId, AffilieId, Statut, DateFin | Intégrations, Prestations |
| surveillance-medicale | `surveillance-medicale.decision-emise.v1` | DecisionEmise | DecisionId, PersonneId, AffilieId, Categorie, CodesMesures, ValideJusquAu | Documents, Communications, Obligations |
| surveillance-medicale | `surveillance-medicale.examen-cloture.v1` | ExamenCloture | ExamenId, PersonneId, AffilieId, TypeExamen, Date | Obligations, Prestations |
| surveillance-medicale | `surveillance-medicale.vaccination-administree.v1` | VaccinationAdministree | VaccinationId, PersonneId, CodeVaccin, Dose, Date | Intégrations, Obligations |
