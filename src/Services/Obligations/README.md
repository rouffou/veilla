# Service Obligations

Service de la zone standard (ARC-06) : il ne porte que des identifiants, des types d'examens, des dates et des statuts, jamais de contenu médical.
Il calcule les obligations de surveillance de la santé des travailleurs (SAN-01 à SAN-04, AFF-24, AFF-32, §5.1), suit leur statut (SAN-02)
et héberge l'orchestrateur de la saga « examen de reprise du travail » (ARC-33, POR-04, ADR 0008, plan `docs/architecture/saga-examen-reprise.md`).

## Architecture

Clean architecture (§14.5), une base PostgreSQL propre au service (ARC-02) :

| Couche | Contenu |
|---|---|
| `Sepp.Obligations.Domain` | `Obligation` et sa machine à états, `MoteurEcheances` (fonction pure de l'état des projections), `ReconciliationObligations`, projections locales (`Projections.cs`), `Reprises/ProcessusReprise`, `PolitiqueSuiviReprise` |
| `Sepp.Obligations.Application` | cas d'usage (consultation, gestion, reprises), gestionnaires d'événements, `RecalculObligations`, `SynchronisationProcessusReprise`, `TraiterMinuteriesReprise` |
| `Sepp.Obligations.Adapters` | EF Core / PostgreSQL et migrations, API REST, `TraitementPeriodiqueService`, `MinuteriesRepriseService` |
| `Sepp.Obligations.Infrastructure` | racine de composition, configuration |

Aucun appel synchrone vers un autre service : le moteur ne lit que des modèles de lecture locaux alimentés par événements (ARC-31) ; l'inbox rend les
consommateurs idempotents et l'outbox publie atomiquement avec la transaction (ARC-32).

## La saga « examen de reprise »

Le processus est un agrégat persistant (`ProcessusReprise`, table `processus_reprise`) : un gestionnaire de processus (ADR 0008), pas une chorégraphie pure.
Les réactions chorégraphiées existantes (Planification, Surveillance médicale, Documents, Communications) restent en place.

1. **Annonce.** `POST /api/v1/reprises` (portail employeur via le BFF, ou application interne) ou l'événement `bff-employeur.reprise-annoncee` (origine `Evenement`).
   Idempotent : même travailleur, affilié et date de reprise = même processus (200) ; seul `DebutAbsence` différent = modification. Le processus, la reprise locale
   et le recalcul sont validés dans la même transaction. Absence d'au moins 4 semaines : `ObligationCreee(EXAMEN_REPRISE)` ; sinon branche `ExamenNonRequis`.
   Publie `obligations.reprise-enregistree.v1`.
   **Occupation requise** : la personne doit avoir une occupation active chez l'affilié à la date de reprise (projections `OccupationDebutee` / `OccupationTerminee`, fin incluse) ; sinon 422 `reprise.occupation-inactive`, à la création comme à la modification du début d'absence. L'annonce par événement est ignorée sans exception (comme les autres rejets métier, pas de rejeu infini).
2. **Rendez-vous.** Planification réserve un créneau (`RendezVousPlanifie`) ou signale `UrgenceNonCouverte` (jalon `NonCouverte`, alerte).
3. **Convocation.** `ConvocationEnvoyee` passe l'obligation à « convoqué » ; `ConvocationNonRemise` lève une alerte.
4. **Absence / annulation / replanification.** `AbsenceRendezVousConstatee` passe l'obligation à « absent » (`nombre_absences`), `RendezVousAnnule` (hors `ObligationLevee`)
   et `RendezVousReplanifie` mettent le processus à jour. `PlanificationUrgenteDemandee` n'est publiée que si `Obligations:Reprise:ReplanificationAutomatique` vaut `true`
   (`false` par défaut) ; sinon une alerte demande une replanification manuelle.
5. **Examen et décision.** `ExamenCloture` réalise l'obligation ; `DecisionEmise` (avec `ExamenId`) est appliquée au processus. Une décision reçue avant l'examen est parquée
   (`decision_recue`) et appliquée dès que l'examen est connu ; la plus récente (`OccurredAt`) l'emporte. `DocumentPublie` (`ObjetType = decision`, destinataire `Affilie`)
   renseigne `document_employeur_id`. La saga est `Terminee` quand l'examen est réalisé **et** la décision émise.
6. **Compensation.** `POST /api/v1/reprises/{id}/annulation` annule la reprise : la reprise locale est marquée annulée, le moteur l'ignore, le recalcul annule l'obligation
   (`ObligationCloturee(Annule)`), ce qui permet à Planification de libérer le rendez-vous. Refusée (409) une fois l'examen clôturé.
   La sortie de l'entreprise donne `SansObjet` (`ObligationCloturee(SortieEntreprise)`).

Les jalons sont idempotents et acceptés dans un ordre quelconque ; le statut en est dérivé :
`Annoncee → ObligationOuverte → Planifiee | NonCouverte → Convoquee → ExamenRealise → Terminee` (+ `DecisionEmise` si la décision précède l'examen),
branches `ExamenNonRequis`, `Annulee`, `SansObjet`, et indicateur `hors_delai` / `EnRetard`. La date limite est recopiée de l'obligation, jamais recalculée par le processus.
La synchronisation (`SynchronisationProcessusReprise`) est appelée après chaque recalcul, dans la même transaction.

### Minuteries

Stockées en base (`prochaine_echeance`, `type_minuterie`), traitées par `MinuteriesRepriseService` (`FOR UPDATE SKIP LOCKED`, `TimeProvider`, fuseau de Bruxelles) à l'intervalle
`Obligations:Reprise:IntervalleMinuteries` (1 minute) : plusieurs instances ne traitent jamais le même processus. Pas de message planifié Service Bus.

| Minuterie | Déclenchement | Effet |
|---|---|---|
| `EcheanceMenacee` | N jours ouvrables avant la date limite, sans examen | alerte |
| `HorsDelai` | lendemain de la date limite, sans examen | `hors_delai`, `EnRetard`, alerte |
| `RendezVousSansCloture` | rendez-vous passé d'1 jour ouvrable sans clôture | alerte |
| `DecisionEnAttente` | N jours ouvrables après l'examen sans décision | alerte |
| `Expiration` | N jours après la reprise | alerte (aucun changement de statut) |

### API

| Route | Permission | Remarque |
|---|---|---|
| `POST /api/v1/reprises` | `reprise:annoncer` | 201 à la création, 200 si idempotent ; périmètre `affilie_id` pour les externes |
| `GET /api/v1/reprises?statut=&echeanceAvant=&affilieId=` | `reprise:lire` | `echeanceAvant` : date limite au plus tard à cette date |
| `GET /api/v1/reprises/{id}` | `reprise:lire` | un externe ne voit pas les identifiants d'examen et de décision |
| `PUT /api/v1/reprises/{id}` | `reprise:gerer` | corrige `debutAbsence` ; pour changer la date de reprise, annuler puis annoncer de nouveau |
| `POST /api/v1/reprises/{id}/annulation` | `reprise:gerer` | corps `{ "motif": "ErreurDeSaisie" \| "RepriseReportee" \| "Autre" }` |
| `GET /api/v1/affilies/{id}/alertes` | `obligation:lire` | enrichie des alertes de reprise si `reprise:lire` |

### Configuration (`Obligations:Reprise:*`)

| Clé | Défaut | Remarque |
|---|---|---|
| `ReplanificationAutomatique` | `false` | **à valider** (reconvocation automatique après une absence) |
| `AlerteAvantEcheanceJoursOuvrables` | `2` | **à valider** (`SANTE.REPRISE.ALERTE_AVANT_ECHEANCE`) ; 0 désactive |
| `RendezVousSansClotureJoursOuvrables` | `1` | fixé par le plan |
| `DelaiDecisionJoursOuvrables` | non défini (désactivé) | **à valider** |
| `ExpirationJours` | non défini (désactivé) | **à valider** |
| `Actif`, `IntervalleMinuteries` | `true`, `00:01:00` | service d'arrière-plan |

Les délais légaux (10 jours ouvrables, 4 semaines) viennent des paramètres légaux du service Référentiels (`SANTE.REPRISE.DELAI`, `SANTE.REPRISE.ABSENCE_MINIMUM`), pas de cette configuration.
Les valeurs par défaut ci-dessus sont des valeurs provisoires d'organisation, jamais présentées comme légales.

## Points à valider

Métier (départements médical et juridique) : décompte du délai (jour de reprise, reprise un jour non ouvrable, borne des 4 semaines), nature de l'absence, champ d'application,
examen après la date limite, reconvocation automatique et nombre de tentatives, ce que voit l'employeur (POR-04), décision reportée, sortie de l'entreprise pendant le processus,
seuils d'alerte et expiration, motifs d'annulation (codes provisoires), répartition des permissions `reprise:*`.

Architecture : déclencheur par API plutôt que par le BFF (écart au §14.6), examen « ouvert » non détectable (voir limites), `ObligationCloturee.Motif = Recalcul` pour une annulation de reprise.

## Limites connues

- Occupation : si `personnes.occupation-debutee` n'est pas encore reçu (événements en désordre), l'annonce est refusée comme s'il n'y avait pas d'occupation (**à valider**) ; l'API peut être rappelée une fois l'occupation reçue, mais une annonce par événement refusée n'est pas rejouée.
- Aucun événement « examen ouvert » n'existe : l'annulation n'est refusée qu'une fois l'examen **clôturé** (`ExamenCloture`), pas pendant sa réalisation.
- Un `DocumentPublie` reçu avant la `DecisionEmise` correspondante n'est pas parqué (information seule).
- Un changement manuel de statut (`ChangerStatutObligation`) ne resynchronise le processus qu'au prochain recalcul.
- Un enregistrement interrompu (processus créé, recalcul non terminé) est repris par le traitement périodique.

## Tests

`dotnet test` sur `Obligations.slnx` : domaine (permutations d'ordre des jalons, minuteries), application (`FakeTimeProvider`), architecture,
et intégration Testcontainers (PostgreSQL : API, annonces simultanées, minuteries `SKIP LOCKED` en parallèle).
