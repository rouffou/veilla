# ADR 0004 — Azure Service Bus et outbox/inbox maison

## Contexte
Communication asynchrone par défaut (ARC-05), livraison au moins une fois et consommateurs idempotents (ARC-31), outbox transactionnelle (ARC-32), sagas pour les processus longs (ARC-33).

## Décision
- **Azure Service Bus** (rubriques et abonnements) ; l'émulateur Service Bus officiel en local.
- Le socle `Sepp.BuildingBlocks.Infrastructure` fournit :
  - une **outbox** EF Core : les événements d'intégration sont écrits dans `outbox_message` dans la même transaction que les données, puis publiés par un service d'arrière-plan ;
  - une **inbox** (`inbox_message`) : le consommateur enregistre l'identifiant du message traité dans la même transaction que son effet ;
  - une abstraction `IMessagePublisher` (implémentation Service Bus, implémentation en mémoire pour les tests et le poste de développement).
- Les sagas (reprise, réintégration, procédure psychosociale formelle) seront des agrégats persistés dans le service orchestrateur, avec minuteries par messages planifiés.

## Alternatives
MassTransit ou NServiceBus : complets mais sous licence commerciale. Wolverine : pertinent mais très structurant. Le besoin réel tient en quelques centaines de lignes testées, remplaçables derrière les mêmes interfaces.

## Conséquences
Les contrats d'événements vivent dans `src/Contracts`, versionnés, et ne transportent que des identifiants, dates, statuts et catégories (ARC-06, ARC-34).
