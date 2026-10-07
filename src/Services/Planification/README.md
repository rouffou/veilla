# Service Planification

Agendas, créneaux, rendez-vous, convocations et tournées (cahier des charges §5.3 SAN-10 à SAN-13, §8 PLA-01 à PLA-09,
§15.3 « Planification », §14.6 saga de reprise). Base `planification`, port local 5117, espace de noms `Sepp.Planification`.

## Exigences couvertes

| Exigence | Réalisation |
|---|---|
| PLA-01 ressources et compétences | `Ressource` (conseiller, infirmier, assistant, salle, cabine, appareil, unité mobile, chauffeur) ; compétences = codes de types d'acte ; `POST /api/v1/ressources`, `PUT …/competences` |
| PLA-02 lieux | `Lieu` (centre fixe, cabinet en entreprise, unité mobile, distance) avec code postal et position ; `POST /api/v1/lieux` |
| PLA-03 modèles d'agenda | `ModeleAgenda` historisé (DAT-04, un nouveau modèle clôture le précédent), plages par jour et type d'acte, durées standard globales ou par CPMT (`DureeStandard`, la durée du CPMT l'emporte) ; génération idempotente des créneaux hors week-ends et jours fériés ; le CPMT ne gère que sa propre ressource |
| PLA-03 congés RH (lecture seule) | Port `IOutilRh` (`LireCongesAsync`, aucune écriture) ; simulateur configurable ; import idempotent (`POST /api/v1/conges/imports`) qui bloque les créneaux libres et signale les créneaux déjà réservés |
| PLA-04 propositions de sessions | `PlanificateurSessions` : regroupement par personne (un rendez-vous couvre toutes ses obligations, SAN-03) puis par site, ordre de visite du plus proche voisin (haversine), une session par jour ouvrable, signalement « hors délai » ; la méthode est décrite dans la réponse de `GET /api/v1/sessions/propositions` ; `POST /api/v1/sessions/{id}/planification` remplit la session et convoque par lot |
| PLA-05 tournées | `Session` : unité mobile, chauffeur, capacité journalière, itinéraire (emplacements et raccordements) ; les créneaux mobilisent conseiller, unité et chauffeur |
| PLA-06 créneaux d'urgence | Créneaux réservés aux urgences ; échéance = 10 jours ouvrables belges (paramètres `SANTE.*.DELAI`, `BusinessCalendar`) ; réservation automatique à la réception de `obligations.obligation-creee` pour les types urgents ; sans créneau : `planification.urgence-non-couverte` ; un créneau d'urgence libre à moins de 24 h s'ouvre aux réservations ordinaires |
| PLA-07 replanification en masse | `POST /api/v1/replanifications` : déplace les rendez-vous de la ressource absente dans le même lieu (avant la date limite si possible), publie `rendez-vous-replanifie` et une convocation de type `Replanification` ; sans solution : annulation (motif `AbsenceRessource`) |
| PLA-08 salle d'attente | Arrivée (le jour du rendez-vous), appel en salle ou cabine, fin ; file ordonnée par heure prévue puis d'arrivée ; identifiants uniquement (le nom s'affiche via Personnes) |
| PLA-09 agendas M365 / Google | Port `IAgendaExterne` (écriture, suppression, lecture des occupations) ; simulateur ; intitulé fixe « Rendez-vous SEPP » sans type d'acte, identité, affilié ni obligation (`EvenementAgenda`) ; les occupations externes deviennent des indisponibilités |
| SAN-10 convocations | `ConvocationEmise` (canal par demande, préférence de l'affilié ou défaut) ; envoi par Communications ; convocation par lot (`LotId`) |
| SAN-11 recommandé | Option par type d'acte (`Planification:Parametres:TypesRecommandes`) ou par demande ; le recommandé passe par `Courrier` ou `Email`, jamais par SMS ni portail |
| SAN-12 réservation en ligne | `/api/v1/reservations/*` : employeur (claims `affilie_id`) et travailleur (claim `personne_id`) ; créneaux ouverts sans information interne ; annulation jusqu'à 24 h avant |
| SAN-13 rappels, absences, reconvocation | Rappels J-7 / J-1 (`CONVOCATION.RAPPEL_1/2`), idempotents ; constat d'absence, obligations remises à planifier ; reconvocation dans le premier créneau libre |

## Contrats

Publiés : `planification.rendez-vous-planifie`, `rendez-vous-annule` (contrats existants) ; `convocation-emise`, `rappel-rendez-vous-du`,
`rendez-vous-replanifie`, `absence-rendez-vous-constatee`, `urgence-non-couverte` (`src/Contracts/Sepp.Contracts/Planification`,
catalogue `docs/architecture/evenements.md`). Le service Communications consomme `convocation-emise` et `rappel-rendez-vous-du`.
Consommés : `obligations.obligation-creee`, `obligations.obligation-echue`, `referentiels.parametre-legal-modifie`.

## Pas de double réservation

- `occupation_ressource` : contrainte d'exclusion PostgreSQL (`EXCLUDE USING gist`, extension `btree_gist`, migration `Initial`) — une
  ressource, salle ou appareil compris, ne peut pas être occupée sur deux périodes qui se chevauchent ; violation → 409.
- `rendez_vous` : index unique partiel — un créneau n'a qu'un rendez-vous actif.
- Verrou optimiste sur le créneau (DAT-03).
- Tests d'intégration : huit réservations simultanées du même créneau → un rendez-vous ; six tournées simultanées du même conseiller → une session.
- `btree_gist` est une extension de confiance (PostgreSQL 13+) ; sur Azure Database for PostgreSQL elle doit figurer dans `azure.extensions`.

## Périmètre des externes

- Employeur (rôles `employeur`, `sipp`) : affiliés du claim multivalué `affilie_id`.
- Travailleur (rôle `travailleur`) : claim `personne_id` = identifiant de la personne dans le service Personnes. **À confirmer avec
  l'analyse du fournisseur d'identité** : ce claim n'est pas encore émis par le realm local ; sans lui, un travailleur ne peut rien réserver.

## Lacunes et hypothèses

- Les codes de type d'examen reçus des obligations servent de types d'acte des créneaux (`EXAMEN_REPRISE`, `CONSULTATION_SPONTANEE`,
  `VISITE_PRE_REPRISE` pour les urgences) : à aligner avec le service Obligations dès son écriture (`Planification:Parametres:TypesUrgence`).
- L'envoi effectif des convocations (message, date d'envoi) est connu de Communications : `Convocation.EnregistrerEnvoi` existe dans le
  domaine mais aucune route ni consommateur ne l'appelle encore.
- L'outil RH et les inscriptions d'application Microsoft Entra / Google ne sont pas identifiés : adaptateurs « Reel » en squelette documenté
  (`Adapters/External/Adaptateurs.cs`) ; `Planification:Adaptateurs:*` vaut `Simulateur` en développement.
- Tâches planifiées (rappels, synchronisation) : une seule instance du service ; désactivées en développement (`Planification:Taches:Actif`).
- Le CPMT est rapproché de sa ressource par `Ressource.ReferenceId` = claim `sub`.
