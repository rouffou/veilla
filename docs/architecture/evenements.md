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
| affilies | `affilies.operation-affilie-modifiee.v1` | OperationAffilieModifiee | OperationId, AffilieId, TypeOperation, Statut, DateEffet, AffilieAbsorbantId, AffiliesBeneficiaires, SeppContrepartie | Surveillance médicale (transfert des dossiers, SAN-42), Postes et risques, Prestations, Reporting (AFF-06) |
| audit (tous les services) | `audit.acces-donnee-sensible.v1` | AccesDonneeSensible | Service, Zone, UtilisateurId, Role, Action, ObjetType, ObjetId, Motif, BrisDeGlace (horodatage = `occurredAt`) | Audit (journal infalsifiable NF-04) |
| audit | `audit.bris-de-glace-signale.v1` | BrisDeGlaceSignale | EntreeAuditId, Zone, Service, UtilisateurId, ObjetType, ObjetId | Communications (alerte au CPMT dirigeant / CPAP dirigeant, §3.3) |
| bff-employeur | `bff-employeur.reprise-annoncee.v1` | RepriseAnnoncee | PersonneId, AffilieId, DateReprise, DebutAbsence | Réintégration, Obligations (conservé pour rétrocompatibilité, ADR 0008) |
| communications | `communications.message-abandonne.v1` | MessageAbandonne | MessageId, ObjetType, ObjetId, ReferenceOrigineId, TypeMessage, Canal, Recommande, CodeErreur, Date | Planification (`ConvocationRendezVous`) |
| communications | `communications.message-envoye.v1` | MessageEnvoye | MessageId, ObjetType, ObjetId, ReferenceOrigineId, TypeMessage, Canal, Recommande, EnvoyeLe | Planification (`ConvocationRendezVous`) |
| documents | `documents.document-publie.v1` | DocumentPublie | DocumentId, Zone, TypeDestinataire, DestinataireId, CodeModele, ObjetType?, ObjetId? | Communications, Obligations (saga de reprise) |
| integrations | `integrations.donnees-bce-recues.v1` | DonneesBceRecues | NumeroBce, AffilieId, Denomination, FormeJuridique, CodeNace, NumerosUnitesEtablissement, DateExtraction | Affiliés (consommateur à écrire, voir note ci-dessous) |
| integrations | `integrations.incapacite-notifiee.v1` | IncapaciteNotifiee | IncapaciteId, PersonneId, AffilieId, DateDebut, Source | Réintégration, Obligations |
| obligations | `obligations.obligation-cloturee.v1` | ObligationCloturee | ObligationId, PersonneId, AffilieId, TypeExamen, Statut, Motif?, Date | Planification, Surveillance médicale, Reporting |
| obligations | `obligations.obligation-creee.v1` | ObligationCreee | ObligationId, PersonneId, AffilieId, TypeExamen, DateDue, DateLimite | Planification, BFF, Reporting, Surveillance médicale (examens dus, SAN-20) |
| obligations | `obligations.obligation-echue.v1` | ObligationEchue | ObligationId, PersonneId, AffilieId, TypeExamen, DateLimite | Planification, BFF, Reporting |
| obligations | `obligations.planification-urgente-demandee.v1` | PlanificationUrgenteDemandee | ObligationId, RendezVousId?, PersonneId, AffilieId, TypeExamen, DateDue, DateLimite, Motif | Planification (émis seulement si la replanification automatique est activée) |
| obligations | `obligations.reprise-enregistree.v1` | RepriseEnregistree | RepriseId, PersonneId, AffilieId, DateReprise, DebutAbsence, Origine, Statut | Réintégration (futur), Reporting, BFF |
| personnes | `personnes.affectation-modifiee.v1` | AffectationModifiee | AffectationId, PersonneId, PosteId, DateDebut, DateFin | Obligations, Postes et risques, Surveillance médicale (SAN-20) |
| personnes | `personnes.etat-particulier-declare.v1` | EtatParticulierDeclare | EtatParticulierId, PersonneId, Categorie, DateDebut, DateFin | Obligations (voir note ARC-06 ci-dessous) |
| personnes | `personnes.occupation-debutee.v1` | OccupationDebutee | OccupationId, PersonneId, AffilieId, DateDebut | Obligations, Planification, Reporting |
| personnes | `personnes.occupation-terminee.v1` | OccupationTerminee | OccupationId, PersonneId, AffilieId, DateFin | Obligations, Planification, Reporting |
| planification | `planification.absence-rendez-vous-constatee.v1` | AbsenceRendezVousConstatee | RendezVousId, PersonneId, AffilieId, Debut, ObligationIds | Obligations (les obligations redeviennent à planifier), Communications (SAN-13) |
| planification | `planification.convocation-emise.v1` | ConvocationEmise | ConvocationId, RendezVousId, PersonneId, AffilieId, LieuId, TypeActe, Debut, Canal, Recommande, TypeConvocation, LotId | Communications (envoi, SAN-10, SAN-11) |
| planification | `planification.convocation-envoyee.v1` | ConvocationEnvoyee | ConvocationId, RendezVousId, PersonneId, AffilieId, ObligationIds, Canal, Recommande, DateEnvoi | Obligations (obligations « convoquées »), Reporting |
| planification | `planification.convocation-non-remise.v1` | ConvocationNonRemise | ConvocationId, RendezVousId, PersonneId, AffilieId, ObligationIds, Canal, Recommande, Date | Obligations (alerte ou replanification) |
| planification | `planification.rappel-rendez-vous-du.v1` | RappelRendezVousDu | RendezVousId, PersonneId, AffilieId, LieuId, Debut, Canal, NumeroRappel | Communications (rappels J-7 / J-1, SAN-13) |
| planification | `planification.rendez-vous-annule.v1` | RendezVousAnnule | RendezVousId, PersonneId, Motif | Communications, Obligations |
| planification | `planification.rendez-vous-planifie.v1` | RendezVousPlanifie | RendezVousId, PersonneId, AffilieId, Debut, ObligationIds | Communications, Obligations, Surveillance médicale (ouverture d'examen) |
| planification | `planification.rendez-vous-replanifie.v1` | RendezVousReplanifie | RendezVousId, PersonneId, AffilieId, AncienDebut, NouveauDebut, Motif | Communications, Reporting (PLA-07) |
| planification | `planification.urgence-non-couverte.v1` | UrgenceNonCouverte | ObligationId, PersonneId, AffilieId, TypeExamen, DateLimite | Communications (alerte au planificateur, PLA-06) |
| postes-risques | `postes-risques.liste-nominative-generee.v1` | ListeNominativeGeneree | ListeNominativeId, AffilieId, TypeListe, Version, DateReference, DateGeneration | Obligations (alerte de revue des listes, AFF-32) |
| postes-risques | `postes-risques.profil-risque-poste-modifie.v1` | ProfilRisquePosteModifie | PosteId, AffilieId, CodesRisques, ValideDu | Obligations, Reporting, Surveillance médicale (SAN-20) |
| postes-risques | `postes-risques.regle-surveillance-modifiee.v1` | RegleSurveillanceModifiee | RisqueId, CodeRisque, Categorie, Version, TypeSurveillance, FrequenceMois, SurveillanceProlongee, ValideDu | Obligations (SAN-01) |
| postes-risques | `postes-risques.surcharge-frequence-definie.v1` | SurchargeFrequenceDefinie | SurchargeId, AffilieId, CibleType, CibleId, CodeRisque, FrequenceMois, ValideDu, ValideJusquAu | Obligations (SAN-01) |
| prestations | `prestations.prestation-enregistree.v1` | PrestationEnregistree | PrestationId, AffilieId, Discipline, TypePrestation, Unites, Date | Reporting, Intégrations |
| prevention | `prevention.mesurage-enregistre.v1` | MesurageEnregistre | MesurageId, GroupeExpositionId, AffilieId, Agent, Niveau, Date | Surveillance médicale, Reporting |
| prevention | `prevention.mesure-prevention-creee.v1` | MesurePreventionCreee | MesureId, AffilieId, SourceType, Echeance | Reporting, BFF employeur |
| referentiels | `referentiels.jours-feries-modifies.v1` | JoursFeriesModifies | Annee, JoursSupplementaires | Obligations (délais en jours ouvrables, DAT-08), Surveillance médicale (délais de concertation et de recours, SAN-34), services calculant des délais légaux |
| referentiels | `referentiels.nomenclature-modifiee.v1` | NomenclatureModifiee | NomenclatureId, Code, Version | Services utilisant la nomenclature |
| referentiels | `referentiels.parametre-legal-modifie.v1` | ParametreLegalModifie | Code, Valeur, Unite, ValideDu, ValideJusquAu | Tous les services (politiques légales, ARC-21) |
| reintegration | `reintegration.trajet-demarre.v1` | TrajetDemarre | TrajetId, PersonneId, AffilieId, Initiateur, DateDemande | Intégrations, Prestations |
| reintegration | `reintegration.trajet-termine.v1` | TrajetTermine | TrajetId, PersonneId, AffilieId, Statut, DateFin | Intégrations, Prestations |
| surveillance-medicale | `surveillance-medicale.decision-emise.v1` | DecisionEmise | DecisionId, PersonneId, AffilieId, Categorie, CodesMesures, ValideJusquAu, ExamenId? | Documents, Communications, Obligations |
| surveillance-medicale | `surveillance-medicale.examen-cloture.v1` | ExamenCloture | ExamenId, PersonneId, AffilieId, TypeExamen, Date | Obligations, Prestations, Postes et risques |
| surveillance-medicale | `surveillance-medicale.vaccination-administree.v1` | VaccinationAdministree | VaccinationId, PersonneId, CodeVaccin, Dose, Date | Intégrations, Obligations |

## Notes par contrat

### `personnes.etat-particulier-declare.v1` (AFF-23, AFF-24, ARC-06)

Obligations doit savoir qu'une travailleuse bénéficie de la protection de la maternité pour déclencher l'examen et les mesures liées aux risques du poste, mais une grossesse ou un allaitement ne doit pas circuler en clair hors du service Personnes. Compromis retenu :

- le type exact (grossesse, allaitement, travail de nuit, jeune) reste chiffré dans la table `etat_particulier` du service Personnes (ARC-45) ;
- l'événement ne porte qu'une **catégorie générique** : `PROTECTION_MATERNITE` (grossesse et allaitement confondus, sans date présumée d'accouchement), `TRAVAIL_DE_NUIT` ou `JEUNE_TRAVAILLEUR`, et la période de protection (`DateDebut`, `DateFin` facultative, dernier jour inclus) ;
- l'événement est republié avec le même `EtatParticulierId` quand la période change (fin anticipée) : c'est l'état courant, que les consommateurs appliquent de façon idempotente ;
- l'abonnement à ce contrat sur la rubrique `personnes` est réservé au service Obligations (règle de filtre sur `Subject` côté Service Bus).

### `personnes.affectation-modifiee.v1` (AFF-22, DAT-04)

Publié à la création, à la clôture et au changement d'une affectation. `DateFin` est la borne **exclusive** de la période de validité (convention DAT-04 `[valid_from, valid_to[`), `null` pour une affectation en cours.

### `personnes.occupation-debutee.v1` / `personnes.occupation-terminee.v1` (AFF-20, AFF-23)

`AffilieId` est l'employeur déclarant (pour un intérimaire : l'agence d'intérim). L'entreprise utilisatrice est connue par les affectations aux postes, qui appartiennent à son catalogue. `DateFin` est le dernier jour d'occupation (inclus, comme DIMONA).

### `integrations.donnees-bce-recues.v1` (§12, AFF-01, AFF-02)

Publié par le service Intégrations quand une consultation de la BCE renvoie des données d'entreprise différentes de la précédente réception (première réception comprise) ; une consultation inchangée ne publie rien. Ce sont des données d'entreprise publiques, pas des données personnelles :

- `NumeroBce` et `NumerosUnitesEtablissement` sous leur forme canonique à dix chiffres ;
- `AffilieId` est renseigné si le numéro BCE correspond à un affilié connu (table `correspondance_identifiant` du service Intégrations, alimentée par `affilies.affilie-cree` / `affilies.affilie-modifie`), sinon `null` ;
- les adresses et dénominations des unités d'établissement ne voyagent pas dans l'événement (ARC-06 : identifiants, dates, statuts et catégories) ; le consommateur les lit par `GET /api/v1/bce/entreprises/{numeroBce}` du service Intégrations ;
- **consommateur à écrire côté Affiliés** : gestionnaire idempotent qui met à jour la fiche (dénomination, forme juridique, NACE) et les unités d'établissement de l'affilié, en passant par l'historique AFF-05 sous l'identité technique du service.

### `planification.*` (§8 PLA-01 à PLA-09, §5.3 SAN-10 à SAN-13, ARC-06)

Aucun de ces contrats ne porte de contenu de message, de donnée de santé ni d'identité : identifiants, horaires, catégories et codes.

- **`planification.convocation-emise.v1`** (SAN-10, SAN-11) : une convocation est à envoyer ; le service Communications l'envoie par le `Canal` indiqué (`Courrier`, `Email`, `Sms`, `Portail`), en recommandé si `Recommande` (recommandé papier par `Courrier`, électronique par `Email` ; un canal SMS ou portail est remplacé par `Courrier` quand le recommandé est exigé), après lecture des coordonnées de la personne auprès du service Personnes. `TypeActe` est une catégorie (même espace de codes que `TypeExamen` des obligations), `TypeConvocation` vaut `Convocation`, `Reconvocation` (après une absence, SAN-13) ou `Replanification` (rendez-vous déplacé, PLA-07), `LotId` regroupe les convocations émises par lot.
- **`planification.rappel-rendez-vous-du.v1`** (SAN-13) : `NumeroRappel` 1 (J-7 par défaut) ou 2 (J-1) selon les paramètres légaux `CONVOCATION.RAPPEL_1` et `CONVOCATION.RAPPEL_2` (jours calendrier, ARC-21). Un rappel n'est émis qu'une fois par rendez-vous (idempotent, noté sur le rendez-vous) ; un rendez-vous pris après la date du rappel n'en reçoit pas (la convocation vient de partir). Le canal est celui de la dernière convocation.
- **`planification.rendez-vous-replanifie.v1`** (PLA-07) : même `RendezVousId`, nouvel horaire ; `Motif` est un code (`AbsenceRessource`). Une `ConvocationEmise` de type `Replanification` part en parallèle : c'est elle qui notifie la personne.
- **`planification.rendez-vous-annule.v1`** : `Motif` est un code (`DemandeTravailleur`, `DemandeEmployeur`, `AbsenceRessource`, `ObligationLevee`, `Autre`), jamais un texte libre.
- **`planification.absence-rendez-vous-constatee.v1`** (SAN-13) : les obligations couvertes redeviennent à planifier ; la reconvocation est une action du planificateur (`POST /api/v1/rendez-vous/{id}/reconvocation`).
- **`planification.urgence-non-couverte.v1`** (PLA-06) : aucun créneau disponible avant l'échéance légale (10 jours ouvrables par défaut, jours fériés belges exclus) ; une alerte par obligation.
- Consommés par Planification : `obligations.obligation-creee`, `obligations.obligation-echue` (projection des obligations à planifier ; un type d'examen urgent déclenche la réservation immédiate d'un créneau d'urgence, saga de reprise §14.6 étape 3) et `referentiels.parametre-legal-modifie` (délais d'urgence `SANTE.*.DELAI`, rappels `CONVOCATION.RAPPEL_*`).
### `referentiels.jours-feries-modifies.v1` (DAT-08, ARC-34)
`JoursSupplementaires` est l'**état complet** des jours fériés supplémentaires de l'année (jours de remplacement, fêtes des Communautés), en plus des dix jours fériés légaux que chaque service calcule (`BelgianPublicHolidays`). Les services qui calculent des délais en jours ouvrables (Obligations) tiennent ainsi leur calendrier à jour sans appel synchrone. Champ ajouté de façon compatible : facultatif (`null` pour un producteur antérieur, ignoré par le consommateur), il ne change pas la version du contrat.
### `postes-risques.liste-nominative-generee.v1` (AFF-30, AFF-31, AFF-32)
Publié par Postes et risques à chaque nouvelle version d'une liste nominative (génération ou validation d'une proposition de modification par le CPMT). Le service Obligations n'en retient que la dernière version par type de liste pour l'alerte « liste non revue depuis 12 mois » (paramètre légal `SANTE.LISTES_NOMINATIVES.REVUE_ALERTE`). Le contenu de la liste (travailleurs, postes) ne voyage pas.
### `obligations.*` (SAN-01, SAN-04)
`TypeExamen` porte le code du type d'obligation, même vocabulaire que `surveillance-medicale.examen-cloture.v1` : `EVALUATION_PREALABLE`, `EVALUATION_PERIODIQUE`, `ACTES_MEDICAUX_SUPPLEMENTAIRES`, `EXAMEN_REPRISE`, `VISITE_PRE_REPRISE`, `CONSULTATION_SPONTANEE`, `PROTECTION_MATERNITE`, `SURVEILLANCE_PROLONGEE`, `ESTIMATION_POTENTIEL_TRAVAIL`, `EVALUATION_REINTEGRATION`. `ObligationCreee` est publié à la création d'une obligation et quand elle redevient due après avoir été annulée par un recalcul ; `ObligationEchue` une seule fois par dépassement de la date limite (une nouvelle date limite après recalcul permet un nouveau signalement). Les types `PROTECTION_MATERNITE`, `CONSULTATION_SPONTANEE` et `VISITE_PRE_REPRISE` révèlent une démarche ou un état du travailleur : leurs consommateurs externes à l'équipe de surveillance (portail employeur, SIPP) ne doivent pas les afficher.
### Codes de type d'examen
Source de vérité : `src/Contracts/Sepp.Contracts/TypesExamen.cs` (classe `Sepp.Contracts.Examens.TypesExamen` : constantes et liste `Connus` ; espace de noms dédié pour éviter la collision avec `TypesExamen` du domaine de Surveillance médicale, remplacé au lot 6). Les codes sont ceux d'Obligations (`TypeObligation.Code()`) : `EVALUATION_PREALABLE`, `EVALUATION_PERIODIQUE`, `ACTES_MEDICAUX_SUPPLEMENTAIRES`, `EXAMEN_REPRISE`, `VISITE_PRE_REPRISE`, `CONSULTATION_SPONTANEE`, `PROTECTION_MATERNITE`, `SURVEILLANCE_PROLONGEE`, `ESTIMATION_POTENTIEL_TRAVAIL`, `EVALUATION_REINTEGRATION`, auxquels s'ajoute `AUTRE_LEGISLATION` (Surveillance médicale). Ils sont utilisés par `TypeExamen` (`obligations.obligation-creee`, `obligations.obligation-cloturee`, `surveillance-medicale.examen-cloture`…) et `TypeActe` (`planification.convocation-emise`). Chaque service les reprend par ces constantes ; `ACTES_SUPPLEMENTAIRES` (ancien code de Surveillance médicale) est remplacé par `ACTES_MEDICAUX_SUPPLEMENTAIRES`.
### `obligations.reprise-enregistree.v1`, `obligations.obligation-cloturee.v1`, `obligations.planification-urgente-demandee.v1` (ARC-33, POR-04, ADR 0008)
Publiés par le processus de reprise du service Obligations. `RepriseId` identifie le processus. `Origine` : `PortailEmployeur`, `Interne`, `Evenement` ; `Statut` de la reprise : `Enregistree`, `Modifiee`, `Annulee`, `NonRequise`. `ObligationCloturee.Statut` : `Realise`, `Annule`, `SortiEntreprise` (`Motif` : code facultatif). `PlanificationUrgenteDemandee.Motif` : `Absence`, `AnnulationRendezVous`, `ConvocationNonRemise` ; émis seulement si la replanification automatique est activée. `bff-employeur.reprise-annoncee.v1` est conservé pour rétrocompatibilité.
### `communications.message-envoye.v1`, `communications.message-abandonne.v1`, `planification.convocation-envoyee.v1`, `planification.convocation-non-remise.v1` (SAN-10, DOC-05)
Boucle de retour de l'envoi d'une convocation : Communications publie le sort du message (`ReferenceOrigineId` = `ConvocationId`, `TypeMessage` = `ConvocationRendezVous`), Planification enregistre l'envoi ou la non-remise sur la convocation et le publie pour Obligations. Aucun contenu de message (ARC-06).
### `surveillance-medicale.decision-emise.v1`, `documents.document-publie.v1` : champs facultatifs
`DecisionEmise.ExamenId` et `DocumentPublie.ObjetType`/`ObjetId` (`decision` et `DecisionId`) sont ajoutés de façon compatible : facultatifs (`null` pour un producteur antérieur), sans changement de version.
