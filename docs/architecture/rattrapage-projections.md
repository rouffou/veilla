# Rattrapage des projections locales (procédure d'exploitation)

Décision : [ADR 0011](../adr/0011-rattrapage-projections.md). Exigences : ARC-31, ARC-34, ARC-06, ARC-41, plan de reprise des
données.

Une projection locale ne reçoit que les événements publiés après la création de l'abonnement de son service. À la mise en
service d'un consommateur, ou quand un service s'abonne à un nouveau contrat, l'exploitation demande aux producteurs de
**republier l'état courant** sous forme d'instantanés. L'opération est idempotente : elle peut être relancée sans risque.

## Ce qui est republiable

| Producteur | Type (`type`) | Contrat publié | Consommateurs qui se rattrapent |
|---|---|---|---|
| affilies | `affilies` | `affilies.affilie-instantane.v1` | documents, documents-medicale (projection linguistique) |
| personnes | `personnes` | `personnes.personne-instantanee.v1` (langue seulement) | documents, documents-medicale (projection linguistique) |
| personnes | `occupations` | `personnes.occupation-instantanee.v1` | affilies (employeurs du travailleur, POR-11) |

Producteurs et consommateurs restants : voir le ticket #369 (Obligations, Surveillance médicale, Planification,
Réintégration, Postes et risques).

## Prérequis

- Rôle `exploitation` attribué à l'opérateur dans Entra ID (realm local : utilisateur `exploitant`, mot de passe
  `veilla-dev`). Ce rôle n'a que `plateforme:republier` : il ne lit aucune donnée métier.
- Version du producteur qui expose `/api/v1/admin/republications` (migration `Republication` appliquée par son job de
  migration, ADR 0010) et version du consommateur qui porte le gestionnaire de l'instantané.
- Abonnements et filtres de sujets à jour (`infra/variables.tf`, `subject_filters`), appliqués par Terraform **avant** la
  republication : un instantané publié avant la création du filtre n'est pas livré.

## Ordre de mise en service

1. Déployer l'infrastructure (Terraform) : abonnements et filtres des consommateurs.
2. Déployer les consommateurs (migrations comprises) et vérifier qu'ils consomment (`/health/ready`, journaux du
   consommateur Service Bus). Les événements courants alimentent déjà la projection.
3. Déployer les producteurs (migration `Republication` comprise).
4. Republier, producteur par producteur. Pour Documents : `affilies` (Affiliés), puis `personnes` (Personnes). Pour les
   occupations d'Affiliés : `occupations` (Personnes). L'ordre entre types est indifférent : chaque instantané est
   indépendant et daté.
5. Contrôler le résultat (ci-dessous), puis seulement basculer un consommateur sur sa projection (par exemple
   `DOCUMENTS__SOURCELINGUISTIQUE = Projection` pour l'instance standard).

Un événement courant publié pendant la republication n'est pas perdu ni écrasé : chaque instantané porte l'horodatage de
l'état (`etatDu`) et le consommateur ignore un instantané plus ancien que son état.

## Lancer une republication

```bash
# Jeton de l'opérateur (local : realm veilla, client veilla-dev-cli).
TOKEN=$(curl -s -X POST http://localhost:8180/realms/veilla/protocol/openid-connect/token \
  -d grant_type=password -d client_id=veilla-dev-cli -d username=exploitant -d password=veilla-dev | jq -r .access_token)

# Types republiables par le service.
curl -H "Authorization: Bearer $TOKEN" "$PERSONNES/api/v1/admin/republications/types"

# Demande : 202 Accepted, en-tête Location vers le suivi. 409 si une republication du même type est déjà en cours.
curl -i -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"type":"personnes"}' "$PERSONNES/api/v1/admin/republications"
```

En Azure, les services ne sont joignables que par le réseau interne : passer par le point d'accès d'administration de la
plateforme (bastion ou job ponctuel dans l'environnement Container Apps), jamais par une ouverture publique.

## Contrôler le résultat

1. **Côté producteur** : `GET /api/v1/admin/republications/{id}` jusqu'à `"statut": "Terminee"`. `publies` doit être égal au
   nombre d'objets non supprimés du type (par exemple `SELECT count(*) FROM personne WHERE deleted_at IS NULL`). `Echouee`
   (avec `erreur`) signale un type sans source dans la version déployée. Un lot en échec technique est rejoué au tick
   suivant (journal « Échec d'un lot de republication ») : la republication reste `EnCours`.
2. **Bus** : l'outbox du producteur se vide (`SELECT count(*) FROM outbox_message WHERE processed_at IS NULL`), aucun
   message en lettre morte sur les abonnements des consommateurs (métrique `DeadletteredMessages` de Service Bus).
3. **Côté consommateur** : nombre de lignes de la projection, par exemple pour Documents
   `SELECT sujet, count(*) FROM contexte_linguistique GROUP BY sujet` (à rapprocher des compteurs `publies` ; une personne
   sans langue valide n'est pas projetée), et pour Affiliés `SELECT count(*) FROM occupation_locale`.
4. **Fonctionnel** : un document généré par `documents-medicale` pour un affilié ou un travailleur néerlandophone antérieur
   à la mise en service sort en néerlandais.

## Débit

Section `Republication` de l'appsettings du producteur (valeurs par défaut, à valider en recette de charge) :
`TailleLot` = 200 instantanés par lot, `Intervalle` = 1 s entre deux lots, `ArriereOutboxMax` = 1000 messages en attente
dans l'outbox au-delà desquels aucun lot n'est écrit. Pour 300 000 personnes : environ 25 minutes.

## Sécurité

- Aucun instantané ne transporte de donnée de santé ni d'identité : `personne-instantanee` ne porte que la langue (ARC-06,
  vérifié par `tests/Sepp.Contracts.Tests`).
- La demande est tracée (`republication.demandee_par`, `demandee_le`) et journalisée.
