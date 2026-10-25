# ADR 0004 — Azure Service Bus et outbox/inbox maison

> **Amendée par l'[ADR 0008](0008-sagas-processus-persistants.md)** : les minuteries des sagas sont stockées en base et traitées par un service d'arrière-plan, et non plus par messages planifiés.
>
> **Amendée (#391)** : en local, un bus de développement PostgreSQL remplace l'émulateur Service Bus (voir « Amendement : bus de développement »).

## Contexte
Communication asynchrone par défaut (ARC-05), livraison au moins une fois et consommateurs idempotents (ARC-31), outbox transactionnelle (ARC-32), sagas pour les processus longs (ARC-33).

## Décision
- **Azure Service Bus** (rubriques et abonnements) ; en local, le bus de développement PostgreSQL (amendement ci-dessous) plutôt que l'émulateur Service Bus.
- Le socle `Sepp.BuildingBlocks.Infrastructure` fournit :
  - une **outbox** EF Core : les événements d'intégration sont écrits dans `outbox_message` dans la même transaction que les données, puis publiés par un service d'arrière-plan ;
  - une **inbox** (`inbox_message`) : le consommateur enregistre l'identifiant du message traité dans la même transaction que son effet ;
  - une abstraction `IMessagePublisher` (implémentation Service Bus, implémentation en mémoire pour les tests et le poste de développement).
- Les sagas (reprise, réintégration, procédure psychosociale formelle) seront des agrégats persistés dans le service orchestrateur, avec minuteries par messages planifiés (amendé par l'ADR 0008 : minuteries stockées en base).

## Alternatives
MassTransit ou NServiceBus : complets mais sous licence commerciale. Wolverine : pertinent mais très structurant. Le besoin réel tient en quelques centaines de lignes testées, remplaçables derrière les mêmes interfaces.

## Conséquences
Les contrats d'événements vivent dans `src/Contracts`, versionnés, et ne transportent que des identifiants, dates, statuts et catégories (ARC-06, ARC-34).

## Amendement : bus de développement PostgreSQL (#391)

### Contexte
Sans bus, l'outbox publie en mémoire sur le poste de développement : les messages sont perdus et aucun consommateur ne démarre, donc aucun flux entre services (affilié → personne → obligations, par exemple). L'émulateur Azure Service Bus impose d'accepter les licences de l'émulateur et de SQL Edge, ce que l'on ne peut pas exiger par défaut.

### Décision
- `Sepp.BuildingBlocks.Infrastructure/Messaging` fournit un **bus de développement PostgreSQL**, activé par `ConnectionStrings:BusDeveloppement` (base `bus` du conteneur `postgres` local, créée par `init-databases.sql` ou, à défaut, au premier démarrage).
- Il est **refusé hors des environnements Development, Test et Testing** : à l'enregistrement si la configuration connaît l'environnement, et toujours à la construction du bus (le service ne démarre pas). Il ne peut pas non plus coexister avec une configuration Service Bus. Sans cette chaîne, rien ne change (Service Bus ou publication en mémoire).
- **Publication** : inchangée, par l'outbox. `PostgresMessagePublisher` écrit chaque message dans `bus_message` (`seq`, `id`, `topic`, `subject` = nom versionné du contrat, `payload`, `correlation_id`, `occurred_at`, `cree_le`). Les insertions d'une rubrique sont sérialisées par un verrou consultatif de transaction : une ligne n'est jamais visible avant une ligne de numéro inférieur, et un curseur ne saute aucun message.
- **Consommation** : `PostgresBusConsumer<TContext>` lit, pour chaque rubrique souscrite par le code, les messages postérieurs au curseur de l'abonnement (`bus_curseur(subscription, topic, seq)`), avec l'abonnement `ServiceBus:SubscriptionName` et les filtres `ServiceBus:SubjectFilters:<rubrique>` (copie des `subscribes_to` et `subject_filters` de Terraform, vérifiée par test sur `deploy/local/compose.yaml`). Il remet chaque message à `IntegrationEventDispatcher`, idempotent par l'inbox. Le curseur avance dans la transaction du bus, après traitement : livraison au moins une fois, reprise au premier message non acquitté après un redémarrage. Un nouvel abonnement lit sa rubrique depuis le début, comme un abonnement créé par Terraform avant le démarrage des services. Une ligne de curseur verrouillée (`FOR UPDATE SKIP LOCKED`) empêche deux instances de consommer le même abonnement en même temps.
- **Échecs** : un message en échec est retenté à chaque passe, dans l'ordre (`bus_tentative`) ; après `BusDeveloppement:MaxAttempts` tentatives (5 par défaut, comme `max_delivery_count`), il passe en **lettre morte** (`bus_lettre_morte`, journal d'erreur) et l'abonnement continue.

### Alternatives écartées
L'émulateur Azure Service Bus (licences de l'émulateur et de SQL Edge à accepter) ; la publication en mémoire (aucun flux entre services) ; un bus HTTP direct entre services (sémantique d'abonnement et de filtres différente de Service Bus).

### Conséquences
Le bus de développement ne vaut que pour le poste de développement et les tests : ni chiffrement, ni contrôle d'accès par rubrique, ni rétention. Les tables du bus grossissent tant que le volume local n'est pas recréé (`docker compose down -v`). Les filtres de sujets de `compose.yaml` doivent suivre `infra/variables.tf` (test `ConfigurationTerraformTests`).
