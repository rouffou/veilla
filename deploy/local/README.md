# Environnement local (CTR-23)

Environnement complet pour le poste de développement, avec **données fictives uniquement** (NF-14).

```bash
docker compose -f deploy/local/compose.yaml up -d --build
```

| Composant | URL | Remarque |
|---|---|---|
| Service Référentiels | http://localhost:5110 | `/health/ready`, `/openapi/v1.json`, `/scalar` (Development) |
| Service Affiliés | http://localhost:5111 | idem |
| Service Personnes et occupations | http://localhost:5112 | idem ; clés de chiffrement de développement uniquement |
| Service Postes et risques | http://localhost:5113 | idem |
| Service Audit | http://localhost:5114 | idem |
| Service Intégrations | http://localhost:5115 | simulateurs BCE, DIMONA, registre national ; flux lancés par `POST /api/v1/flux/{flux}/executions` |
| BFF employeur | http://localhost:5200 | API du portail employeur ; voir [src/Bff/Employeur/README.md](../../src/Bff/Employeur/README.md) |
| BFF travailleur | http://localhost:5201 | API du portail travailleur ; voir [src/Bff/Travailleur/README.md](../../src/Bff/Travailleur/README.md) |
| Application interne | http://localhost:8081 | |
| Portail employeur | http://localhost:8082 | |
| Portail travailleur | http://localhost:8083 | |
| Keycloak (realm `veilla`) | http://localhost:8180 | console d'administration : `admin` / `admin-local-dev` |
| Tableau de bord OpenTelemetry | http://localhost:18888 | traces, journaux, métriques (ARC-47) |
| PostgreSQL | localhost:5432 | `sepp` / `sepp-local-dev`, une base par service |

## Utilisateurs de test

Définis dans [`keycloak/veilla-realm.json`](keycloak/veilla-realm.json), mot de passe commun `veilla-dev` :
`cpmt`, `cpmt.dirigeant`, `infirmier`, `cpap`, `securite`, `gestionnaire`, `planificateur`, `admin` (administrateur fonctionnel), `dpo`, `employeur`, `travailleur`, `exploitant` (rôle `exploitation` : republication des producteurs, aucun droit métier, voir [rattrapage des projections](../../docs/architecture/rattrapage-projections.md)).

Les rôles de la matrice §3.3 sont émis dans le claim `roles` et l'audience `sepp-api` est ajoutée au jeton, comme en production.

Les utilisateurs externes portent le claim multivalué `affilie_id` : identifiants des affiliés auxquels ils ont accès (attribut utilisateur Keycloak, déclaré dans le profil utilisateur du realm). L'utilisateur `employeur` est rattaché à l'affilié fictif `0192a5c8-0000-7000-8000-000000000001` . Pour tester le portail employeur, créez un affilié puis remplacez cette valeur par son identifiant dans la console Keycloak (Utilisateurs → employeur → Attributs).

L'utilisateur `travailleur` porte le claim `personne_id` (valeur unique, fictive : `0192a5c8-0000-7000-8000-0000000000a1`), lu par Documents et Planification pour limiter le travailleur à ses propres données (ADR 0005). Pour tester le portail travailleur, remplacez cette valeur par l'identifiant d'une personne créée.

## Appeler l'API

Le client `veilla-dev-cli` (flux mot de passe) n'existe que dans ce realm local :

```bash
TOKEN=$(curl -s -X POST http://localhost:8180/realms/veilla/protocol/openid-connect/token \
  -d grant_type=password -d client_id=veilla-dev-cli -d username=admin -d password=veilla-dev | jq -r .access_token)
curl -H "Authorization: Bearer $TOKEN" "http://localhost:5110/api/v1/parametres-legaux?langue=nl"
curl -H "Authorization: Bearer $TOKEN" "http://localhost:5110/api/v1/calendrier/echeance?depart=2026-05-01&joursOuvrables=10"
```

## Bus d'événements

Les services du `compose.yaml` publient et consomment leurs événements par le **bus de développement PostgreSQL** (ADR 0004, amendement #391) : base `bus` du conteneur `postgres`, publication par l'outbox, consommation par curseur et par abonnement.

- Chaque service doté d'une base reçoit `ConnectionStrings__BusDeveloppement` ; chaque service abonné reçoit `ServiceBus__SubscriptionName` (son nom dans le catalogue Terraform) et ses `ServiceBus__SubjectFilters__<rubrique>__<n>` (les `subject_filters` de `infra/variables.tf`). Un test (`ConfigurationTerraformTests`) vérifie cet alignement : un nouvel abonnement ou filtre Terraform doit être reporté ici.
- L'instance unique `documents` sert localement les trois zones : ses filtres réunissent ceux de `documents` et de `documents-medicale`. `compose.documents-par-zone.yaml` rétablit le filtre de la seule zone standard.
- Le bus est **refusé hors des environnements Development, Test et Testing** : un service configuré autrement ne démarre pas.
- Un message en échec est retenté à chaque passe (1 s), puis mis en lettre morte après 5 tentatives (`BusDeveloppement__MaxAttempts`) ; l'abonnement continue. Pour inspecter le bus :

```bash
docker compose -f deploy/local/compose.yaml exec postgres psql -U sepp -d bus   -c "select subscription, topic, seq from bus_curseur order by 1, 2"   -c "select subscription, subject, tentatives, erreur from bus_lettre_morte"
```

- Un volume créé avant l'ajout de la base `bus` reste utilisable : la base est créée au premier démarrage. `docker compose down -v` vide le bus avec les autres bases.
- Un service lancé hors conteneur (`dotnet run`) n'utilise le bus que si l'on renseigne `ConnectionStrings__BusDeveloppement` (`Host=localhost;Database=bus;Username=sepp;Password=sepp-local-dev`) et `ServiceBus__SubscriptionName` ; sinon il publie en mémoire.

L'émulateur Azure Service Bus n'est pas utilisé : il impose l'acceptation des licences de l'émulateur et de SQL Edge.

## Lancer un service hors conteneur

```bash
docker compose -f deploy/local/compose.yaml up -d postgres keycloak otel-dashboard
dotnet run --project src/Services/Referentiels/src/Sepp.Referentiels.Infrastructure
```

## Comptes techniques

Le client confidentiel `veilla-integrations` (identifiants client, rôle `integrations`, audience `sepp-api`) permet au service Intégrations d'appeler l'API DIMONA de Personnes. Son secret de développement est défini dans le realm local et repris dans `compose.yaml` ; hors poste de développement, il vient de Key Vault.

Les clients confidentiels `veilla-documents` (rôle `documents`), `veilla-communications` (rôle `communications`) et `veilla-affilies` (rôle `affilies`, permission `integrations:bce-lire`, lecture des données BCE chez Intégrations) fournissent de même le compte technique des services Documents, Communications et Affiliés ; les deux premiers lisent l'affilié et la personne (`affilie:lire`, `personne:lire`) pour la langue des documents et les coordonnées des destinataires. Leurs secrets de développement (`documents-local-dev`, `communications-local-dev`, `affilies-local-dev`) sont repris dans `compose.yaml` (`Documents__CompteTechnique__*`, `Communications__CompteTechnique__*`, `Affilies__CompteTechnique__*`).

Pour tester le flux DIMONA en local : associer un numéro BCE à un affilié (`PUT /api/v1/correspondances`, utilisateur `gestionnaire`), puis lancer `POST /api/v1/flux/dimona/executions`. Le simulateur produit deux entrées et une sortie pour cet employeur, et une entrée rejetée pour un employeur non affilié.

## Documents : une instance par zone

Par défaut, un seul service `documents` (port 5119) sert les trois zones, comme avant. Dans le realm local uniquement, le compte de service de `veilla-documents` porte aussi le rôle `documents-medicale` : l'instance unique de développement sert les trois zones, donc la copie du dossier (SAN-43) et le carnet de vaccination (SAN-50) restent servis. La séparation des droits (`dossier-sante:transmettre-documents` réservé au rôle `documents-medicale`, décision #356, ARC-06) n'est réelle qu'en déploiement par zone, où le rôle `documents` seul n'a pas ce droit.

Pour reproduire la production (« une instance par zone », ADR 0009) :

```bash
docker compose -f deploy/local/compose.yaml -f deploy/local/compose.documents-par-zone.yaml --profile par-zone up -d --build
```

- `documents` (port 5119) ne sert plus que la zone standard ;
- `documents-medicale` (port 5122, profil `par-zone`) sert la zone médicale, avec sa base `documents_medicale`, son stockage de fichiers propre et le client confidentiel `veilla-documents-medicale` (rôle technique `documents-medicale`, secret de développement `documents-medicale-local-dev`), chargé de lire le contenu clinique chez Surveillance médicale (le compte `veilla-documents` garde néanmoins le rôle `documents-medicale` en local, sans effet puisque `documents` ne sert plus que la zone standard).

Le script `init-databases.sql` ne s'exécute qu'à la création du volume PostgreSQL, et le realm qu'à l'import initial de Keycloak : sur un environnement existant, créez la base (`docker compose -f deploy/local/compose.yaml exec postgres psql -U sepp -c "CREATE DATABASE documents_medicale"`) et recréez le volume ou le conteneur Keycloak pour obtenir le nouveau client.

## Portées OIDC

Le realm déclare les portées `profile`, `email`, `offline_access` (rôle `offline_access` donné aux utilisateurs de test) et `sepp-api` : les portails demandent `openid profile email offline_access`.
