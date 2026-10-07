# ADR 0008 — Sagas : processus persistants, minuteries en base, déclencheur par API

## Contexte
ARC-33 exige que les processus longs (reprise du travail, réintégration, procédure psychosociale formelle) soient orchestrés avec compensation et minuteries ; POR-04 demande un suivi de statut visible de l'employeur. L'ADR 0004 prévoyait des agrégats persistés dans un service orchestrateur, avec minuteries par messages planifiés. Le plan de la saga « examen de reprise » (`docs/architecture/saga-examen-reprise.md`, rouffou/veilla#264) a montré plusieurs écarts avec cette intention :

- la chorégraphie existante laisse des jalons sans propriétaire (aucun producteur de `RepriseAnnoncee`, aucune clôture d'obligation publiée, aucun retour d'envoi des convocations) et ne permet ni suivi de statut, ni annulation, ni alerte d'échéance ;
- le BFF n'a ni base ni outbox : lui faire publier `RepriseAnnoncee` violerait ARC-32 (publication transactionnelle) ;
- aucun bus n'existe en développement local : des messages planifiés Service Bus ne sont ni testables ni reproductibles.

## Décision
1. **Orchestrateur persistant hébergé par Obligations.** Un agrégat `ProcessusReprise` (table `processus_reprise`, jalons idempotents acceptés dans un ordre quelconque, statut dérivé) est tenu par le service Obligations, en zone standard : il ne porte que des identifiants, dates, statuts et codes (ARC-06). Les réactions chorégraphiées existantes restent (elles servent aussi hors saga).
   - Pas de chorégraphie pure : elle ne donne ni compensation, ni minuterie, ni statut interrogeable (ARC-33, POR-04).
   - Pas Réintégration : ce service porte le trajet d'incapacité et de réintégration (§15.3), son périmètre est la zone médicale et il n'a aucune raison de connaître le délai légal de la reprise.
   - Pas de nouveau service : une saga n'est pas une capacité métier distincte (ARC-01) ; Obligations possède déjà le délai légal, le calendrier et l'obligation dont la clôture termine le processus, et consomme presque tous les événements concernés.
2. **Minuteries stockées en base.** Chaque processus porte `prochaine_echeance` et `type_minuterie`. Un service d'arrière-plan les traite par `SELECT … FOR UPDATE SKIP LOCKED` (plusieurs instances sans doublon) en s'appuyant sur `TimeProvider` (fuseau Europe/Brussels pour les jours ouvrables ; `FakeTimeProvider` dans les tests). Ceci **amende l'ADR 0004** : les minuteries ne sont plus des messages planifiés Service Bus, qui n'existent pas en local, ne se testent pas de façon déterministe et ne s'annulent pas proprement quand un processus change de date.
3. **Déclencheur par appel synchrone.** Le BFF relaie l'annonce de l'employeur par un appel synchrone (ARC-30) `POST /api/v1/reprises` à l'API d'Obligations, qui persiste le processus et publie `obligations.reprise-enregistree` par son outbox, dans la même transaction. **Écart à la lettre du §14.6** (« le BFF publie `RepriseAnnoncee` ») : **à valider par l'architecte**.
4. **Rétrocompatibilité de `RepriseAnnoncee`.** Le contrat `bff-employeur.reprise-annoncee.v1` n'est ni supprimé ni modifié : il reste consommé par Obligations (qui délègue à la même fonction d'enregistrement, origine `Evenement`) pour tout producteur éventuel. Il est marqué « conservé pour rétrocompatibilité » dans le code et dans le catalogue. Les autres ajouts de contrats sont compatibles en v1 (champs facultatifs en fin de constructeur : `DecisionEmise.ExamenId`, `DocumentPublie.ObjetType`/`ObjetId`).

## Alternatives
- **Chorégraphie pure** (statu quo amélioré) : moins de code, mais pas de vue d'ensemble, et les délais, annulations et alertes devraient être répartis entre cinq services.
- **Orchestrateur dans Réintégration ou dans un service dédié** : écartés (voir plus haut).
- **Messages planifiés Service Bus** : conformes à l'ADR 0004 d'origine, mais absents en local, difficiles à tester et à annuler.
- **Publication de `RepriseAnnoncee` par le BFF** : conforme au §14.6 mais contraire à ARC-32 sans outbox côté BFF.

## Conséquences
- Les sagas suivantes (réintégration, procédure psychosociale formelle) suivent le même modèle : agrégat persistant dans le service qui possède le délai légal, minuteries en base, jalons idempotents.
- Le socle ajoute `BusinessCalendar.SubtractBusinessDays` et `Microsoft.Extensions.TimeProvider.Testing` ; les permissions `reprise:annoncer`, `reprise:lire` et `reprise:gerer` (répartition à valider, §3.3).
- Le déclencheur synchrone doit être validé par l'architecte ; s'il est refusé, le BFF devra disposer d'une outbox (ARC-32) et publier `RepriseAnnoncee`, dont le consommateur reste en place.
- Les codes de type d'examen sont unifiés dans `Sepp.Contracts.TypesExamen`.
