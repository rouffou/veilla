# Saga « examen de reprise du travail » — plan d'implémentation

Référence : rouffou/veilla#264 (TSK-SAGA-REPRISE), ARC-33, cahier des charges §14.6, §5. Plan de conception validé
pour implémentation par lots ; les points marqués **à valider** relèvent du métier (départements médical et juridique)
ou de l'architecte et ne doivent pas recevoir de valeur inventée.

## 0. Constats (code au 2026-10-07)

- Chorégraphie existante : Obligations crée `EXAMEN_REPRISE` depuis `RepriseLocale` (`SANTE.REPRISE.DELAI` = 10 jours
  ouvrables, `SANTE.REPRISE.ABSENCE_MINIMUM` = 4 semaines, `MoteurEcheances.cs`) ; Planification réserve un créneau
  d'urgence sur `ObligationCreee` (`RendezVousPlanifie` + `ConvocationEmise`, ou `UrgenceNonCouverte`) ; Surveillance
  médicale publie `ExamenCloture` puis `DecisionEmise` ; Documents génère les 3 exemplaires (`DocumentPublie`) ;
  Communications notifie.
- Écarts : aucun producteur de `RepriseAnnoncee` (le BFF n'a ni base ni outbox ; publier depuis le BFF violerait
  ARC-32) ; `RepriseAnnoncee` sans identifiant (pas d'annulation possible) ; `IncapaciteNotifiee` jamais publié ;
  `AbsenceRendezVousConstatee`, `RendezVousReplanifie`, `UrgenceNonCouverte` non consommés par Obligations ;
  `DecisionEmise` sans `ExamenId` ; Obligations ne publie aucune clôture d'obligation ; Communications ne publie rien
  (`Convocation.EnregistrerEnvoi` jamais appelé) et crée une convocation dès `RendezVousPlanifie` même si Planification
  a choisi de ne pas convoquer ; pas de messages planifiés dans le socle (pas de bus en local).
- Codes de type d'examen divergents : `ACTES_MEDICAUX_SUPPLEMENTAIRES` (Obligations, catalogue) vs
  `ACTES_SUPPLEMENTAIRES` (Surveillance médicale) ; `ESTIMATION_POTENTIEL_TRAVAIL` absent de Surveillance médicale ;
  `AUTRE_LEGISLATION` seulement dans Surveillance médicale ; `VISITE_PERIODIQUE` (inexistant) dans des tests de
  Planification et Communications ; `TypesUrgence` codé en dur dans Planification.
- Calendriers divergents : Planification (configuration) et Surveillance médicale (`Calendrier.Belge`) ne consomment
  pas `JoursFeriesModifies` ; `CloturerExamenHandler` recalcule « hors délai » avec un autre calendrier.
- Terraform (`infra/variables.tf`) : abonnements manquants — obligations ← integrations, reintegration ;
  planification ← referentiels, communications ; surveillance-medicale ← prevention, referentiels ;
  communications ← audit. Aucun filtre sur `Subject` (le catalogue réserve `personnes.etat-particulier-declare` à
  Obligations).
- Realm local : pas de claim `personne_id`, pas de clients `veilla-documents` / `veilla-communications`.
- Tests : tous les hôtes déclarent `public partial class Program` dans l'espace global (test multi-hôtes →
  `extern alias`) ; `Microsoft.Extensions.TimeProvider.Testing` absent de `Directory.Packages.props`.
- Hors saga : service Prestations inexistant (§14.6 étape 5).

## 1. Forme : orchestrateur persistant dans Obligations

Gestionnaire de processus persistant (agrégat `ProcessusReprise`) hébergé par **Obligations**, zone standard. Les
réactions chorégraphiées existantes restent (elles servent aussi hors saga).

- Pas de chorégraphie pure : ARC-33 exige orchestration, compensation et minuteries ; ADR 0004 prévoit des agrégats
  persistés dans le service orchestrateur ; POR-04 demande un suivi de statut.
- Obligations : possède le délai légal, le calendrier, l'obligation qui clôt la saga ; consomme déjà presque tout ; zone
  standard (la saga ne porte qu'identifiants, dates, statuts — ARC-06).
- Pas Réintégration (§15.3 : incapacité/trajet ; Lot 3 ; zone médicale injustifiée) ni nouveau service (ARC-01).
- **Déclencheur** : le BFF relaie en synchrone (ARC-30) `POST /api/v1/reprises` vers Obligations, qui persiste et
  publie par l'outbox. Écart à la lettre du §14.6 (« le BFF publie RepriseAnnoncee ») — **à valider par l'architecte**.
  `bff-employeur.reprise-annoncee.v1` reste consommé (rétrocompatibilité, origine `Evenement`).
- **Minuteries en base** (`prochaine_echeance`) traitées par un service d'arrière-plan `FOR UPDATE SKIP LOCKED` +
  `TimeProvider`, pas de messages planifiés Service Bus. ADR 0008 amende l'ADR 0004.

## 2. Étapes et événements

1. Annonce : portail → BFF `POST /api/v1/affilies/{affilieId}/reprises` → Obligations `POST /api/v1/reprises` (aussi
   depuis l'application interne). Création de `ProcessusReprise` (UUID v7), mise à jour de `RepriseLocale`, recalcul
   dans la même transaction. Absence ≥ 4 semaines → `ObligationCreee(EXAMEN_REPRISE)` ; sinon `ExamenNonRequis`.
   Publication `obligations.reprise-enregistree.v1`.
2. Planification : créneau d'urgence → `RendezVousPlanifie` + `ConvocationEmise`, ou `UrgenceNonCouverte`.
3. Convocation : Communications envoie → `communications.message-envoye.v1` / `message-abandonne.v1` ; Planification
   `Convocation.EnregistrerEnvoi` → `planification.convocation-envoyee.v1` / `convocation-non-remise.v1` ; Obligations
   passe l'obligation à `Convoque`.
4. Examen : `ExamenCloture` → obligation réalisée ; `DecisionEmise` porte `ExamenId` (ajout facultatif v1).
5. Formulaire : `DocumentPublie` porte `ObjetType`/`ObjetId` (ajout facultatif v1). Saga `Terminee` quand examen
   réalisé **et** décision émise.
6. Prestation : hors lot.

### Nouveaux contrats (ARC-06 : Guid, dates, booléens, codes)

| Contrat | Champs | Consommateurs |
|---|---|---|
| `obligations.reprise-enregistree.v1` | RepriseId, PersonneId, AffilieId, DateReprise, DebutAbsence, Origine (`PortailEmployeur`/`Interne`/`Evenement`), Statut (`Enregistree`/`Modifiee`/`Annulee`/`NonRequise`) | Réintégration (futur), Reporting, BFF |
| `obligations.obligation-cloturee.v1` | ObligationId, PersonneId, AffilieId, TypeExamen, Statut (`Realise`/`Annule`/`SortiEntreprise`), Motif?, Date | Planification, Surveillance médicale, Reporting |
| `obligations.planification-urgente-demandee.v1` | ObligationId, RendezVousId?, PersonneId, AffilieId, TypeExamen, DateDue, DateLimite, Motif (`Absence`/`AnnulationRendezVous`/`ConvocationNonRemise`) | Planification (émis seulement si replanification automatique activée) |
| `communications.message-envoye.v1` | MessageId, ObjetType, ObjetId, ReferenceOrigineId?, TypeMessage, Canal, Recommande, EnvoyeLe | Planification |
| `communications.message-abandonne.v1` | MessageId, ObjetType, ObjetId, ReferenceOrigineId?, TypeMessage, Canal, Recommande, CodeErreur, Date | Planification |
| `planification.convocation-envoyee.v1` | ConvocationId, RendezVousId, PersonneId, AffilieId, ObligationIds, Canal, Recommande, DateEnvoi | Obligations, Reporting |
| `planification.convocation-non-remise.v1` | ConvocationId, RendezVousId, PersonneId, AffilieId, ObligationIds, Canal, Recommande, Date | Obligations |

Ajouts compatibles v1 (paramètre facultatif en fin de constructeur, `null` par défaut) : `DecisionEmise(..., Guid?
ExamenId = null)`, `DocumentPublie(..., string? ObjetType = null, Guid? ObjetId = null)`.

Codes partagés : `src/Contracts/Sepp.Contracts/TypesExamen.cs` (constantes + `Connus`), source de vérité ; on retient
`ACTES_MEDICAUX_SUPPLEMENTAIRES`, on ajoute `ESTIMATION_POTENTIEL_TRAVAIL` et `AUTRE_LEGISLATION` au catalogue.

## 3. État, délais, compensations

Agrégat `Domain/Reprises/ProcessusReprise.cs`, table `processus_reprise` : id (RepriseId), personne_id, affilie_id,
date_reprise, debut_absence, origine, obligation_id, date_limite (recopiée, jamais recalculée), rendez_vous_id,
debut_rendez_vous, nombre_absences, convocation_envoyee_le, convocation_non_remise, urgence_non_couverte, examen_id,
examen_le, decision_id, decision_le, document_employeur_id, statut, hors_delai, annulee_le, motif_annulation,
prochaine_echeance, type_minuterie, audit DAT-03 + version. Unicité partielle (personne_id, affilie_id, date_reprise)
hors annulés ; index obligation_id, examen_id, prochaine_echeance.

- **Jalons idempotents**, statut dérivé : `Annoncee → ObligationOuverte → Planifiee | NonCouverte → Convoquee →
  ExamenRealise → DecisionEmise → Terminee` ; branches `ExamenNonRequis`, `Annulee`, `SansObjet` ; indicateur
  `EnRetard`/`hors_delai`. Ordre d'arrivée quelconque.
- Synchronisation depuis l'obligation : après `MiseAJourProjection.TerminerAsync`, `SynchronisationProcessusReprise`
  relit l'`Obligation` liée et l'applique (lien par la clé `REPRISE|affilie|date`, même transaction).
- `DecisionEmise` avant `ExamenCloture` : parquée dans la projection `decision_recue` (examen_id → decision_id) ;
  valeurs remplaçables : plus récent `OccurredAt` gagne ; annulation définitive.
- Idempotence : inbox ; annonce en double → même RepriseId (200) ; seul `DebutAbsence` différent → modification.
- Délais : date limite du moteur (recopiée). Ajouter `BusinessCalendar.SubtractBusinessDays`. Minuteries
  (TimeProvider, Europe/Brussels) : (a) alerte « échéance menacée » à N jours ouvrables
  (`SANTE.REPRISE.ALERTE_AVANT_ECHEANCE`, **à valider**) ; (b) lendemain de la date limite sans examen → `hors_delai`,
  `EnRetard` ; (c) rendez-vous passé d'1 jour ouvrable sans clôture → alerte ; (d) examen clôturé sans décision après N
  jours (**à valider**) ; (e) expiration administrative (**à valider**). Hôte
  `Adapters/Traitement/MinuteriesRepriseService.cs` (`Obligations:Reprise:IntervalleMinuteries`, 1 min), cas d'usage
  `TraiterMinuteriesReprise`. Alertes : `GET /api/v1/reprises?statut=&echeanceAvant=` et `GET /affilies/{id}/alertes`.

| Situation | Réaction |
|---|---|
| Annulation avant examen | `RepriseLocale.Annulee`, obligation `Annule(Recalcul)`, `ObligationCloturee(Annule)` ; Planification annule le rendez-vous futur qui ne couvre plus d'obligation ouverte (`ObligationLevee`) ; Communications prévient. Examen ouvert/clôturé → 409. |
| Changement de date | Annulation puis nouvelle obligation et réservation (**à valider**). |
| Rendez-vous annulé (≠ `ObligationLevee`) | Obligation à replanifier ; `planification-urgente-demandee(AnnulationRendezVous)` si mode automatique, sinon alerte. |
| Rendez-vous replanifié | Handler `RendezVousReplanifie` ; alerte si au-delà de la date limite. |
| Absence | Handler `AbsenceRendezVousConstatee` → `MarquerAbsent`, `nombre_absences++`, puis replanification auto ou alerte (**à valider**). |
| Convocation non remise | Jalon + alerte. |
| Urgence non couverte | Handler `UrgenceNonCouverte` → jalon `NonCouverte` + alerte. |
| Sortie de l'entreprise | Obligation `SortiEntreprise`, saga `SansObjet` ; annulation du rendez-vous **à valider**. |

## 4. Corrections au passage

1. Codes : `TypesExamen.cs` ; Surveillance médicale reprend les constantes + migration EF des lignes
   `ACTES_SUPPLEMENTAIRES` ; Planification `TypesUrgence` par constantes ; `VISITE_PERIODIQUE` → `EVALUATION_PERIODIQUE`
   dans les tests ; test Obligations `TypeObligation.Code()` ⊆ `TypesExamen.Connus` ; `CloturerExamenHandler` : hors
   délai = `examen.Date ∉ [DateDue, DateLimite]` de la projection `ObligationDue`.
2. Envoi effectif : Communications ne crée plus de convocation sur `RendezVousPlanifie` ; `Message.ReferenceOrigineId`
   (= ConvocationId) ; publie `message-envoye` / `message-abandonne` (outbox, même SaveChanges). Planification :
   handlers `MessageEnvoye`/`MessageAbandonne` (TypeMessage `ConvocationRendezVous`), `EnregistrerEnvoi` idempotent,
   `MarquerNonRemise`, publie `convocation-envoyee` / `convocation-non-remise`.
3. Terraform : abonnements manquants (§0) + `subject_filters = optional(map(list(string)), {})` →
   `azurerm_servicebus_subscription_rule` (`sys.Label IN (...)`), pour audit → communications et pour réserver
   `personnes.etat-particulier-declare.v1` à Obligations. Garde-fou : test d'architecture par service (abonnements DI ⊆
   `subscribes_to` de `infra/variables.tf`).
4. Keycloak local : clients `veilla-documents`, `veilla-communications`, mapper `personne_id` ; secrets de dev dans
   compose.

## 5. Lots

Ordre : lots 1 et 2 d'abord ; puis 3, 5, 6 en parallèle ; 4 après 3 ; 7 en dernier. Chaque service n'est touché que par
un lot ; fichiers partagés : contrats et catalogue (lot 1), `Security.cs` et `Directory.Packages.props` (lot 2),
`infra/*` et `deploy/*` (lot 7).

- **Lot 1 — Contrats, codes, ADR** : `docs/adr/0008-sagas-processus-persistants.md`, `TypesExamen.cs`,
  `Communications/CommunicationsEvents.cs`, nouveaux contrats Obligations/Planification, ajouts facultatifs
  `DecisionEmise`/`DocumentPublie`, catalogue, `docs/adr/README.md`. Tests : `ContractRulesTests` (ARC-06, désérialisation
  d'une charge v1 sans les nouveaux champs), codes `^[A-Z0-9_]+$` uniques.
- **Lot 2 — Socle** : `BusinessCalendar.SubtractBusinessDays` + tests ; `Microsoft.Extensions.TimeProvider.Testing` ;
  permissions `reprise:annoncer` (employeur, SIPP, gestionnaire, planificateur), `reprise:lire` (+ CPMT, infirmier,
  assistant médical), `reprise:gerer` (gestionnaire, planificateur) — répartition **à valider** (§3.3).
- **Lot 3 — Obligations (orchestrateur)** : `ProcessusReprise`, `PolitiqueSuiviReprise`, `RepriseUseCases`
  (`EnregistrerReprise`, `ModifierReprise`, `AnnulerReprise`, `ObtenirReprise`, `ListerReprises`),
  `SynchronisationProcessusReprise`, `TraiterMinuteriesReprise`, handlers (`ConvocationEnvoyee`, `ConvocationNonRemise`,
  `UrgenceNonCouverte`, `DecisionEmise`, `DocumentPublie`, `AbsenceRendezVousConstatee`, `RendezVousReplanifie`),
  `RepriseAnnonceeHandler` délègue à `EnregistrerReprise`, moteur ignore les reprises annulées, réconciliation absent →
  `MarquerAbsent`, `ObligationCloturee` publié, API `/reprises` (permissions + périmètre `affilie_id`), migration,
  `appsettings` (`ReplanificationAutomatique = false`), README. Tests domaine (permutations d'ordre, doublons,
  minuteries), application (faux TimeProvider), intégration (concurrence sur l'unicité, `SKIP LOCKED` parallèle).
- **Lot 4 — BFF et portail employeur (POR-04)** : routes `/affilies/{id}/reprises` (POST jamais rejoué, périmètre avant
  appel aval), front annonce + suivi. Tests de relais, refus hors périmètre, traduction 409/422.
- **Lot 5 — Planification et Communications** : §4.2, compensation sur `ObligationCloturee`,
  `PlanificationUrgenteDemandee`, `JoursFeriesModifies` dans Planification, `TypesUrgence` par constantes.
- **Lot 6 — Surveillance médicale et Documents** : codes + migration, `DecisionEmise.ExamenId`, hors délai depuis la
  projection, handler `ObligationCloturee`, `DocumentPublie.ObjetType = "decision"`/`ObjetId`. Tests de frontière et de
  rétrocompatibilité.
- **Lot 7 — Plateforme** : Terraform (abonnements, filtres), Keycloak, compose, garde-fous d'abonnement, projet
  `tests/Sepp.Sagas.Tests` (5 hôtes en `extern alias`, un PostgreSQL / 5 bases, `BusDeTest` routé comme Terraform,
  pompe d'outbox déterministe avec mode chaos, faux TimeProvider). Scénarios : nominal ; absence < 4 semaines ;
  annulation après planification ; absence au rendez-vous ; date limite dépassée ; ordre inversé et doublons ; aucun
  créneau.

## 6. Questions à faire valider

Juridique et médical : décompte du délai (jour de reprise, reprise un jour non ouvrable, borne des 4 semaines) ; nature
de l'absence (maladie, accident, accouchement — catégorie dans l'annonce ? donnée de santé ?) ; champ d'application
(tous les travailleurs ou seulement ceux sous surveillance) ; examen après la date limite ; absence à l'examen
(reconvocation automatique, tentatives, information de l'employeur) ; ce que voit l'employeur (POR-04) ; décision
reportée ; sortie de l'entreprise pendant le processus ; seuils d'alerte et expiration ; pré-reprise (POR-12) et
`IncapaciteNotifiee` (hors de ce plan) ; annonce par le travailleur.

Architecture : écart au §14.6 (déclencheur par API) ; suppression de la convocation sur `RendezVousPlanifie` ;
filtres Terraform et règle `$Default` (vérifier par `terraform plan`) ; migration tracée des codes en zone médicale ;
coût du test multi-hôtes en CI ; répartition des permissions de reprise.
