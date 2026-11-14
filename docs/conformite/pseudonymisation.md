# Pseudonymisation hors production et environnement de formation (NF-14)

Ce document répond à l'exigence NF-14 (ticket #166) : aucune donnée réelle hors production. Il couvre trois volets :
les simulateurs imposés hors production, l'outil de pseudonymisation d'une copie de production pour la recette et la
formation, et l'option d'environnement de formation.

Principe directeur : **les données de production ne quittent jamais la production sans être pseudonymisées**, et
l'outil ne tourne jamais sur la production elle-même. Les jeux de démonstration sont une autre voie (#393, données
entièrement fictives) ; la pseudonymisation sert quand la recette ou la formation exige des volumes et des cas réels.

## 1. Simulateurs imposés hors production

Deux lignes de défense, indépendantes :

1. **Terraform** (`infra/variables.tf`) : `adapter_mode` vaut `Simulateur` par défaut et `Reel` est refusé par la
   validation tant que `environment` n'est pas `prod`.
2. **Service** (`GardeAdaptateurs.RefuserReelHorsProduction`, socle) : au démarrage, tout adaptateur configuré à `Reel`
   (`<Section>:Adaptateurs:<Port>`) dans un environnement autre que `prod` fait échouer le service, que l'adaptateur réel
   soit implémenté ou non. `SEPP__ENVIRONNEMENT` absent (développement local, tests) est assimilé à un environnement
   hors production. Les services concernés : Communications, Documents, Intégrations, Planification, Réintégration
   (Surveillance médicale n'accepte que `Simulateur`).

**Pas de dérogation pour la préproduction.** Ni le cahier des charges ni les ADR n'en prévoient ; la préproduction a la
topologie de la production mais ses données doivent être fictives ou pseudonymisées, donc ses adaptateurs aussi. Si une
recette de raccordement réel devenait nécessaire (ex. Dimona en préproduction), elle exigerait une décision
d'architecture (ADR) et un indicateur de dérogation explicite et journalisé, ce qui n'est pas implémenté.

## 2. Outil de pseudonymisation

### 2.1 Cadre d'emploi

- L'outil `tools/pseudonymisation/Sepp.Pseudonymisation` (console .NET) travaille sur une **copie restaurée** des bases
  de production, dans une zone d'exploitation isolée (même zone de sécurité que la source pour les zones médicale et
  psychosociale), jamais sur la base de production.
- Séquence : restaurer les sauvegardes dans des serveurs temporaires, lancer l'outil, vérifier le rapport, puis
  seulement alors exposer la copie à la recette ou à la formation ; détruire la copie brute (avant pseudonymisation)
  et les clés temporaires.
- L'outil tourne en une transaction par service ; en cas d'erreur, rien n'est modifié. Une copie déjà pseudonymisée est
  refusée (table `pseudonymisation_execution`) : restaurer une copie neuve pour rejouer.
- Il ne fournit pas d'anonymisation au sens du RGPD : les données restent des données personnelles pseudonymisées
  (la clé secrète permet de rejouer la correspondance). La copie reste donc soumise aux mêmes mesures de sécurité que
  la production, sauf décision contraire du délégué à la protection des données après destruction de la clé.

### 2.2 Garde contre la production

Avant toute écriture (`GardeCible`) :

| Contrôle | Effet |
|---|---|
| `Pseudonymisation:Environnement` absent, ou `prod`, `production`, `prd`, `live` | refus |
| `SEPP__ENVIRONNEMENT` du processus vaut `prod` | refus |
| Hôte ou nom de base contenant le mot `prod`, `production` ou `prd` (mot entier : `preprod` n'est pas concerné) | refus |
| Hôte dans `Pseudonymisation:HotesInterdits` (exact ou `*.suffixe`) | refus |
| `Pseudonymisation:HotesAutorises` renseigné et hôte absent | refus |
| Confirmation : `--confirmer <environnement>` doit reprendre exactement le nom de l'environnement cible | refus sinon |

Recommandation d'exploitation : renseigner `HotesAutorises` avec le suffixe du serveur temporaire de copie.

### 2.3 Techniques

Les règles sont déclarées par service dans `CatalogueRegles.cs` (colonne par colonne). Les valeurs fictives sont
produites par HMAC-SHA256 avec une **clé secrète** (`PSEUDONYMISATION__CLE`, base64, 256 bits, jamais dans le dépôt) :

| Technique | Colonnes | Détail |
|---|---|---|
| Remplacement cohérent des noms | noms, prénoms, contacts | liste fictive choisie par HMAC de l'identifiant de ligne ; même identifiant, même valeur |
| Adresses | rue, numéro, boîte | rue fictive ; code postal, localité et pays sont **conservés** (granularité commune) |
| Courriel, téléphone | personnes, contacts | `prenom.nom.NNNN@exemple.invalid` (domaine réservé, jamais routé) ; téléphone `+32 2 000 NN NN` (non attribuable) |
| NISS fictif valide | `personne.niss_chiffre` | 11 chiffres avec contrôle modulo 97 (règle du préfixe 2 après 2000), cohérent avec la date de naissance décalée et le sexe, unique (résolution des collisions) |
| Index aveugle | `personne.niss_hash` | recalculé (HMAC) sur le NISS fictif avec la clé d'index de l'environnement cible |
| Décalage des dates | `date_naissance`, `date_deces` | décalage de 1 à 120 jours (non nul), propre à la personne : même décalage partout |
| Numéros BCE / UE | `affilie.numero_bce`, `unite_etablissement.numero_ue_bce`, `ecart_synchronisation.numero_bce` | numéros fictifs valides (modulo 97), unicité garantie, **même valeur fictive** pour un même numéro d'origine dans tout le service |
| Dénominations | `affilie`, `unite_etablissement`, `site`, `groupe` | « Entreprise fictive 123456 », etc. ; coordonnées des sites déplacées de quelques kilomètres |
| Textes libres en clair | `ecart_synchronisation.detail`, `preuve_destruction.motif_validation`, références de destinataire | remplacés par un texte neutre |
| Historiques JSON | `modification_affilie.avant` / `apres` | remplacés par `{}` |
| Identifiants d'utilisateur | `created_by`, `updated_by` de **toutes** les tables, `auteur`, `gestionnaire`, `professionnel`… | jeton stable `utilisateur-<hex>` ; `system` conservé |
| Files d'événements | `outbox_message`, `inbox_message`, `republication` | vidées (aucune republication de la copie vers un bus) |

**Cohérence référentielle entre services.** Les identifiants techniques (UUID de personne, d'affilié, de dossier) sont
**conservés** : ils ne portent aucune information d'identité (ARC-06) et c'est la clé de jointure entre services,
dont les projections locales. La « table de correspondance à clé secrète » est donc la fonction HMAC elle-même : une
même valeur d'origine donne la même valeur fictive dans chaque service, sans qu'aucune table de correspondance ne soit
stockée (rien à protéger, rien qui permette de revenir en arrière sans la clé). Seules les valeurs qui doivent rester
uniques (NISS, BCE) passent par une correspondance en mémoire, propre à l'exécution.

### 2.4 Champs chiffrés (ADR 0012)

Les champs chiffrés (`FieldEncryptor`, AES-256-GCM, format `v2:<dek>:…` ou `v1:` historique) sont **déchiffrés par les
clés de la source** puis **rechiffrés par les clés de l'environnement cible** : la copie n'est jamais lisible avec les clés
de production, et la clé racine de production n'est pas nécessaire à la cible. L'outil charge les deux jeux de clés par
`ChiffrementChamps` (section `Source:Chiffrement` et `Cible:Chiffrement`, même format que la section du service).
Selon la colonne :

- **conservé et rechiffré** : valeur sans caractère identifiant une fois la personne pseudonymisée (`etat_particulier`,
  lecture tuberculinique) ;
- **supprimé** : texte libre médical remplacé par un texte neutre chiffré (observation clinique, anamnèse, commentaires,
  justifications, recommandations, notes, titres et descriptions de pièces jointes, contenu eHealth, paquets de transfert,
  objets et réponses Fedris) ;
- **JSON neutralisé** : la structure et les valeurs numériques sont conservées, seules les chaînes des propriétés
  libres (`valeur`, `texte`, `description`, `agentCausal`, `derniereDecision`) sont remplacées ;
- **mis à NULL** : historique des mutations du registre national (`avant_chiffre`, `apres_chiffre`).

Pour la source en Azure, l'outil déballe les DEK avec la clé racine du service : lui accorder temporairement
« Key Vault Crypto Service Encryption User » sur cette clé et la retirer ensuite. La clé de l'index aveugle cible
(`Cible:IndexAveugle`) est enveloppée par la clé racine **cible** (procédure de `infra/README.md`).

### 2.5 Mise en œuvre

```bash
export PSEUDONYMISATION__CLE=<base64 256 bits, propre à l'opération>
dotnet run --project tools/pseudonymisation/Sepp.Pseudonymisation -- --confirmer recette --a-blanc
dotnet run --project tools/pseudonymisation/Sepp.Pseudonymisation -- --confirmer recette [--services personnes,affilies]
```

Configuration (variables d'environnement `Pseudonymisation__…` ou `appsettings.json` local, jamais committé) :

```text
Pseudonymisation:Environnement                         recette | formation | preprod | test
Pseudonymisation:HotesAutorises:0                      *.copie-temporaire.exemple
Pseudonymisation:Services:<service>:Connexion          copie restaurée du service
Pseudonymisation:Services:<service>:Source:Chiffrement section de chiffrement de la production (services chiffrés)
Pseudonymisation:Services:<service>:Cible:Chiffrement  section de chiffrement de l'environnement cible
Pseudonymisation:Services:personnes:Cible:IndexAveugle clé d'index enveloppée par la clé racine cible
```

Services : `personnes`, `affilies`, `surveillance-medicale`, `reintegration`. `--a-blanc` exécute tout puis annule la
transaction : seul le rapport est produit. Le rapport indique les lignes traitées par table, les tables vidées, le
nombre d'identifiants d'utilisateur remplacés et **la liste des colonnes texte sans règle**, à revoir avant exposition.

### 2.6 Ce qui reste (hors périmètre de cette version)

L'outil couvre Personnes, Affiliés, Surveillance médicale et Réintégration. Il **reste à traiter** avant d'exposer une
copie complète :

- **Autres services** : Documents (documents et métadonnées, contenu des fichiers du stockage Blob, à régénérer ou
  vider), Communications (adresses et messages de notification), Intégrations (charges utiles chiffrées des échanges
  BCE, Dimona, registre national), Planification (convocations et agendas), Obligations, Postes de risques, Audit (journal
  d'accès contenant des identifiants d'utilisateurs et des motifs), Référentiels (aucune donnée personnelle attendue),
  ainsi que les BFF (aucune base). Les stockages Blob, les files Service Bus, les journaux et les sauvegardes de la copie
  sont à purger ou à ne pas restaurer.
- **Colonnes conservées volontairement** : dates d'événements (examens, incapacités, occupations), références
  externes (`reference_dimona`, `reference_fedris`, références de mutation), codes de référentiel, statuts, code postal et
  localité. Leur conservation garde la cohérence entre services (projections, obligations) ; un décalage uniforme par
  personne de toutes les dates est une évolution possible, à coordonner avec Planification et Obligations.
- **Risque de ré-identification résiduel** : combinaison de dates d'événements, de codes d'agents et de lieux dans les
  petites populations. La copie reste une donnée personnelle pseudonymisée ; elle ne sort pas des zones et accès
  prévus. Le NISS fictif respecte les règles de forme et peut, par hasard, coïncider avec un numéro réel : ne jamais
  l'envoyer à un système externe (d'où les simulateurs imposés).
- Accès et durée de conservation de la copie, décision du délégué à la protection des données, et exécution réelle
  sur une copie de production : à valider (aucune exécution réelle n'a eu lieu ; seuls des tests sur bases fictives
  existent).

### 2.7 Tests

- Unitaires (`Sepp.Pseudonymisation.Tests`) : déterminisme, NISS et BCE valides et uniques, décalage de date, neutralisation
  JSON, rechiffrement, garde contre la production.
- Intégration Testcontainers (`Sepp.Pseudonymisation.Integration.Tests`) : quatre bases PostgreSQL migrées par les
  services eux-mêmes, peuplées de valeurs « ORIGINAL… » (clés générées pour le test), pseudonymisées par l'outil par sa
  configuration ; vérifie qu'aucune valeur d'origine ne reste dans les colonnes ciblées (champs chiffrés illisibles avec
  la clé source), la validité et l'unicité des NISS, le recalcul de l'index aveugle, la cohérence des identifiants entre
  services, le refus de rejouer et de viser la production, et le mode à blanc.

## 3. Environnement de formation

Deux options pour former les gestionnaires et les médecins :

| Option | Avantages | Inconvénients |
|---|---|---|
| **A. Réutiliser la recette** (recommandée au démarrage) | aucun coût d'infrastructure ni de racine Terraform ; une seule copie pseudonymisée à entretenir | la formation et la recette partagent les données et les versions : jeux de données à réinitialiser entre sessions, risque de perturber la recette |
| **B. Racine Terraform dédiée `infra/environments/formation`** | isolement complet, version stable, réinitialisation à la demande par restauration d'une copie pseudonymisée, droits propres aux formateurs | coût d'un environnement complet (PostgreSQL, Service Bus, Container Apps), second état Terraform, une valeur de plus à gérer |

Pour l'option B, les travaux ne sont pas triviaux et **ne sont pas faits ici** : la variable `environment` n'accepte que
`dev`, `test`, `recette`, `preprod` ou `prod` (`infra/variables.tf`, ses tests et `infra/README.md`) ; il faudrait ajouter
`formation`, copier la racine de `recette` avec ses valeurs réduites (SKU minimaux, `adapter_mode = "Simulateur"`),
créer l'environnement GitHub et l'identité de déploiement, et écrire une procédure de réinitialisation (restauration d'une
copie pseudonymisée puis exécution de l'outil avec `--confirmer formation`). `GardeAdaptateurs` et `GardeCible`
traitent déjà `formation` comme hors production.

Décision proposée : option A tant que le volume de formation reste faible ; passer à l'option B quand les formations
deviennent récurrentes ou quand la recette doit rester stable. À valider par le responsable de projet.
