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
| Application interne | http://localhost:8081 | |
| Portail employeur | http://localhost:8082 | |
| Portail travailleur | http://localhost:8083 | |
| Keycloak (realm `veilla`) | http://localhost:8180 | console d'administration : `admin` / `admin-local-dev` |
| Tableau de bord OpenTelemetry | http://localhost:18888 | traces, journaux, métriques (ARC-47) |
| PostgreSQL | localhost:5432 | `sepp` / `sepp-local-dev`, une base par service |

## Utilisateurs de test

Définis dans [`keycloak/veilla-realm.json`](keycloak/veilla-realm.json), mot de passe commun `veilla-dev` :
`cpmt`, `cpmt.dirigeant`, `infirmier`, `cpap`, `securite`, `gestionnaire`, `planificateur`, `admin` (administrateur fonctionnel), `dpo`, `employeur`, `travailleur`.

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

Sans configuration de bus, l'outbox publie en mémoire (ADR 0004). L'émulateur Azure Service Bus n'est pas inclus par défaut : il impose l'acceptation des licences de l'émulateur et de SQL Server, à faire explicitement par chaque développeur.

## Lancer un service hors conteneur

```bash
docker compose -f deploy/local/compose.yaml up -d postgres keycloak otel-dashboard
dotnet run --project src/Services/Referentiels/src/Sepp.Referentiels.Infrastructure
```

## Comptes techniques

Le client confidentiel `veilla-integrations` (identifiants client, rôle `integrations`, audience `sepp-api`) permet au service Intégrations d'appeler l'API DIMONA de Personnes. Son secret de développement est défini dans le realm local et repris dans `compose.yaml` ; hors poste de développement, il vient de Key Vault.

Les clients confidentiels `veilla-documents` (rôle `documents`), `veilla-communications` (rôle `communications`) et `veilla-affilies` (rôle `affilies`, permission `integrations:bce-lire`, lecture des données BCE chez Intégrations) fournissent de même le compte technique des services Documents, Communications et Affiliés ; les deux premiers lisent l'affilié et la personne (`affilie:lire`, `personne:lire`) pour la langue des documents et les coordonnées des destinataires. Leurs secrets de développement (`documents-local-dev`, `communications-local-dev`, `affilies-local-dev`) sont repris dans `compose.yaml` (`Documents__CompteTechnique__*`, `Communications__CompteTechnique__*`, `Affilies__CompteTechnique__*`).

Pour tester le flux DIMONA en local : associer un numéro BCE à un affilié (`PUT /api/v1/correspondances`, utilisateur `gestionnaire`), puis lancer `POST /api/v1/flux/dimona/executions`. Le simulateur produit deux entrées et une sortie pour cet employeur, et une entrée rejetée pour un employeur non affilié.

## Portées OIDC

Le realm déclare les portées `profile`, `email`, `offline_access` (rôle `offline_access` donné aux utilisateurs de test) et `sepp-api` : les portails demandent `openid profile email offline_access`.
