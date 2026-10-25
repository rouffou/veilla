# ADR 0011 — Rattrapage des projections locales : republication de l'état courant par les producteurs (instantanés)

## Statut
Proposée le 2026-10-25 (à valider par l'architecte). Références : rouffou/veilla#369, #366, #368. Complète l'ADR 0004.

## Contexte
Plusieurs services tiennent des projections locales alimentées par les événements d'autres services (ARC-05, ARC-31) :
- Documents : `contexte_linguistique` (régime et langue de l'affilié, langue du travailleur, NF-41, ADR 0009). L'instance
  `documents-medicale` ne peut appeler ni Affiliés ni Personnes ; sans projection, elle produit les documents en français ;
- Affiliés : occupations projetées (POR-11, #340, #368) ;
- Obligations : occupations, affectations, états particuliers, profils de risque, rendez-vous ;
- Surveillance médicale, Planification et Réintégration : obligations, affectations, profils de risque, incapacités.

Une projection ne contient que les événements publiés après la création de l'abonnement du consommateur : Service Bus ne
rejoue pas l'historique, et les producteurs ne conservent les messages de l'outbox que jusqu'à leur publication. Les affiliés
et personnes antérieurs à la mise en service d'un consommateur (ou à l'ajout d'un nouveau consommateur) en sont absents.

Contraintes : ARC-06 (aucune donnée sensible dans un événement), ARC-31 (consommateurs idempotents), ARC-34 (contrats
versionnés), ARC-41 (permissions), et la charge : un producteur peut compter des centaines de milliers de lignes.

## Décision
1. **Republication contrôlée par le producteur.** Chaque producteur expose `POST /api/v1/admin/republications` (corps
   `{ "type": "<type d'état>" }`, réponse 202 avec `Location`), `GET /api/v1/admin/republications[/{id}]` (suivi) et
   `GET /api/v1/admin/republications/types`. Types livrés : `personnes` et `occupations` (Personnes), `affilies` (Affiliés).
2. **Droit d'exploitation dédié.** Permission `plateforme:republier`, accordée au seul nouveau rôle `exploitation` (rôle
   d'une personne de l'équipe d'exploitation, sans aucun droit métier). Aucun profil métier ne la reçoit, l'administrateur
   fonctionnel compris. La réponse ne contient aucune donnée métier (type, statut, compteurs, auteur, dates).
3. **Événements « instantané » dédiés**, et non réémission des événements v1 : `affilies.affilie-instantane.v1`,
   `personnes.personne-instantanee.v1` (langue seulement), `personnes.occupation-instantanee.v1`. Chacun porte l'état
   courant de l'objet et `EtatDu`, l'horodatage de sa dernière modification chez le producteur (`updated_at`).
   `correlationId` = identifiant de la republication.
4. **Publication par lots via l'outbox, débit borné.** La demande crée une ligne `republication` (`EnCours`). Un service
   d'arrière-plan du socle (`RepublicationProcessor`) lit un lot dans l'ordre de la clé primaire (curseur par identifiant),
   écrit les instantanés dans l'outbox et avance le curseur **dans la même transaction**. La ligne est verrouillée
   (`FOR UPDATE SKIP LOCKED`) : plusieurs répliques ne traitent jamais le même lot, une reprise après panne repart du dernier
   lot validé. Débit : `Republication:TailleLot` (200) par `Republication:Intervalle` (1 s), et aucun lot tant que l'outbox
   compte au moins `Republication:ArriereOutboxMax` (1000) messages à publier : les événements métier courants ne sont
   retardés que de quelques lots. Valeurs à valider en recette de charge.
5. **Idempotence et garde d'ordre chez le consommateur.** L'inbox écarte une livraison en double (ARC-31). Le gestionnaire
   d'un instantané est une écriture par clé, sans effet métier, qui ignore l'instantané si l'état local est plus récent que
   `EtatDu` (règle `OccurredAt` des projections existantes). Rejouer une republication ne crée donc ni doublon ni écrasement.
6. **Socle réutilisable** (`Sepp.BuildingBlocks`) : `ISourceRepublication` (une source par type, dans l'adaptateur de
   persistance du producteur), `AddSeppRepublication<TContext>`, `AddRepublicationSource<TSource>`,
   `ModelBuilder.AjouterRepublications()` (table `republication`, migration du producteur), `MapSeppRepublications()`.

### Instantanés dédiés plutôt que réémission des événements v1
- **Pas d'effet de bord involontaire.** Un événement v1 est un fait métier que ses consommateurs traitent comme tel :
  réémettre `personnes.occupation-debutee` relancerait le calcul des obligations, `affilies.affilie-cree` une interrogation
  de la BCE par Intégrations, et chaque nouveau consommateur v1 hériterait du risque. Un instantané n'est traité que par les
  consommateurs qui s'y abonnent explicitement (gestionnaire, `subject_filters`) : abonnement opt-in, revu à chaque ajout.
- **État et non fait.** Une occupation terminée se republie en un seul instantané (début et fin), au lieu de deux faits
  dont l'ordre d'arrivée compte. La garde d'ordre s'appuie sur l'horodatage de l'état (`EtatDu`), pas sur l'heure de
  republication : un instantané lu avant une modification et livré après elle est ignoré.
- **Pas de rupture de contrat** : les v1 ne changent pas ; un en-tête de rejeu exigerait que chaque consommateur v1 sache
  l'interpréter, sans garde-fou statique.
- Coût accepté : un contrat et un gestionnaire de plus par objet et par consommateur, et la mise à jour des filtres
  Terraform.

## Alternatives écartées
- **Réémission des v1 avec un en-tête de rejeu** : voir ci-dessus.
- **Import initial par extraction de base** (export SQL du producteur vers le consommateur) : couplage aux schémas,
  impossible entre zones (NSG, ARC-04), contourne ARC-06 et l'audit.
- **Lecture synchrone du producteur au démarrage du consommateur** : impossible pour `documents-medicale` (pare-feu de la
  zone médicale), et charge non maîtrisée sur le producteur.
- **Rétention longue sur le bus** (sessions, rejeu de journal) : Service Bus ne rejoue pas un abonnement créé après coup.

## Conséquences
- Procédure d'exploitation : [rattrapage des projections](../architecture/rattrapage-projections.md) (ordre de mise en
  service, contrôle du résultat).
- Nouveau rôle `exploitation` à créer dans Entra ID (realm local : utilisateur `exploitant`).
- Producteurs et consommateurs restants (Obligations, Surveillance médicale, Planification, Réintégration, Postes et
  risques, Intégrations) : suivis dans le ticket #369 ; même socle, un type de source et un gestionnaire par objet.
- Les horodatages comparés (`updated_at` du producteur, `OccurredAt` des événements v1) viennent de l'horloge système des
  répliques : un écart d'horloge de quelques millisecondes entre répliques est sans effet pratique.
