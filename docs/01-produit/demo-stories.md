---
titre: Démo commerciale veilla, user stories par écran et par lot (épique #389)
auteur: product-owner
statut: en revue
date: 2026-11-14
---

# Démo commerciale veilla : user stories par écran et par lot

Découpage de la démo commerciale de l'épique #389 en stories prêtes à développer, une par écran ou par lot (A à H), plus les stories transverses. Ce découpage applique la décision DA-02 du responsable produit, du 2026-11-14.

Chaque bloc de la section 5 se copie tel quel dans un ticket GitHub. Le titre est la ligne `###`, et le reste du bloc forme le corps. La référence `DEMO-xx` sert aux dépendances entre stories, en attendant les numéros GitHub.

## 1. Sources

| Source | Usage |
|---|---|
| Plan de démo `demo-app-interne.md` (scratchpad de l'orchestrateur) | Inventaire des API, parcours P1 à P11, lots A à H |
| `socle-front-api.md` (scratchpad) | `chargeable`, `InterneApi`, rôles, i18n par portée, outils de test |
| `CLAUDE.md` | Conventions du projet, zones, jalons, tickets |
| `tools/github-backlog/data.py` et `state.json` | Texte des exigences (cahier des charges v1.1) et numéro de leur ticket |
| `docs/cahier-des-charges-sepp.pdf` | §3.1 à §3.3 (profils et matrice), contrôle des exigences citées |
| `docs/securite/matrice-droits.csv`, `app-interne/src/app/roles.ts` | Droits par rôle |
| `docs/securite/authentification-forte.md`, ADR 0009, ADR 0013 | NF-01, documents par zone, BFF interne |
| Code : `src/Bff/Interne/.../Ecrans/`, endpoints des services, `app-interne`, `portail-employeur` | Existant et manques |
| PR #438 (lot G, #393, en cours de fusion) : `Catalogue.cs`, `EtapePlanification.cs`, `EtapesSanteEtReintegration.cs`, `EtapesReferentielsEtDossiers.cs` | Données de démo |
| Tickets #351, #356 et #436 | Liens et blocages connus |

Les documents `docs/00-pilotage/etat-des-lieux-backlog.md` et `docs/architecture/etat-des-lieux-architecture.md` n'existent pas dans le dépôt (main du 2026-11-14). Le chemin critique de la section 4 est donc établi à partir du plan et du code (hypothèse H1).

## 2. Données de démo de référence

Les critères d'acceptation s'appuient sur ces données. Les clés `boulangerie-N`, `construct-N` et `zorg-N` sont celles du catalogue de la PR #438. Le nom de la personne correspondante se lit dans `deploy/local/demo/manifeste.json` (question Q6).

**Comptes Keycloak locaux** (mot de passe `veilla-dev`)

| Compte | Personne | Rôle |
|---|---|---|
| `cpmt` | Dr Anne Martin | CPMT, ressource « Dr Anne Martin - Bruxelles », gestionnaire des 16 dossiers de santé |
| `cpmt.dirigeant` | Dr Marc Dubois | CPMT et CPMT dirigeant, ressource « Dr Marc Dubois - Gand » |
| `cpmt.mfa` | Hélène Fontaine | CPMT avec un OTP de démonstration (NF-01) |
| `infirmier` | Sophie Lambert | Infirmière, ressource « Inf. Sophie Lambert - Bruxelles » |
| `assistant` | Laura Maes | Assistante médicale |
| `planificateur` | Nicolas Lemaire | Planificateur |
| `responsable` | Philippe Simon | Responsable de centre |
| `gestionnaire` | Julie Peeters | Gestionnaire de dossiers |
| `admin` | Claire Dupont | Administratrice fonctionnelle |
| `dpo` | Isabelle Renard | DPO |
| `securite` | Karim Benali | Conseiller en prévention sécurité |
| `employeur` | Eva Claes | Employeur de la Boulangerie et de Construct |
| `travailleur` | Nadia Lambert (`boulangerie-0`) | Travailleuse (hypothèse H2) |

**Affiliés et lieux**

| Affilié | BCE | Langue et régime | Site | Travailleurs |
|---|---|---|---|---|
| Boulangerie Démo SRL | 0999.010.126 | FR, Bruxelles-Capitale, CP 118.03, catégorie B | Fournil central | 14 |
| Zorg Demo vzw | 0999.020.222 | NL, néerlandais, CP 331, catégorie C | Woonzorgcentrum Demo | 14 |
| Construct Démo SA | 0999.030.318 | FR, français, CP 124, catégorie A | Chantier-dépôt de Liège | 12 |

- La Boulangerie et Construct font partie du « Groupe Demo Benelux ».
- Deux lieux : « Centre SEPP Bruxelles » et « Centre SEPP Gand ». Les créneaux vont de J à J+30. Chez les conseillers, la plage 11:30–12:00 est réservée aux urgences et la plage 16:30–17:30 aux examens de reprise.

**Situations clés**

| Clé | Situation |
|---|---|
| `boulangerie-0` (Nadia Lambert) | Boulanger de nuit (travail de nuit élevé, manutention de charges moyen, denrées alimentaires faible). RDV à J avec Dr Anne Martin à Bruxelles. Dossier de santé. Décision « Apte » signée (évaluation préalable), avec documents. Demande de copie déposée depuis le portail |
| `boulangerie-1`, `-2`, `construct-0`, `-1` | Les quatre autres RDV de J chez Dr Anne Martin |
| `boulangerie-10` | Décision **en brouillon** à signer en direct : apte avec mesures, `AMENAGEMENT_POSTE` |
| `boulangerie-11` | **Examen en cours** (`EVALUATION_PERIODIQUE`, ouvert à J). Biométrie saisie : IMC 24,5, TA 122/78 |
| `boulangerie-12` | Incapacité depuis J-150. Trajet demandé par le travailleur à J-120, sans invitation : **délai dépassé** |
| `boulangerie-13` | État particulier « travail de nuit » |
| `construct-7` | Décision « inaptitude temporaire », valable 45 jours |
| `construct-9` | Incapacité depuis J-70. Trajet demandé à J-60, invitation recommandée à J-55, **évaluation en attente** |
| `construct-10` | Reprise annoncée par l'employeur : absence depuis J-45, reprise à J+7 |
| `zorg-6`, `-7`, `-9`, `-10` | Hépatite B : dose 1 à J-240, dose 2 à J-210, **rappel dû** (environ 60 jours de retard). Lot `DEMO-HB-001` au centre de Bruxelles |
| `zorg-6`, `-7`, `-8`, `construct-6`, `-8`, `boulangerie-9` | Décisions signées, avec documents FR ou NL |
| Construct, « Conducteur de travaux » | Proposition poste-risque **refusée** (ajout de `EX.SECU.CONDUITE`), motif « avis du comité PPT manquant » |
| Compte `employeur` | Proposition de risque **soumise** depuis le portail, en attente du CPMT |

## 3. Socle commun à toutes les stories

Chaque story le respecte, et sa rubrique « Accessibilité et i18n » ne précise que les points propres à l'écran.

- **Front** :
  - composants autonomes, OnPush et signaux ;
  - `chargeable()` et `@switch (etat().statut)` ;
  - `<vl-chargement />` et `<vl-erreur-api>` ;
  - `versErreurApi` pour les envois ;
  - codes d'erreur traduits dans `codes-erreur.ts` et `errors.*` ;
  - dates affichées avec `dateLocale`, dans le fuseau Europe/Brussels ;
  - styles `vl-*`.
- **Propriété des fichiers** : chaque story ne modifie que :
  - son dossier `app-interne/src/app/<feature>/` ;
  - sa portée i18n `public/i18n/<feature>/{fr,nl,de}.json` ;
  - son fichier d'écran composite `src/Bff/Interne/.../Ecrans/Ecran<X>.cs`.

  Les fichiers partagés (`codes-erreur.ts`, `public/i18n/{fr,nl,de}.json`) reçoivent des ajouts seulement, avec un rebase fréquent.
- **BFF interne** :
  - les composites utilisent `Annuaire` et `SectionTolerante<T>` ;
  - une section refusée (403) est absente de la réponse, ce n'est pas une erreur ;
  - une section indisponible est signalée sans faire échouer l'écran ;
  - tests d'intégration avec services aval simulés ;
  - instantané OpenAPI à jour ;
  - aucune logique métier (ARC-43).
- **Sécurité** :
  - les services font foi ;
  - le front masque ce que le rôle ne peut pas utiliser (`roleGuard`, `*vlSiRole`) ;
  - `Security.cs` et `matrice-droits.csv` ne changent qu'avec une justification écrite dans le ticket ;
  - aucune donnée de santé hors zone médicale (ARC-06).
- **Accessibilité (WCAG 2.1 AA, NF-50)** :
  - un seul `h1` traduit par page ;
  - tableaux avec `caption` et en-têtes `scope` ;
  - `role="status"` pour le chargement, `role="alert"` pour les erreurs ;
  - libellés et `aria-describedby` sur chaque champ ;
  - focus placé sur le premier champ en erreur, ou sur le message de succès, après un envoi ;
  - dialogues en `<dialog>` natif ;
  - statuts lisibles sans la couleur (texte du badge) ;
  - tout se fait au clavier.
- **i18n** :
  - FR, NL et DE obligatoires pour l'application interne ;
  - FR, NL, DE et EN pour les portails ;
  - les termes médicaux NL et DE sont marqués « à valider » dans le fichier de traduction ;
  - aucune clé manquante dans aucune langue (test).
- **Tests** :
  - une spec Vitest par écran : chargement, erreur, rôle, envoi ;
  - `provideInterneTesting` et `HttpTestingController` ;
  - les scénarios Gherkin deviennent des tests (unitaires, d'intégration BFF ou vérification de bout en bout DEMO-T2).
- **Definition of Done** :
  - build sans avertissement, lint et tests verts ;
  - garde-fous d'architecture verts (OpenAPI, matrice des droits, catalogue des événements) ;
  - scénarios vérifiés sur les données de démo (`deploy/local/demo.ps1`, projet compose distinct) ;
  - ticket commenté et PR `Closes #n`.

## 4. Ordonnancement : chemin critique et parallélisme

### 4.1 Vue d'ensemble

| Réf. | Story | Prio. | Taille | Jalon | Vague | Dépend de |
|---|---|---|---|---|---|---|
| — | #393 Données de démo (lot G, PR #438) | P0 | — | — | 0 | — |
| — | #436 BFF travailleur, liste des documents vide | P0 | S | — | 0 | — |
| DEMO-A1 | Personnes : recherche par nom | P0 | M | Lot 1 | 1 | — |
| DEMO-C0 | Surveillance médicale : examens en cours et décisions en brouillon | P0 | M | Lot 2 | 1 | — |
| DEMO-F1 | Paramètres légaux, calendrier, nomenclatures | P0 | M | Lot 1 | 1 | — |
| DEMO-F2 | Référentiel des risques et règles | P0 | M | Lot 1 | 1 | — |
| DEMO-F3 | Modèles de documents | P0 | M | Lot 2 | 1 | — |
| DEMO-A5 | Personnes : lecture groupée des résumés | P1 | S | Lot 1 | 1 | — |
| DEMO-C4 | BFF interne : documents de la zone médicale | P1 | M | Lot 2 | 1 (ADR d'abord) | — |
| DEMO-H1 | Documents au portail employeur | P1 | M | Lot 2 | 1 | — |
| DEMO-T1 | Accès renforcé : montée en niveau et motif d'accès | P1 | M | Lot 1 | 1 | — |
| DEMO-A2 | Recherche globale | P0 | M | Lot 1 | 2 | A1 |
| DEMO-A3 | Dossier travailleur | P0 | L | Lot 1 | 2 | — (A2 pour l'accès) |
| DEMO-B1 | Agenda et détail d'un rendez-vous | P0 | L | Lot 2 | 2 | — |
| DEMO-C1 | Vue consultation et examen | P0 | L | Lot 2 | 2 | — (C0 pour reprendre un examen) |
| DEMO-D1 | Tableau de bord par rôle | P0 | M | Lot 2 | 2 | C0 |
| DEMO-D2 | File de travail du CPMT | P0 | M | Lot 2 | 2 | C0 |
| DEMO-A4 | Fiche affilié | P1 | M | Lot 1 | 2 | — |
| DEMO-C3 | Vaccinations | P1 | M | Lot 2 | 2 | — |
| DEMO-E1 | Réintégration : tableau des délais et dossier d'incapacité | P1 | M | Lot 3 | 2 | — |
| DEMO-F4 | Intégrations et DIMONA simulée | P1 | M | Lot 1 | 2 | — |
| DEMO-F5 | Journal d'audit | P1 | M | Lot 1 | 2 | — |
| DEMO-B2 | Obligations à planifier et prise de rendez-vous | P0 | L | Lot 2 | 3 | B1 (affichage dans l'agenda) |
| DEMO-C2 | Décision d'évaluation de santé et signature | P0 | L | Lot 2 | 3 | C1, F3, #436 |
| DEMO-B3 | Salle d'attente | P1 | M | Lot 2 | 3 | B1 |
| DEMO-E2 | Trajet de réintégration | P1 | M | Lot 3 | 3 | E1 |
| DEMO-T4 | Présentation commerciale | P1 | M | Lot 3 | 3 et 4 | — (contenu final après T2) |
| DEMO-T2 | Vérification de bout en bout P1 à P11 | P0 | L | Lot 3 | 4 | Toutes les P0, #393, #436 |
| DEMO-T3 | Script de démo de 15 à 20 minutes | P0 | M | Lot 3 | 5 | T2 |

### 4.2 Chemin critique

```
#393 données ─┐
C1 consultation (L) ──► C2 décision et signature (L) ──► T2 vérification (L) ──► T3 script (M)
                         ▲            ▲
             F3 modèles ─┘   #436 ────┘
```

- **C1 puis C2** forment la chaîne la plus longue : P4, le cœur de la démo, enchaîne examen, décision, signature, document et portail. À démarrer dès la vague 2, avec un développeur dédié.
- **A1 puis A2** constituent un second chemin, plus court : la recherche par nom est le premier geste de la démo (P2).
- **C0** débloque D1 et D2, et doit être livré tôt.
- **#436** (bug) et **#393** (données) sont des prérequis de T2 qui ne relèvent pas de ce découpage.

### 4.3 Ce qui se fait en parallèle

- **Vague 1**, en même temps : A1, C0, F1, F2, F3, A5, H1, T1 et l'ADR de C4. Ce sont des services, des BFF et des dossiers front distincts, sans fichier commun.
- **Vague 2**, avec un développeur par dossier front :
  - A2 et A3 (`search/`, `travailleurs/`) ;
  - B1 (`planification/`) ;
  - C1 (`consultation/`) ;
  - D1 (`dashboard/`) et D2 (`travail/`) ;
  - A4 (`affilies/`), C3 (`vaccinations/`), E1 (`reintegration/`), F4 et F5 (`admin/`, sous-routes distinctes).
- **Vague 3** :
  - B2 et B3 suivent B1, dans le même dossier ;
  - C2 suit C1, dans le même dossier ;
  - E2 suit E1.
- **Attention aux conflits** :
  - F1 à F5 partagent `admin/admin.routes.ts` : déclarer les sous-routes d'abord, dans F1 ;
  - B1 à B3 partagent `planification.routes.ts` ;
  - C1 à C3 touchent `consultation/` et `vaccinations/`.

## 5. Stories

---

### [DÉMO A] Personnes : rechercher des travailleurs par nom et date de naissance

Réf. : DEMO-A1

- **Jalon** : Lot 1 – Socle
- **Labels** : `type:exigence-fonctionnelle`, `svc:personnes`
- **Priorité** : P0
- **Taille** : M
- **Zone** : standard
- **Parcours démo** : A (P2)
- **Exigences** : NF-51 (#178, recherche globale), NF-31 (#171), DAT-06 (#242)
- **Tickets liés** : #389, #2

**Corps**

**Besoin** : En tant que CPMT, planificateur ou gestionnaire, je veux retrouver un travailleur par son nom, son prénom ou sa date de naissance, afin d'ouvrir son dossier sans connaître son NISS.

**Écrans et routes** : aucun. C'est l'API utilisée par DEMO-A2.

**Écran composite du BFF et appels d'API** :
- Nouvel endpoint `GET /api/v1/personnes?q=&dateNaissance=&affilieId=&page=&taille=`, avec la permission `personne:lire`.
- Il renvoie une page de résumés : `id`, `prenom`, `nom`, `dateNaissance`, NISS masqué (`85.07.12-***.**`, jamais en clair), affiliés des occupations actives (`affilieId`).
- Règles :
  - au moins 2 caractères ;
  - recherche insensible à la casse et aux accents ;
  - préfixe sur chaque mot, dans n'importe quel ordre (« lambert nadia » = « Nadia Lambert ») ;
  - taille de page de 20 par défaut, 50 au plus ;
  - tri par pertinence, puis par nom.
- Le périmètre est le même que pour `GET /personnes/{id}` : un jeton externe ne voit que son périmètre.

**Manques backend à combler** :
- Le cas d'usage `RechercherPersonnes`, son index (nom et prénom normalisés) et l'endpoint.
- Le choix de l'index (par exemple `unaccent` avec `pg_trgm`) revient à l'architecte. Une extension PostgreSQL nouvelle doit être justifiée (question Q4).

**Droits par rôle** :
- `personne:lire` : cpmt, cpmt-dirigeant, infirmier, assistant-medical, cp-*, cpap, cpap-dirigeant, gestionnaire, planificateur.
- Les autres internes reçoivent 403.
- Travailleur : `personne:lire-propres` uniquement, donc 403.

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Recherche de personnes par nom

  Contexte:
    Étant donné le jeu de données de démo chargé par deploy/local/demo.ps1

  Scénario: Recherche par nom
    Étant donné le CPMT Anne Martin connectée
    Quand elle recherche "lambert"
    Alors le résultat contient "Nadia Lambert", liée à l'affilié Boulangerie Démo SRL
    Et son NISS est masqué

  Scénario: Ordre des mots, casse et accents indifférents
    Étant donné le planificateur Nicolas Lemaire connecté
    Quand il recherche "LAMBERT nadia"
    Alors "Nadia Lambert" est le premier résultat

  Scénario: Filtre par date de naissance
    Étant donné le CPMT Anne Martin connectée
    Quand elle recherche "lambert" avec la date de naissance de Nadia Lambert du manifeste
    Alors un seul résultat est renvoyé

  Scénario: Requête trop courte
    Quand une recherche est faite avec "l"
    Alors la réponse est 400 avec le code "personnes.recherche-trop-courte"

  Scénario: Profil sans droit de lecture des personnes
    Étant donné la DPO Isabelle Renard connectée
    Quand elle recherche "lambert"
    Alors la réponse est 403

  Scénario: Le NISS ne sort jamais en clair
    Quand une recherche renvoie des résultats
    Alors aucune réponse ne contient de NISS complet
```

**Accessibilité et i18n** : sans objet (API). Les codes d'erreur sont ajoutés au catalogue traduit par DEMO-A2.

**Hors périmètre** :
- recherche phonétique ou floue ;
- recherche sur l'adresse ;
- recherche des personnes sorties depuis plus de 10 ans.

**Dépendances** : aucune. Bloque DEMO-A2.

---

### [DÉMO A] Personnes : lecture groupée des résumés de personnes

Réf. : DEMO-A5

- **Jalon** : Lot 1 – Socle
- **Labels** : `type:exigence-non-fonctionnelle`, `svc:personnes`, `svc:plateforme`
- **Priorité** : P1
- **Taille** : S
- **Zone** : standard
- **Parcours démo** : A, B, D, E (P1, P3, P8)
- **Exigences** : NF-31 (#171), ARC-35 (#203)
- **Tickets liés** : #389

**Corps**

**Besoin** : En tant qu'utilisateur de l'agenda, du tableau de bord ou de la réintégration, je veux que les noms des travailleurs s'affichent en un seul appel, afin que l'écran s'ouvre en moins de 2 secondes (NF-31).

**Écrans et routes** : aucun écran. L'appel est utilisé par `Annuaire` (BFF interne) et par les écrans B2 et E1 via le relais.

**Écran composite du BFF et appels d'API** :
- `POST /api/v1/personnes/resumes`, avec le corps `{ "ids": [...] }` (100 au plus).
- Il renvoie `[{ id, prenom, nom }]`. Les identifiants inconnus ou hors périmètre sont omis, sans erreur.
- `Annuaire.NomsPersonnesAsync` utilise cet appel au lieu de N appels `GET /personnes/{id}`.

**Manques backend à combler** : l'endpoint, puis l'adaptation d'`Annuaire` et de ses tests.

**Droits par rôle** : `personne:lire`, comme DEMO-A1.

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Lecture groupée des résumés

  Scénario: Cinq personnes en un appel
    Étant donné les 5 rendez-vous de J du CPMT Anne Martin
    Quand le BFF résout les noms de ces 5 personnes
    Alors un seul appel est fait au service Personnes
    Et les 5 noms, dont "Nadia Lambert", sont renvoyés

  Scénario: Identifiant inconnu
    Quand la lecture porte sur 2 identifiants, dont un inconnu
    Alors un seul résumé est renvoyé, avec un statut 200

  Scénario: Trop d'identifiants
    Quand la lecture porte sur 101 identifiants
    Alors la réponse est 400
```

**Accessibilité et i18n** : sans objet.

**Hors périmètre** : un cache entre requêtes ou entre utilisateurs, interdit par `Annuaire`.

**Dépendances** : aucune. Les composites de B1 et D1 fonctionnent sans elle, mais plus lentement.

---

### [DÉMO A] Recherche globale de travailleurs et d'affiliés

Réf. : DEMO-A2

- **Jalon** : Lot 1 – Socle
- **Labels** : `type:exigence-fonctionnelle`, `svc:plateforme`, `svc:personnes`, `svc:affilies`
- **Priorité** : P0
- **Taille** : M
- **Zone** : standard
- **Parcours démo** : A (P2)
- **Exigences** : NF-51 (#178), AFF-01 (#23), NF-31 (#171), NF-50 (#177), NF-40 (#175)
- **Tickets liés** : #389, #390

**Corps**

**Besoin** : En tant que CPMT, je veux taper un nom, un NISS ou un numéro BCE dans la recherche globale, afin d'ouvrir le dossier d'un travailleur ou la fiche d'un affilié en 3 clics au plus.

**Écrans et routes** :
- Champ de recherche du Shell (`search/global-search.ts`) et son raccourci clavier existant.
- Page `/recherche?q=` (`search/search-page.ts`, qui affiche aujourd'hui « non branché »).
- Deux sections de résultats :
  - **Travailleurs** : nom, date de naissance, NISS masqué, affilié ou affiliés, lien vers `/travailleurs/:id` ;
  - **Affiliés** : dénomination, BCE, ville, lien vers `/affilies/:id`.

**Écran composite du BFF et appels d'API** : `GET /api/v1/ecrans/recherche?q=` (`EcranRecherche.cs`). Le composite détecte le type de requête :
- 11 chiffres, avec ou sans séparateurs (NISS) : `POST personnes/recherche` ;
- 10 chiffres commençant par 0 ou 1 (BCE) : `GET affilies?bce=` ;
- sinon : `GET personnes?q=` (DEMO-A1) et `GET affilies?denomination=`, en parallèle.

Chaque section est une `SectionTolerante`. Les dénominations des affiliés des travailleurs sont résolues par `Annuaire`.

**Manques backend à combler** :
- DEMO-A1 ;
- l'implémentation d'`EcranRecherche` (répond 501 aujourd'hui).

**Droits par rôle** :
- La route `/recherche` est ouverte à tout interne authentifié.
- Section Travailleurs : `personne:lire`.
- Section Affiliés : `affilie:lire`.
- Admin-fonctionnel, direction et responsable-centre ne voient que les affiliés.
- La DPO ne voit aucune section, avec un message explicite.

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Recherche globale

  Scénario: Du nom au dossier en 3 clics au plus
    Étant donné le CPMT Anne Martin connectée sur le tableau de bord
    Quand elle saisit "Lambert" dans la recherche globale et valide
    Alors la page /recherche?q=Lambert liste "Nadia Lambert - Boulangerie Démo SRL"
    Quand elle choisit ce résultat
    Alors le dossier /travailleurs/{id de Nadia Lambert} s'ouvre

  Scénario: Recherche par NISS
    Étant donné le gestionnaire Julie Peeters connectée
    Quand elle recherche le NISS de Nadia Lambert du manifeste
    Alors exactement un travailleur est listé, Nadia Lambert

  Scénario: Recherche par numéro BCE
    Quand Julie Peeters recherche "0999.010.126"
    Alors la section Affiliés liste "Boulangerie Démo SRL"

  Scénario: Recherche par dénomination
    Quand Julie Peeters recherche "construct"
    Alors la section Affiliés liste "Construct Démo SA"

  Scénario: Service Affiliés indisponible
    Étant donné le service Affiliés arrêté
    Quand Anne Martin recherche "Lambert"
    Alors la section Travailleurs liste Nadia Lambert
    Et la section Affiliés affiche une erreur dans une zone role="alert"

  Scénario: Profil sans accès aux personnes
    Étant donné l'administratrice fonctionnelle Claire Dupont connectée
    Quand elle recherche "Boulangerie"
    Alors seule la section Affiliés est affichée

  Scénario: Aucun résultat
    Quand Anne Martin recherche "zzzz"
    Alors le message "Aucun résultat" est annoncé dans une zone role="status"
```

**Accessibilité et i18n** :
- Le nombre de résultats est annoncé (`role="status"`).
- Un `h2` par section et des listes sémantiques.
- Le champ de recherche a un libellé visible ou `aria-label`.
- Clés `search.*` en FR, NL et DE.

**Hors périmètre** :
- auto-complétion pendant la saisie ;
- recherche d'autres objets (RDV, documents) ;
- historique des recherches.

**Dépendances** : DEMO-A1. Mène à DEMO-A3 et DEMO-A4. Un lien cassé vers une page encore « à venir » est toléré jusqu'à leur livraison.

---

### [DÉMO A] Dossier travailleur

Réf. : DEMO-A3

- **Jalon** : Lot 1 – Socle
- **Labels** : `type:exigence-fonctionnelle`, `svc:plateforme`, `svc:personnes`, `svc:obligations`
- **Priorité** : P0
- **Taille** : L
- **Zone** : standard (seules les catégories de décision sortent de la zone médicale, ARC-06)
- **Parcours démo** : A (P2), et sert à P3, P4 et P5
- **Exigences** : AFF-22 (#36), AFF-23 (#37), SAN-01 (#42), SAN-02 (#43), DOC-05 (#139), ARC-06 (#189), ARC-35 (#203), NF-31 (#171)
- **Tickets liés** : #389, #390

**Corps**

**Besoin** : En tant que CPMT, planificateur ou gestionnaire, je veux voir sur une seule page la situation d'un travailleur (emplois, postes et risques, obligations, rendez-vous, décisions, documents, envois), afin de comprendre ce qui est dû et pourquoi.

**Écrans et routes** :
- Route `/travailleurs/:id`, dans le dossier `travailleurs/`.
- Sections :
  - identité et coordonnées ;
  - occupations (affilié, période, type de contrat) ;
  - affectations (poste, site, période) avec le profil de risques du poste ;
  - états particuliers ;
  - obligations (type, échéance, statut, badge « en retard », lien « pourquoi » vers la trace) ;
  - rendez-vous à venir et passés ;
  - décisions (catégorie, mesures, validité, date, sans motif médical) ;
  - documents ;
  - envois (communications).
- Actions :
  - « Planifier », vers DEMO-B2, pré-filtré sur l'affilié et la personne ;
  - « Ouvrir la consultation », vers `/consultation/:id`, pour les rôles médicaux.

**Écran composite du BFF et appels d'API** : `GET /api/v1/ecrans/travailleurs/{personneId}` (`EcranDossierTravailleur.cs`). Il appelle :
- `personnes/{id}`, `/occupations`, `/affectations`, `/etats-particuliers` ;
- `postes-risques` `postes/{posteId}/profil-risques`, pour chaque affectation active ;
- `obligations` `personnes/{id}/obligations` ;
- `planification` `rendez-vous?personneId=` ;
- `surveillance-medicale` `decisions?personneId=`, seulement si `decision:lire` ;
- `documents` `documents?typeDestinataire=personne&destinataireId=`, instance standard, en minuscules (voir #436) ;
- `communications` `messages?typeDestinataire=personne&destinataireId=`.

La trace d'une obligation est chargée à la demande, par le relais : `obligations/{id}/trace`. Chaque section est une `SectionTolerante`. Les affiliés sont résolus par `Annuaire`.

**Manques backend à combler** :
- L'implémentation d'`EcranDossierTravailleur` (501 aujourd'hui).
- La casse de `typeDestinataire` à aligner avec le service Documents (même cause que #436).
- Les exemplaires médicaux des documents relèvent de DEMO-C4.

**Droits par rôle** :

| Section | Rôles |
|---|---|
| Identité, occupations, affectations | `personne:lire` |
| Profil de risques | `poste:lire` |
| Obligations | `obligation:lire` : cpmt*, infirmier, assistant-medical, gestionnaire, planificateur |
| Rendez-vous | `planification:lire` |
| Décisions | `decision:lire` : cpmt*, infirmier, cp-*, gestionnaire |
| Documents | `documents:lire` |
| Envois | `communications:lire` |

- Le planificateur et l'assistant ne voient pas les décisions.
- Les conseillers cp-* ne voient pas les obligations.
- Le bouton « Planifier » requiert `planification:gerer`.

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Dossier travailleur

  Scénario: Vue complète par le CPMT
    Étant donné le CPMT Anne Martin connectée
    Quand elle ouvre le dossier de Nadia Lambert
    Alors le titre de la page est "Nadia Lambert"
    Et l'occupation active est chez Boulangerie Démo SRL
    Et l'affectation au poste "Boulanger de nuit" liste les risques travail de nuit (élevé), manutention de charges (moyen) et denrées alimentaires (faible)
    Et le rendez-vous de J au Centre SEPP Bruxelles avec Dr Anne Martin est listé, à l'heure de Bruxelles
    Et la décision "Apte" (évaluation préalable) est listée sans aucun motif médical
    Et les documents publiés pour Nadia Lambert sont listés

  Scénario: Comprendre pourquoi une obligation est due
    Étant donné Anne Martin sur le dossier de Nadia Lambert
    Quand elle demande la trace d'une obligation
    Alors le risque, la règle et la fréquence qui l'ont créée sont affichés

  Scénario: Planificateur sans accès aux décisions
    Étant donné le planificateur Nicolas Lemaire connecté
    Quand il ouvre le dossier de Nadia Lambert
    Alors la section Décisions est absente
    Et le bouton "Planifier" est visible

  Scénario: Conseiller sécurité sans accès aux obligations
    Étant donné le conseiller sécurité Karim Benali connecté
    Quand il ouvre le dossier de Nadia Lambert
    Alors la section Obligations est absente, sans message d'erreur

  Scénario: Service indisponible
    Étant donné le service Obligations arrêté
    Quand Anne Martin ouvre le dossier de Nadia Lambert
    Alors les autres sections sont affichées
    Et la section Obligations affiche une erreur role="alert" avec "Réessayer"

  Scénario: Travailleur inconnu
    Quand Anne Martin ouvre /travailleurs/{identifiant inconnu}
    Alors la page indique "Travailleur introuvable"

  Scénario: Temps de réponse
    Étant donné les données de démo
    Quand le dossier de Nadia Lambert est ouvert 20 fois
    Alors le 95e percentile du composite est inférieur à 2 secondes
```

**Accessibilité et i18n** :
- `h1` = nom du travailleur, et un `h2` par section.
- Tableaux avec `caption`.
- Un badge de statut porte toujours son texte (« en retard », « planifié »…).
- Une navigation interne par ancres, au clavier.
- Les libellés des risques et des statuts sont traduits (portée `travailleurs`, FR, NL et DE).
- Les dénominations et les noms de postes s'affichent tels qu'ils sont saisis.

**Hors périmètre** :
- modification de l'identité, des occupations ou des affectations ;
- contenu clinique ;
- historique détaillé des communications (seulement les 20 derniers envois).

**Dépendances** :
- Accès par DEMO-A2.
- Les liens vers DEMO-B2 et DEMO-C1 sont actifs à leur livraison.
- Indépendante de DEMO-A1 pour son développement.

---

### [DÉMO A] Fiche affilié

Réf. : DEMO-A4

- **Jalon** : Lot 1 – Socle
- **Labels** : `type:exigence-fonctionnelle`, `svc:plateforme`, `svc:affilies`, `svc:postes-risques`, `svc:obligations`
- **Priorité** : P1
- **Taille** : M
- **Zone** : standard
- **Parcours démo** : A (P2 et P5)
- **Exigences** : AFF-01 (#23), AFF-02 (#24), AFF-03 (#25), AFF-10 (#29), AFF-11 (#30), AFF-30 (#39), AFF-32 (#41), SAN-01 (#42)
- **Tickets liés** : #389, #1

**Corps**

**Besoin** : En tant que gestionnaire ou CPMT, je veux voir la fiche d'un affilié (structure, postes et risques, travailleurs, obligations et alertes), afin de piloter sa surveillance de santé.

**Écrans et routes** : route `/affilies/:id`, dans le dossier `affilies/`. Onglets :
- fiche (BCE, forme, NACE, commission paritaire, catégorie, langue, régime linguistique, dates) ;
- structure (groupe, unités d'établissement, sites, départements) et contacts ;
- postes, avec leur profil de risques et le statut de la dernière proposition ;
- travailleurs (paginé, lien vers `/travailleurs/:id`) ;
- obligations (synthèse : dues, en retard, planifiées) et alertes de non-couverture ;
- listes nominatives.

Action : « Planifier cet affilié », vers DEMO-B2, avec `planification:gerer`.

**Écran composite du BFF et appels d'API** : `GET /api/v1/ecrans/affilies/{affilieId}` (`EcranAffilie.cs`). Il appelle :
- `affilies/{id}`, avec sa structure et ses contacts ;
- `postes-risques` `postes?affilieId=` et `listes-nominatives?affilieId=` ;
- `obligations` `affilies/{id}/obligations/synthese` et `affilies/{id}/alertes` ;
- `personnes` `affilies/{id}/travailleurs` (première page ; les suivantes passent par le relais).

**Manques backend à combler** : l'implémentation d'`EcranAffilie` (501 aujourd'hui).

**Droits par rôle** :
- Lecture : `affilie:lire`.
- Travailleurs : `personne:lire`.
- Obligations et alertes : `obligation:lire`.
- Postes : `poste:lire`.
- Une section non autorisée est absente.

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Fiche affilié

  Scénario: Fiche de la Boulangerie
    Étant donné le gestionnaire Julie Peeters connectée
    Quand elle ouvre la fiche de Boulangerie Démo SRL
    Alors la page affiche le BCE 0999.010.126, la CP 118.03, la catégorie B et le régime Bruxelles-Capitale
    Et le groupe "Groupe Demo Benelux" et le site "Fournil central"
    Et 14 travailleurs et 6 postes, dont "Boulanger de nuit" avec 3 risques

  Scénario: Alertes de non-couverture
    Étant donné Julie Peeters sur la fiche de Construct Démo SA
    Quand elle ouvre l'onglet Obligations
    Alors la synthèse des obligations dues, en retard et planifiées est affichée
    Et les alertes de non-couverture renvoyées par le service sont listées

  Scénario: Historique des propositions
    Étant donné Julie Peeters sur la fiche de Construct Démo SA
    Quand elle ouvre le poste "Conducteur de travaux"
    Alors la proposition refusée et son motif "avis du comité PPT manquant" sont visibles

  Scénario: Droits restreints
    Étant donné l'administratrice fonctionnelle Claire Dupont connectée
    Quand elle ouvre la fiche de Zorg Demo vzw
    Alors l'onglet Travailleurs est absent

  Scénario: Données NL affichées telles quelles
    Étant donné le CPMT Anne Martin avec l'interface en français
    Quand elle ouvre la fiche de Zorg Demo vzw
    Alors le poste "Verpleegkundige" est affiché tel quel
    Et les libellés des risques sont en français
```

**Accessibilité et i18n** :
- Onglets accessibles (`role="tablist"`, flèches du clavier) ou ancres.
- Tableaux paginés avec `caption` et une pagination annoncée.
- Portée `affilies`.

**Hors périmètre** :
- création et modification de l'affilié, fusion et scission (AFF-05, AFF-06) ;
- organes de concertation ;
- unités de prévention.

**Dépendances** : aucune bloquante. Liens vers DEMO-A3 et DEMO-B2.

---

### [DÉMO B] Agenda jour et semaine, et détail d'un rendez-vous

Réf. : DEMO-B1

- **Jalon** : Lot 2 – Surveillance de santé
- **Labels** : `type:exigence-fonctionnelle`, `svc:plateforme`, `svc:planification`
- **Priorité** : P0
- **Taille** : L
- **Zone** : standard
- **Parcours démo** : B (P3), et le RDV du jour pour P4
- **Exigences** : PLA-01 (#108), PLA-02 (#109), PLA-03 (#110), SAN-02 (#43), SAN-10 (#46), SAN-13 (#49), DAT-08 (#244)
- **Tickets liés** : #389, #5

**Corps**

**Besoin** : En tant que CPMT, infirmier ou planificateur, je veux voir l'agenda du jour ou de la semaine par ressource et par lieu, et agir sur un rendez-vous, afin d'organiser les consultations.

**Écrans et routes** :
- `/planification`, l'agenda avec les paramètres `vue=jour|semaine`, `date`, `ressourceId` et `lieuId`.
- Vue par défaut :
  - pour un CPMT ou un infirmier, sa propre ressource (`ReferenceId` = `sub`) ;
  - pour un planificateur, toutes les ressources d'un lieu.
- `/planification/rendez-vous/:id`, le détail d'un rendez-vous :
  - personne, affilié, type, statut ;
  - obligations couvertes ;
  - convocations (canal, date, statut).
- Actions sur le rendez-vous : annuler (motif obligatoire), constater l'absence, reconvoquer (créneau choisi ou premier libre).
- Liens : dossier travailleur, consultation (rôles médicaux), salle d'attente.

**Écran composite du BFF et appels d'API** : `GET /api/v1/ecrans/agenda?du=&au=&ressourceId=&lieuId=` (`EcranAgenda.cs`). Il appelle :
- `planification` `creneaux?du&au&ressourceId&lieuId`, `rendez-vous?du&au&ressourceId&lieuId`, `ressources` et `lieux` ;
- `Annuaire`, pour les noms des personnes (DEMO-A5 si livré) et des affiliés.

Le composite renvoie, par ressource et par jour, des créneaux (libre, réservé, urgence) et des rendez-vous (personne, affilié, type, statut, convocation). Aucune donnée médicale dans les intitulés.

Le détail passe par le relais :
- `GET rendez-vous/{id}` ;
- `GET convocations?rendezVousId=` ;
- `GET obligations/obligations/{id}` ;
- actions `POST rendez-vous/{id}/annulation|absence|reconvocation`.

**Manques backend à combler** :
- L'implémentation d'`EcranAgenda` : la route actuelle n'a aucun paramètre, il faut ajouter `du`, `au`, `ressourceId` et `lieuId`, avec une période de 7 jours au plus.
- Le filtre `ressources` par `referenceId` (pour retrouver sa ressource) est à vérifier ; à défaut, le composite filtre.

**Droits par rôle** :
- Lecture : `planification:lire` (planificateur, responsable-centre, assistant-medical, cpmt, cpmt-dirigeant, infirmier).
- Annuler et reconvoquer : `planification:gerer` (planificateur, responsable-centre).
- Constater l'absence : bouton affiché selon le rôle autorisé par le service. L'endpoint n'a pas de `RequirePermission` explicite (hypothèse H9).

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Agenda

  Scénario: Journée du CPMT
    Étant donné le CPMT Anne Martin connectée à J
    Quand elle ouvre /planification
    Alors la vue jour affiche sa ressource "Dr Anne Martin - Bruxelles"
    Et 5 rendez-vous, dont Nadia Lambert, à l'heure de Bruxelles
    Et la plage 11:30-12:00 est marquée "urgences"

  Scénario: Semaine du planificateur filtrée par lieu
    Étant donné le planificateur Nicolas Lemaire connecté
    Quand il affiche la semaine en cours pour le lieu "Centre SEPP Gand"
    Alors seule la ressource "Dr Marc Dubois - Gand" est affichée, avec les rendez-vous de Zorg Demo vzw

  Scénario: Détail d'un rendez-vous
    Étant donné Anne Martin sur l'agenda de J
    Quand elle ouvre le rendez-vous de Nadia Lambert
    Alors les obligations couvertes et la convocation émise sont affichées
    Et le bouton "Annuler" est absent

  Scénario: Absence puis reconvocation
    Étant donné Nicolas Lemaire sur le rendez-vous de J de construct-1
    Quand il constate l'absence
    Alors le statut devient "absent" et les obligations redeviennent "à planifier"
    Quand il reconvoque sur le premier créneau libre
    Alors un nouveau rendez-vous convoqué apparaît dans l'agenda

  Scénario: Annulation sans motif refusée
    Quand Nicolas Lemaire annule un rendez-vous sans motif
    Alors le champ motif est signalé en erreur et le focus y est placé

  Scénario: Fuseau horaire
    Étant donné un créneau stocké à 06:30 UTC un jour d'hiver
    Alors il est affiché à 07:30
```

**Accessibilité et i18n** :
- L'agenda est un tableau (`caption` « Agenda du <date> », ressources en `th scope="col"`, heures en `th scope="row"`) ou une liste par ressource sur un écran étroit.
- Aucune action réservée au glisser-déposer.
- Jour précédent et suivant au clavier.
- Portée `planification`, avec les types d'actes traduits.

**Hors périmètre** :
- création des modèles d'agenda et des ressources ;
- replanification en masse (PLA-07) ;
- tournées mobiles (PLA-05) ;
- synchronisation M365 (PLA-09) ;
- glisser-déposer.

**Dépendances** : aucune bloquante. DEMO-A5 est souhaitable pour les performances. Prépare DEMO-B2 et DEMO-B3.

---

### [DÉMO B] Obligations à planifier et prise de rendez-vous avec convocation

Réf. : DEMO-B2

- **Jalon** : Lot 2 – Surveillance de santé
- **Labels** : `type:exigence-fonctionnelle`, `svc:plateforme`, `svc:planification`, `svc:obligations`
- **Priorité** : P0
- **Taille** : L
- **Zone** : standard
- **Parcours démo** : B (P3)
- **Exigences** : SAN-01 (#42), SAN-02 (#43), SAN-03 (#44), SAN-10 (#46), SAN-11 (#47), PLA-06 (#113), TSK-SAGA-REPRISE (#264)
- **Tickets liés** : #389, #4, #5

**Corps**

**Besoin** : En tant que planificateur, je veux voir les obligations à planifier d'un affilié, regroupées par travailleur, et réserver un créneau qui les couvre avec convocation, afin de respecter les échéances légales.

**Écrans et routes** : `/planification/a-planifier?affilieId=&personneId=`.
1. Choix de l'affilié.
2. Liste des obligations regroupables, par travailleur : type, échéance, date limite, badge « en retard » ou « urgent ».
3. Choix d'un groupe, puis créneaux libres proposés : lieu, ressource compétente, 14 jours.
4. Réservation avec convocation.
5. Confirmation, avec un lien vers le rendez-vous dans l'agenda.

Deux actions complémentaires :
- « Convoquer par lot » les rendez-vous non convoqués ;
- « Créneau d'urgence » pour un examen de reprise.

**Écran composite du BFF et appels d'API** : pas de composite, tout passe par le relais :
- `GET affilies?page=` ;
- `GET obligations/affilies/{id}/obligations/regroupables` ;
- `GET planification/creneaux?du&au&lieuId&statut=libre` ;
- `POST planification/rendez-vous` (créneau, personne, obligations, convocation) ;
- `POST planification/convocations/lots` ;
- `POST planification/urgences`.

Les noms se lisent avec `POST personnes/resumes` (DEMO-A5) ou, à défaut, `GET personnes/{id}` (14 personnes au plus par affilié).

**Manques backend à combler** :
- Aucun pour la démo.
- Une liste « à planifier » tous affiliés confondus n'existe pas (P2, hors périmètre).

**Droits par rôle** :
- Lecture : `obligation:lire`.
- Réservation, lot et urgence : `planification:gerer` (planificateur, responsable-centre).
- L'onglet « À planifier » est masqué aux autres rôles de `/planification`.

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Planifier les obligations

  Scénario: Liste des obligations d'un affilié
    Étant donné le planificateur Nicolas Lemaire connecté
    Quand il choisit l'affilié Construct Démo SA dans "À planifier"
    Alors les obligations à planifier sont listées par travailleur, avec leur échéance
    Et l'examen de reprise de construct-10 porte le badge "urgent"

  Scénario: Un rendez-vous couvre plusieurs obligations
    Étant donné un travailleur avec deux obligations dans la fenêtre de regroupement
    Quand Nicolas Lemaire réserve un créneau pour ce travailleur
    Alors un seul rendez-vous couvre les deux obligations
    Et elles passent au statut "convoqué"
    Et le rendez-vous apparaît dans l'agenda avec sa convocation

  Scénario: Créneau d'urgence pour une reprise
    Étant donné l'examen de reprise de construct-10 (reprise à J+7)
    Quand Nicolas Lemaire demande un créneau d'urgence
    Alors un créneau "examen de reprise" est réservé dans le délai SANTE.REPRISE.DELAI, jours fériés exclus

  Scénario: Créneau pris entre-temps
    Étant donné un créneau réservé par un autre utilisateur après l'affichage
    Quand Nicolas Lemaire le choisit
    Alors un message traduit indique que le créneau n'est plus libre
    Et la liste des créneaux est rechargée

  Scénario: Convocation par lot
    Étant donné des rendez-vous non convoqués pour Boulangerie Démo SRL
    Quand Nicolas Lemaire lance "Convoquer par lot"
    Alors le nombre de convocations émises est annoncé

  Scénario: Le CPMT ne planifie pas
    Étant donné le CPMT Anne Martin connectée
    Quand elle ouvre /planification
    Alors l'onglet "À planifier" est absent
```

**Accessibilité et i18n** :
- Le choix d'un créneau se fait par une liste de boutons radio groupés par jour (`fieldset` et `legend`), pas par une grille seulement visuelle.
- La confirmation est annoncée (`role="status"`) et reçoit le focus.
- Types d'obligations et statuts traduits.

**Hors périmètre** :
- planification automatique de sessions (PLA-04) ;
- choix du canal recommandé (SAN-11) au-delà du canal par défaut de l'affilié ;
- liste tous affiliés.

**Dépendances** : DEMO-B1 pour voir le résultat dans l'agenda. DEMO-A5 est souhaitable.

---

### [DÉMO B] Salle d'attente

Réf. : DEMO-B3

- **Jalon** : Lot 2 – Surveillance de santé
- **Labels** : `type:exigence-fonctionnelle`, `svc:plateforme`, `svc:planification`
- **Priorité** : P1
- **Taille** : M
- **Zone** : standard
- **Parcours démo** : B (P10)
- **Exigences** : PLA-08 (#115), NF-51 (#178)
- **Tickets liés** : #389

**Corps**

**Besoin** : En tant qu'assistante médicale, je veux enregistrer l'arrivée des travailleurs convoqués, et en tant que CPMT, les appeler en salle, afin de fluidifier la journée de consultation.

**Écrans et routes** : `/planification/salle-attente/:lieuId?date=`. Trois colonnes :
- attendus ;
- arrivés (heure d'arrivée, temps d'attente) ;
- appelés ou en consultation.

Actions :
- « Arrivé » (accueil) ;
- « Appeler », avec la salle (CPMT ou infirmier) ;
- « Terminer » ;
- lien « Ouvrir la consultation » (DEMO-C1).

**Écran composite du BFF et appels d'API** :
- Nouveau composite `GET /api/v1/ecrans/salle-attente/{lieuId}?date=` (nouveau fichier `EcranSalleAttente.cs`). Il appelle `planification` `salle-attente/{lieuId}?date=` et `ressources`, et résout les noms avec `Annuaire`.
- Les actions passent par le relais : `POST salle-attente/rendez-vous/{id}/arrivee|appel|fin`.
- Rafraîchissement automatique toutes les 30 secondes (hypothèse H10).

**Manques backend à combler** : le composite n'est pas déclaré dans `EcransInterneEndpoints`. Il faut l'ajouter au BFF, avec ses tests et l'instantané OpenAPI.

**Droits par rôle** : `planification:salle-attente` (assistant-medical, cpmt, cpmt-dirigeant, infirmier, planificateur, responsable-centre).

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Salle d'attente

  Scénario: Accueil d'un travailleur
    Étant donné l'assistante Laura Maes connectée à J
    Quand elle ouvre la salle d'attente du Centre SEPP Bruxelles
    Alors les 5 rendez-vous de Dr Anne Martin sont "attendus"
    Quand elle enregistre l'arrivée de Nadia Lambert
    Alors Nadia Lambert passe dans "arrivés", avec l'heure d'arrivée

  Scénario: Appel en salle
    Étant donné Nadia Lambert arrivée
    Et le CPMT Anne Martin sur la salle d'attente
    Quand elle appelle Nadia Lambert
    Alors Nadia Lambert passe dans "appelés"
    Et un lien "Ouvrir la consultation" mène à /consultation/{id de Nadia Lambert}

  Scénario: Mise à jour sans recharger la page
    Étant donné Anne Martin sur la salle d'attente
    Quand Laura Maes enregistre une arrivée
    Alors l'écran d'Anne Martin l'affiche en 30 secondes au plus, annoncé de façon polie (aria-live="polite")

  Scénario: Profil non autorisé
    Étant donné le gestionnaire Julie Peeters connectée
    Alors la salle d'attente n'apparaît pas dans la navigation
```

**Accessibilité et i18n** :
- Les colonnes sont des listes avec un titre `h2`.
- Les changements sont annoncés en `aria-live="polite"`, sans voler le focus.
- Portée `planification`.

**Hors périmètre** :
- borne d'accueil ;
- écran d'appel public ;
- notifications par SMS.

**Dépendances** : DEMO-B1 (routes de `planification/`). Le lien vers DEMO-C1 est actif à sa livraison.

---

### [DÉMO C] Surveillance médicale : mes examens en cours et mes décisions en brouillon

Réf. : DEMO-C0

- **Jalon** : Lot 2 – Surveillance de santé
- **Labels** : `type:exigence-fonctionnelle`, `svc:surveillance-medicale`, `zone:medicale`
- **Priorité** : P0
- **Taille** : M
- **Zone** : médicale
- **Parcours démo** : C et D (P1, P4)
- **Exigences** : SAN-20 (#50), SAN-31 (#56), NF-04 (#159), ARC-06 (#189)
- **Tickets liés** : #389, #6

**Corps**

**Besoin** : En tant que CPMT ou infirmier, je veux retrouver mes examens non clôturés et mes décisions non signées, afin de reprendre mon travail sans les rechercher travailleur par travailleur.

**Écrans et routes** : aucun écran. L'API est utilisée par DEMO-D1, DEMO-D2 et DEMO-C1.

**Écran composite du BFF et appels d'API** : deux lectures, dont le nom final revient à l'architecte :
- `GET /api/v1/examens?statut=en-cours&realisePar=moi` : `id`, `dossierId`, `personneId`, `affilieId`, `type`, `ouvertLe` ;
- `GET /api/v1/decisions?statut=brouillon&redacteur=moi` : `id`, `examenId`, `personneId`, `categorie`, `modifieeLe`.

Aucun contenu clinique (ni anamnèse, ni justification). Chaque liste est limitée à l'utilisateur courant (`sub`). La lecture est journalisée (NF-04).

**Manques backend à combler** :
- les deux requêtes, leurs index et leurs endpoints ;
- l'instantané OpenAPI.

`GET /decisions?personneId=` ne renvoie que les décisions émises : il ne convient pas.

**Droits par rôle** :
- `dossier-sante:lire` : cpmt, cpmt-dirigeant, infirmier.
- L'authentification forte s'applique (NF-01, politique automatique).
- Les autres rôles reçoivent 403.

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Mes examens en cours et mes décisions en brouillon

  Scénario: Listes du CPMT
    Étant donné le CPMT Anne Martin connectée
    Quand elle demande ses examens en cours
    Alors l'examen périodique de boulangerie-11, ouvert à J, est listé
    Quand elle demande ses décisions en brouillon
    Alors la décision "apte avec mesures" de boulangerie-10 est listée

  Scénario: Listes limitées à l'utilisateur
    Étant donné le CPMT dirigeant Marc Dubois connecté
    Quand il demande ses décisions en brouillon
    Alors la décision de boulangerie-10 n'est pas listée

  Scénario: Aucun contenu clinique
    Quand Anne Martin demande ses examens en cours
    Alors la réponse ne contient ni anamnèse, ni examen clinique, ni résultat d'acte

  Scénario: Profil non médical
    Étant donné le planificateur Nicolas Lemaire connecté
    Quand il demande des examens en cours
    Alors la réponse est 403

  Scénario: Journalisation
    Quand Anne Martin demande ses décisions en brouillon
    Alors une entrée d'audit de lecture est enregistrée en zone médicale
```

**Accessibilité et i18n** : sans objet (API).

**Hors périmètre** :
- liste de tous les brouillons du centre pour le CPMT dirigeant ;
- examens d'un autre CPMT.

**Dépendances** : aucune. Bloque DEMO-D1 et DEMO-D2.

---

### [DÉMO C] Vue consultation et examen (observation, actes, clôture)

Réf. : DEMO-C1

- **Jalon** : Lot 2 – Surveillance de santé
- **Labels** : `type:exigence-fonctionnelle`, `svc:plateforme`, `svc:surveillance-medicale`, `zone:medicale`
- **Priorité** : P0
- **Taille** : L
- **Zone** : médicale
- **Parcours démo** : C (P4)
- **Exigences** : SAN-20 (#50), SAN-21 (#51), SAN-22 (#52), SAN-23 (#53), SAN-40 (#60), NF-04 (#159), NF-01 (#156), NF-51 (#178), ARC-35 (#203)
- **Tickets liés** : #389, #6

**Corps**

**Besoin** : En tant que CPMT, je veux ouvrir la vue de consultation d'un travailleur convoqué, puis saisir l'examen et le clôturer, afin de réaliser l'évaluation de santé et de satisfaire ses obligations.

**Écrans et routes** :
- `/consultation/:personneId`, la vue de consultation :
  - identité ;
  - postes et risques ;
  - historique (examens, décisions) ;
  - examens dus ;
  - questionnaires déjà remplis ;
  - résultats d'actes ;
  - alertes et rappels.
- `/consultation/:personneId/examens/:examenId`, la saisie :
  - observation (anamnèse, examen clinique) ;
  - actes (biométrie, vision, audiométrie, spirométrie, ECG, biologie), avec les valeurs de référence ;
  - import d'un appareil `SIMULATEUR` (NF-14) ;
  - clôture.
- Accès depuis l'agenda, la salle d'attente, le dossier travailleur et « Examens en cours » (D1).
- Actions :
  - « Ouvrir le dossier » s'il n'existe pas ;
  - « Démarrer l'examen » sur le rendez-vous du jour ;
  - « Reprendre l'examen en cours » ;
  - « Rédiger la décision » après la clôture (DEMO-C2).

**Écran composite du BFF et appels d'API** : `GET /api/v1/ecrans/consultation/{personneId}?date=` (`EcranConsultation.cs`). Il appelle :
- `surveillance-medicale` `dossiers?personneId=`, puis `dossiers/{id}/consultation?date=` (vue SAN-20 existante) ;
- `personnes/{id}` ;
- `planification` `rendez-vous?personneId=&du=J&au=J+1`.

Le composite transmet les en-têtes `X-Motif-Acces` et `X-Bris-De-Glace` reçus du front.

La saisie passe par le relais :
- `POST dossiers` ;
- `POST examens` (avec `rendezVousId`) et `GET examens/{id}` ;
- `PUT examens/{id}/observation` ;
- `POST examens/{id}/actes` et `/imports` ;
- `POST examens/{id}/propositions-frequence/{pid}` ;
- `POST examens/{id}/cloture`.

**Manques backend à combler** :
- L'implémentation d'`EcranConsultation` (501 aujourd'hui).
- La propagation des en-têtes de motif par le composite, à vérifier et tester (le relais les transmet).
- La reprise d'un examen en cours depuis le tableau de bord dépend de DEMO-C0.

**Droits par rôle** :
- Lecture et saisie : `dossier-sante:lire|ecrire` (cpmt, cpmt-dirigeant, infirmier).
- Décision sur la proposition de fréquence : `decision:ecrire` (cpmt, cpmt-dirigeant). L'infirmier ne la voit pas.
- Les autres rôles n'ont pas la route (`roleGuard`) et reçoivent 403 du service.
- Hors relation de soin, un motif est obligatoire (dialogue livré par DEMO-T1).

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Consultation et examen

  Scénario: Ouverture depuis l'agenda
    Étant donné le CPMT Anne Martin connectée à J
    Quand elle ouvre la consultation de Nadia Lambert depuis son rendez-vous
    Alors la vue affiche le poste "Boulanger de nuit" et ses risques, la décision "Apte" de l'évaluation préalable et les examens dus
    Et aucun motif d'accès n'est demandé, car Anne Martin gère le dossier

  Scénario: Examen complet sur le rendez-vous du jour
    Étant donné Anne Martin sur la consultation de Nadia Lambert
    Quand elle démarre l'examen sur le rendez-vous de J
    Et saisit l'anamnèse, l'examen clinique et une biométrie
    Et clôture l'examen
    Alors l'examen est clôturé
    Et les obligations couvertes par le rendez-vous passent au statut "réalisé" dans le dossier travailleur
    Et le bouton "Rédiger la décision" est proposé

  Scénario: Reprise de l'examen en cours
    Étant donné Anne Martin sur son tableau de bord
    Quand elle reprend l'examen en cours de boulangerie-11
    Alors la biométrie déjà saisie (IMC 24,5, TA 122/78) est affichée

  Scénario: Résultat inhabituel et proposition de fréquence
    Étant donné Anne Martin sur l'examen de Nadia Lambert
    Quand elle saisit une tension artérielle de 165/100
    Alors une alerte "résultat inhabituel" s'affiche dans une zone role="alert"
    Et une augmentation de fréquence est proposée
    Quand elle l'accepte
    Alors l'acceptation est enregistrée

  Scénario: Infirmière sans pouvoir de décision
    Étant donné l'infirmière Sophie Lambert sur un examen qu'elle a ouvert
    Alors elle peut enregistrer des actes
    Mais les boutons de décision sur la fréquence sont absents

  Scénario: Profil non médical
    Étant donné l'assistante Laura Maes connectée
    Quand elle ouvre /consultation/{id de Nadia Lambert}
    Alors elle est redirigée vers l'accueil, sans appel au service

  Scénario: Saisie interrompue
    Étant donné une erreur réseau à l'enregistrement de l'observation
    Alors la saisie est conservée dans le formulaire
    Et le message d'erreur propose de réessayer
```

**Accessibilité et i18n** :
- Chaque mesure a un libellé, son unité (`mmHg`, `kg/m²`) et sa norme liée par `aria-describedby`.
- L'alerte est annoncée sans perte de focus.
- L'ordre de tabulation suit la saisie clinique, avec le raccourci d'enregistrement de `keyboard-shortcuts.ts` (NF-51).
- Portée `consultation`, avec les termes médicaux NL et DE marqués « à valider ».

**Hors périmètre** :
- dictée vocale (SAN-24) ;
- import HL7 réel ;
- questionnaires sur tablette ;
- pièces jointes (SAN-41) ;
- déclaration de maladie professionnelle.

**Dépendances** :
- Aucune bloquante pour démarrer.
- DEMO-C0 pour « Reprendre ».
- DEMO-T1 pour le motif et la montée en niveau.
- Bloque DEMO-C2.

---

### [DÉMO C] Décision d'évaluation de santé : rédaction, formulaire par exemplaire, signature et documents

Réf. : DEMO-C2

- **Jalon** : Lot 2 – Surveillance de santé
- **Labels** : `type:exigence-fonctionnelle`, `svc:plateforme`, `svc:surveillance-medicale`, `svc:documents`, `zone:medicale`
- **Priorité** : P0
- **Taille** : L
- **Zone** : médicale (l'exemplaire employeur est en zone standard)
- **Parcours démo** : C (P4)
- **Exigences** : SAN-30 (#55), SAN-31 (#56), SAN-32 (#57), SAN-33 (#58), NF-41 (#176), NF-01 (#156), DOC-02 (#136), NF-21 (#168), POR-13 (#133)
- **Tickets liés** : #389, #356 (ADR 0009), #436

**Corps**

**Besoin** : En tant que CPMT, je veux rédiger la décision après l'examen, prévisualiser le formulaire de l'annexe I.4-2 pour chaque exemplaire, puis signer, afin que l'employeur et le travailleur reçoivent leur document dans la bonne langue.

**Écrans et routes** :
- `/consultation/:personneId/decisions/nouvelle?examenId=`, puis `/consultation/:personneId/decisions/:decisionId`.
- Formulaire :
  - catégorie (apte, apte avec mesures, inaptitude temporaire, inaptitude définitive, mutation, écartement maternité) ;
  - mesures ;
  - durée de validité ;
  - justification et recommandations (zone médicale).
- Aperçu par onglets : Employeur, Travailleur, Dossier.
- Bouton « Signer », avec une confirmation `<dialog>`.
- Après la signature :
  - statut « émise », en lecture seule ;
  - liste des documents générés (exemplaire, langue, date, lien PDF) ;
  - un indicateur « génération en cours » tant que les documents ne sont pas tous là.

**Écran composite du BFF et appels d'API** : pas de composite. Le relais appelle :
- `POST surveillance-medicale/examens/{id}/decision` ;
- `PUT decisions/{id}` ;
- `GET decisions/{id}/complete` ;
- `GET decisions/{id}/formulaire?exemplaire=Employeur|Travailleur|Dossier` ;
- `POST decisions/{id}/signature`, avec l'authentification forte exigée ;
- `GET documents/documents?objetType=decision&objetId=`, pour l'exemplaire employeur (instance standard), interrogé toutes les 3 secondes pendant 30 secondes au plus.

Les exemplaires travailleur et dossier sont en zone médicale : DEMO-C4.

**Manques backend à combler** :
- DEMO-C4, pour afficher les exemplaires travailleur et dossier dans l'application interne. Sans lui, seul l'exemplaire employeur est listé, avec un message explicite.
- #436, pour que le travailleur voie son exemplaire au portail.
- La valeur `objetType` des documents de décision est à confirmer dans le service Documents.

**Droits par rôle** :
- Rédiger, modifier et signer : `decision:ecrire` (cpmt, cpmt-dirigeant).
- Décision complète : `dossier-sante:lire` (cpmt*, infirmier).
- L'infirmier n'a pas les boutons « Rédiger » ni « Signer ».

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Décision d'évaluation de santé

  Scénario: Signer la décision en brouillon
    Étant donné le CPMT Anne Martin connectée
    Et la décision en brouillon de boulangerie-10 : apte avec mesures, aménagement de poste
    Quand elle ouvre l'aperçu de l'exemplaire Employeur
    Alors la catégorie et la mesure sont affichées, sans justification médicale
    Quand elle ouvre l'aperçu de l'exemplaire Dossier
    Alors la justification et les procédures de recours sont affichées
    Quand elle signe et confirme
    Alors la décision est "émise" et n'est plus modifiable
    Et en 30 secondes au plus l'exemplaire Employeur est listé, en français (Boulangerie Démo SRL)

  Scénario: Décision complète en direct pour Nadia Lambert
    Étant donné Anne Martin qui a clôturé l'examen de Nadia Lambert (DEMO-C1)
    Quand elle rédige une décision "Apte" et la signe
    Alors l'exemplaire Employeur est généré en français
    Et Nadia Lambert, connectée au portail travailleur, voit son formulaire d'évaluation de santé dans "Mes documents"

  Scénario: Langue du document selon l'affilié
    Étant donné la décision signée de zorg-6 (Zorg Demo vzw, régime néerlandais)
    Quand Anne Martin consulte ses documents
    Alors l'exemplaire Employeur est en néerlandais

  Scénario: Infirmière en lecture
    Étant donné l'infirmière Sophie Lambert connectée
    Quand elle ouvre la décision émise de boulangerie-9
    Alors la décision est affichée sans bouton "Rédiger" ni "Signer"

  Scénario: Authentification forte exigée
    Étant donné les services en mode Enforce
    Et Anne Martin connectée avec un mot de passe seul
    Quand elle signe
    Alors la montée en niveau est demandée (DEMO-T1)
    Et après l'OTP la signature aboutit, sans ressaisie

  Scénario: Génération lente
    Étant donné un document non généré après 30 secondes
    Alors un message indique que la génération continue, avec "Actualiser"
```

**Accessibilité et i18n** :
- Les aperçus par exemplaire sont des onglets accessibles.
- La confirmation de signature est un `<dialog>` qui piège le focus et le rend au bouton.
- Le statut « émise » est annoncé.
- Portée `consultation`.
- La langue du document suit le régime de l'affilié (NF-41), indépendamment de la langue de l'interface.

**Hors périmètre** :
- concertation et recours (SAN-34) ;
- signature qualifiée eID ou itsme réelle (simulateur local) ;
- modèles de texte (SAN-24) ;
- accusé de lecture employeur (POR-05, DEMO-H1).

**Dépendances** :
- DEMO-C1 ;
- DEMO-F3 (modèles publiés, déjà amorcés par #393) ;
- #436 pour le portail travailleur ;
- DEMO-C4 (P1) pour les exemplaires médicaux ;
- DEMO-T1 en mode Enforce.

---

### [DÉMO C] BFF interne : lire les documents de la zone médicale

Réf. : DEMO-C4

- **Jalon** : Lot 2 – Surveillance de santé
- **Labels** : `type:architecture`, `svc:plateforme`, `svc:documents`, `zone:medicale`
- **Priorité** : P1
- **Taille** : M
- **Zone** : médicale
- **Parcours démo** : C et A (P4, P2)
- **Exigences** : ARC-06 (#189), ARC-43 (#207), SAN-30 (#55), NF-04 (#159)
- **Tickets liés** : #356, ADR 0009, ADR 0013, #389

**Corps**

**Besoin** : En tant que CPMT, je veux consulter dans l'application interne les exemplaires travailleur et dossier d'une décision, afin de vérifier ce que reçoit le travailleur et ce qui est versé au dossier.

**Écrans et routes** :
- Liste des documents de DEMO-C2.
- Section Documents de DEMO-A3, uniquement pour les rôles médicaux.

**Écran composite du BFF et appels d'API** :
- ADR 0009 : les exemplaires travailleur et dossier sont produits par l'instance `documents-medicale`, que le CPMT lit avec `dossier-sante:lire` et un motif.
- Le BFF interne ne relaie aujourd'hui que l'instance standard (`ServicesRelayes`).
- Deux options, à trancher par ADR :
  - a. ajouter `documents-medicale` à la liste fermée du relais (code, compose, Terraform, flux réseau du BFF interne vers la zone médicale) ;
  - b. un composite qui fusionne les deux instances, sur le modèle d'`EcransDocuments` du BFF travailleur.

**Manques backend à combler** : l'ADR, puis la réalisation de l'option retenue, avec un test d'intégration et la mise à jour de `ConfigurationTerraformTests` si une clé est ajoutée.

**Droits par rôle** :
- Instance médicale : `dossier-sante:lire` et la relation de soin, ou un motif.
- Les autres rôles ne voient que l'instance standard.

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Documents de la zone médicale

  Scénario: Trois exemplaires pour le CPMT
    Étant donné le CPMT Anne Martin connectée
    Quand elle ouvre les documents de la décision signée de Nadia Lambert
    Alors les exemplaires Employeur, Travailleur et Dossier sont listés et ouvrables

  Scénario: Gestionnaire limitée à la zone standard
    Étant donné le gestionnaire Julie Peeters connectée
    Quand elle ouvre les documents de Nadia Lambert dans son dossier travailleur
    Alors seul l'exemplaire Employeur est listé

  Scénario: Lecture journalisée
    Quand Anne Martin ouvre l'exemplaire Dossier
    Alors une entrée d'audit de lecture est enregistrée en zone médicale

  Scénario: Instance médicale indisponible
    Étant donné l'instance documents-medicale arrêtée
    Alors l'exemplaire Employeur reste listé
    Et un message signale que les exemplaires médicaux sont indisponibles
```

**Accessibilité et i18n** : le message d'indisponibilité est en `role="alert"`, dans la portée de l'écran appelant.

**Hors périmètre** : documents de la zone psychosociale.

**Dépendances** : l'ADR (architecte solution et architecte sécurité) d'abord. DEMO-C2 l'utilise.

---

### [DÉMO C] Vaccinations : carnet, rappels dus, administration et stock

Réf. : DEMO-C3

- **Jalon** : Lot 2 – Surveillance de santé
- **Labels** : `type:exigence-fonctionnelle`, `svc:plateforme`, `svc:surveillance-medicale`, `zone:medicale`
- **Priorité** : P1
- **Taille** : M
- **Zone** : médicale
- **Parcours démo** : C (P7)
- **Exigences** : SAN-50 (#65), SAN-51 (#66), POR-13 (#133)
- **Tickets liés** : #389, #6

**Corps**

**Besoin** : En tant qu'infirmière, je veux voir les vaccinations et les rappels dus d'un travailleur, administrer une dose en choisissant le lot, et suivre le stock du centre, afin de tenir les schémas vaccinaux à jour.

**Écrans et routes** :
- `/vaccinations/personnes/:personneId` : carnet (vaccin, dose, date, lot), rappels dus avec badge « en retard », bouton « Administrer » (vaccin, dose, date, lot non périmé, remarque).
- `/vaccinations/stock` : stock du centre par vaccin et par lot, lots périmés ou qui périment sous 30 jours, réception d'un lot, ajustement.
- Accès depuis la consultation (onglet Vaccinations).

**Écran composite du BFF et appels d'API** : pas de composite. Le relais appelle :
- `GET surveillance-medicale/dossiers?personneId=` et `GET dossiers/{id}` (partie vaccinations) ;
- `GET dossiers/{id}/vaccinations/rappels` ;
- `POST dossiers/{id}/vaccinations` ;
- `GET protocoles/schemas-vaccinaux` ;
- `GET centres/{centreId}/stock-vaccins` ;
- `POST lots-vaccins` et `POST lots-vaccins/{id}/ajustement`.

**Manques backend à combler** :
- Aucun pour la démo.
- Une liste des rappels dus tous travailleurs confondus n'existe pas (P2, hors périmètre).
- La correspondance `centreId` = lieu de planification est une hypothèse (H8).

**Droits par rôle** :
- Carnet : `dossier-sante:lire`.
- Administration : `dossier-sante:ecrire`.
- Stock : `stock-vaccins:gerer`.
- Rôles concernés : cpmt, cpmt-dirigeant, infirmier.

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Vaccinations

  Scénario: Rappel dû
    Étant donné l'infirmière Sophie Lambert connectée
    Quand elle ouvre les vaccinations de zorg-7
    Alors les doses 1 (J-240) et 2 (J-210) d'hépatite B sont listées avec le lot DEMO-HB-001
    Et le rappel de la dose 3 porte le badge "en retard"

  Scénario: Administration d'une dose
    Étant donné Sophie Lambert sur les vaccinations de zorg-7
    Quand elle administre la dose 3 d'hépatite B avec le lot DEMO-HB-001
    Alors la dose est ajoutée au carnet et le rappel disparaît
    Et le stock du lot DEMO-HB-001 au centre de Bruxelles diminue de 1

  Scénario: Lot périmé non proposé
    Étant donné un lot périmé au centre de Bruxelles
    Quand Sophie Lambert administre une dose
    Alors ce lot n'est pas proposé

  Scénario: Profil non médical
    Étant donné l'assistante Laura Maes connectée
    Alors le menu Vaccinations est absent
```

**Accessibilité et i18n** :
- Tableaux avec `caption`.
- La date de péremption est en texte, pas en couleur seulement.
- Portée `vaccinations`, avec les noms de vaccins NL et DE « à valider ».

**Hors périmètre** :
- enregistrement Vaccinnet ou e-Vax (SAN-52) ;
- tests tuberculiniques ;
- liste des rappels de tout un centre.

**Dépendances** : aucune bloquante. Le lien depuis la consultation se fait avec DEMO-C1.

---

### [DÉMO D] Tableau de bord par rôle

Réf. : DEMO-D1

- **Jalon** : Lot 2 – Surveillance de santé
- **Labels** : `type:exigence-fonctionnelle`, `svc:plateforme`, `svc:planification`, `svc:surveillance-medicale`, `svc:postes-risques`, `svc:reintegration`
- **Priorité** : P0
- **Taille** : M
- **Zone** : standard, avec des cartes de zone médicale réservées aux rôles médicaux
- **Parcours démo** : D (P1)
- **Exigences** : REP-20 (#149), NF-51 (#178), NF-31 (#171), ARC-35 (#203), AFF-14 (#33), SAN-43 (#63), SAN-65 (#73)
- **Tickets liés** : #389, #390

**Corps**

**Besoin** : En tant que CPMT, je veux voir dès la connexion mes rendez-vous du jour et ce qui attend mon action, afin de commencer ma journée sans chercher.

**Écrans et routes** : `/` (`dashboard/dashboard.ts`, cartes `DashboardCard`). Les cartes dépendent du rôle ; chacune affiche un nombre, 5 éléments au plus et « Tout voir ».

| Carte | Rôles | Lien |
|---|---|---|
| Mes rendez-vous du jour | cpmt*, infirmier | `/planification` |
| Rendez-vous du jour du centre | planificateur, assistant, responsable-centre | `/planification` |
| Examens en cours | cpmt*, infirmier | `/consultation/...` |
| Décisions en brouillon | cpmt* | DEMO-C2 |
| Propositions poste-risque à valider | cpmt* | `/travail` |
| Demandes de copie du dossier | cpmt* | `/travail` |
| Délais de réintégration (proches ou dépassés) | cpmt*, infirmier | `/reintegration` |
| Accès rapides (paramètres, modèles, intégrations, audit) | admin-fonctionnel, gestionnaire, dpo | `/admin` |

**Écran composite du BFF et appels d'API** : `GET /api/v1/ecrans/tableau-de-bord` (`EcranTableauDeBord.cs`). Il n'appelle que les services utiles au rôle du jeton :
- `planification` `ressources` (filtrées sur le `sub`) et `rendez-vous?ressourceId&du=J&au=J+1` ;
- `surveillance-medicale`, listes de DEMO-C0 et `demandes-copie-dossier` ;
- `postes-risques` `propositions-poste-risque?statut=soumise` ;
- `reintegration` `tableau-de-bord/delais`.

Les noms passent par `Annuaire` (DEMO-A5 si livré). Chaque carte est une `SectionTolerante`.

**Manques backend à combler** :
- l'implémentation d'`EcranTableauDeBord` (501 aujourd'hui) ;
- DEMO-C0.

**Droits par rôle** : voir le tableau ci-dessus. Le composite n'appelle aucun service de zone médicale pour un rôle non médical (testé).

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Tableau de bord

  Scénario: Journée du CPMT
    Étant donné le CPMT Anne Martin connectée à J
    Quand elle arrive sur l'accueil
    Alors "Mes rendez-vous du jour" compte 5 rendez-vous, dont Nadia Lambert
    Et "Décisions en brouillon" liste boulangerie-10
    Et "Examens en cours" liste boulangerie-11
    Et "Propositions poste-risque à valider" compte au moins la proposition de l'employeur Eva Claes
    Et "Demandes de copie" liste la demande de Nadia Lambert
    Et "Délais de réintégration" signale le délai dépassé de boulangerie-12

  Scénario: Deux clics vers la signature
    Étant donné Anne Martin sur l'accueil
    Quand elle choisit la décision en brouillon de boulangerie-10
    Alors l'écran de décision s'ouvre (DEMO-C2)

  Scénario: Planificateur sans donnée médicale
    Étant donné le planificateur Nicolas Lemaire connecté
    Alors seule la carte "Rendez-vous du jour du centre" est affichée
    Et le BFF n'a appelé ni Surveillance médicale ni Réintégration

  Scénario: Carte en erreur
    Étant donné le service Réintégration arrêté
    Quand Anne Martin arrive sur l'accueil
    Alors la carte "Délais de réintégration" affiche une erreur avec "Réessayer"
    Et les autres cartes sont affichées

  Scénario: Temps de réponse
    Quand l'accueil d'Anne Martin est chargé 20 fois
    Alors le 95e percentile du composite est inférieur à 2 secondes
```

**Accessibilité et i18n** :
- Chaque carte est une `section` avec un titre `h2`.
- Les nombres ont une forme textuelle (« 5 rendez-vous »).
- Un seul `h1` (« Accueil »).
- Portée `dashboard`.

**Hors périmètre** :
- indicateurs agrégés de direction (REP-10 à REP-13) ;
- personnalisation des cartes ;
- notifications en temps réel.

**Dépendances** :
- DEMO-C0.
- Les liens vers DEMO-B1, DEMO-C1, DEMO-C2, DEMO-D2 et DEMO-E1 sont actifs à leur livraison.

---

### [DÉMO D] File de travail du CPMT : propositions poste-risque, demandes de copie, brouillons

Réf. : DEMO-D2

- **Jalon** : Lot 2 – Surveillance de santé
- **Labels** : `type:exigence-fonctionnelle`, `svc:plateforme`, `svc:postes-risques`, `svc:surveillance-medicale`, `zone:medicale`
- **Priorité** : P0
- **Taille** : M
- **Zone** : standard pour les propositions, médicale pour les copies et les brouillons
- **Parcours démo** : D (P1, P5)
- **Exigences** : AFF-14 (#33), AFF-31 (#40), SAN-04 (#45), SAN-43 (#63), POR-13 (#133)
- **Tickets liés** : #389, #351 (écran de la file des demandes de copie)

**Corps**

**Besoin** : En tant que CPMT, je veux valider ou refuser les propositions poste-risque de l'employeur avec l'avis du Comité PPT, et traiter les demandes de copie du dossier, afin que les obligations soient recalculées et que le travailleur obtienne sa copie.

**Écrans et routes** : `/travail` (dossier `travail/`). Trois onglets :
- **Propositions poste-risque** :
  - liste (affilié, poste, auteur, date, statut) ;
  - détail : lignes ajoutées ou retirées avec leur niveau, motif, avis CPPT ;
  - actions : « Enregistrer l'avis CPPT » (date et document), « Valider », « Refuser » (motif obligatoire).
- **Demandes de copie** : liste (travailleur, date, échéance) ; « Valider » ou « Refuser » (motif).
- **Brouillons et examens** : les listes de DEMO-C0, avec des liens vers DEMO-C1 et DEMO-C2.

**Écran composite du BFF et appels d'API** : pas de composite. Le relais appelle :
- `GET postes-risques/propositions-poste-risque?statut=` et `GET /{id}` ;
- `POST /{id}/avis-cppt`, `/validation` et `/refus` ;
- `GET surveillance-medicale/demandes-copie-dossier/` ;
- `POST /{id}/validation` et `/refus` ;
- les listes de DEMO-C0.

Les noms et dénominations se lisent avec DEMO-A5, ou à défaut avec `GET` unitaire.

Après une validation, l'événement de modification du profil de risques déclenche le recalcul des obligations (SAN-04) par le bus de développement.

**Manques backend à combler** :
- DEMO-C0.
- L'écran de #351 est couvert ici. La notification Communications et l'alerte avant échéance de #351 restent dans #351 (Lot 3).

**Droits par rôle** :
- Valider ou refuser une proposition : `risque-poste:valider` (cpmt, cpmt-dirigeant).
- Demandes de copie : `dossier-sante:copie-traiter` (cpmt, cpmt-dirigeant).
- L'infirmier voit les propositions en lecture seule et l'onglet « Brouillons et examens ».

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: File de travail du CPMT

  Scénario: Proposition de l'employeur validée, obligations recalculées (P5)
    Étant donné l'employeur Eva Claes qui propose sur le portail employeur l'ajout d'un risque au poste "Livreur" de Boulangerie Démo SRL
    Et le CPMT Anne Martin connectée
    Quand elle ouvre la proposition dans /travail
    Et enregistre l'avis du Comité PPT avec sa date
    Et valide la proposition
    Alors la proposition est "validée"
    Et en 10 secondes au plus les travailleurs affectés au poste "Livreur" ont une obligation recalculée, visible dans leur dossier travailleur

  Scénario: Refus motivé visible de l'employeur
    Étant donné une proposition soumise par Eva Claes
    Quand Anne Martin la refuse avec le motif "Analyse de risques à compléter"
    Alors Eva Claes voit sur le portail employeur la proposition "refusée" et son motif

  Scénario: Refus sans motif impossible
    Quand Anne Martin refuse une proposition sans motif
    Alors le champ motif est signalé en erreur et reçoit le focus

  Scénario: Demande de copie traitée
    Étant donné la demande de copie déposée par Nadia Lambert
    Quand Anne Martin la valide
    Alors la demande disparaît de la file
    Et Nadia Lambert peut télécharger sa copie depuis le portail travailleur

  Scénario: Infirmière en lecture seule
    Étant donné l'infirmière Sophie Lambert connectée
    Quand elle ouvre /travail
    Alors les propositions sont visibles sans les boutons "Valider" ni "Refuser"
    Et l'onglet "Demandes de copie" est absent
```

**Accessibilité et i18n** :
- Onglets accessibles.
- Le refus est un `<dialog>` avec un champ motif libellé.
- Le résultat d'une action est annoncé (`role="status"`).
- Portée `travail`.

**Hors périmètre** :
- propositions de listes nominatives (AFF-31, onglet ultérieur) ;
- surcharges de fréquence (AFF-13) ;
- notification au travailleur (#351).

**Dépendances** : DEMO-C0. La démonstration de P5 s'appuie sur le portail employeur existant (page `propositions`).

---

### [DÉMO E] Réintégration : tableau des délais et dossier d'incapacité

Réf. : DEMO-E1

- **Jalon** : Lot 3 – Réintégration et portails complets
- **Labels** : `type:exigence-fonctionnelle`, `svc:plateforme`, `svc:reintegration`, `zone:medicale`
- **Priorité** : P1
- **Taille** : M
- **Zone** : médicale (vue de suivi sans donnée médicale pour le gestionnaire)
- **Parcours démo** : E (P8)
- **Exigences** : SAN-60 (#68), SAN-61 (#69), SAN-63 (#71), SAN-65 (#73)
- **Tickets liés** : #389, #7

**Corps**

**Besoin** : En tant que CPMT, je veux voir les délais légaux de réintégration proches ou dépassés et ouvrir un dossier d'incapacité, afin d'agir avant le dépassement.

**Écrans et routes** :
- `/reintegration` : tableau des délais (travailleur, affilié, échéance, type d'échéance, statut proche, dépassé ou respecté), avec des filtres sur l'affilié et le statut.
- `/reintegration/incapacites/:id` : dossier d'incapacité (début, fin prévue, prise en charge, information du travailleur, estimations du potentiel de travail, trajets liés, échanges eHealth).
- Actions : « Prendre en charge », « Informer le travailleur », « Estimer le potentiel ».
- Vue du gestionnaire : `/reintegration/suivi?affilieId=`, le suivi des trajets d'un affilié sans donnée médicale.

**Écran composite du BFF et appels d'API** : pas de composite. Le relais appelle :
- `GET reintegration/tableau-de-bord/delais?affilieId&statut` ;
- `GET incapacites?personneId=` et `GET incapacites/{id}` ;
- `POST incapacites/{id}/prise-en-charge`, `/information-travailleur` et `/estimations` ;
- `GET incapacites/{id}/echanges-ehealth` ;
- `GET affilies/{id}/trajets`, pour le suivi du gestionnaire.

Les noms se lisent avec DEMO-A5.

**Manques backend à combler** :
- Le tableau des délais requiert `reintegration:lire`, que le gestionnaire n'a pas. Sa vue passe par le suivi par affilié (question Q5).
- Le motif d'accès s'applique (filtre `VerifierMotif`, DEMO-T1).

**Droits par rôle** :
- Lecture : `reintegration:lire` (cpmt, cpmt-dirigeant, infirmier).
- Écriture : `reintegration:ecrire` (les mêmes).
- Suivi : `reintegration-suivi:lire` (gestionnaire).

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Délais de réintégration

  Scénario: Délai dépassé
    Étant donné le CPMT Anne Martin connectée
    Quand elle ouvre /reintegration
    Alors boulangerie-12 apparaît avec le statut "dépassé" (trajet demandé à J-120 sans invitation)
    Et construct-9 apparaît avec l'évaluation en attente et son échéance

  Scénario: Dossier d'incapacité
    Quand Anne Martin ouvre le dossier d'incapacité de construct-9
    Alors le début de l'incapacité (J-70), la demande de trajet (J-60) et l'invitation recommandée (J-55) sont affichés

  Scénario: Estimation du potentiel de travail
    Étant donné l'infirmière Sophie Lambert sur le dossier de construct-9
    Quand elle enregistre une estimation du potentiel de travail
    Alors l'estimation apparaît dans le dossier

  Scénario: Suivi sans donnée médicale
    Étant donné le gestionnaire Julie Peeters connectée
    Quand elle ouvre le suivi de Construct Démo SA
    Alors les trajets sont listés avec leur statut et leurs échéances
    Et aucune estimation ni commentaire médical n'est affiché
```

**Accessibilité et i18n** :
- Le statut est un texte (« dépassé ») et non une couleur seulement.
- Tableau trié par échéance, avec l'ordre de tri annoncé (`aria-sort`).
- Portée `reintegration`.

**Hors périmètre** :
- échanges eHealth réels (SAN-64) ;
- saisie d'une notification d'incapacité ;
- étapes du trajet (DEMO-E2).

**Dépendances** : aucune bloquante. DEMO-T1 pour le motif. DEMO-E2 suit.

---

### [DÉMO E] Trajet de réintégration : invitation, évaluation, formulaire, réponses

Réf. : DEMO-E2

- **Jalon** : Lot 3 – Réintégration et portails complets
- **Labels** : `type:exigence-fonctionnelle`, `svc:plateforme`, `svc:reintegration`, `zone:medicale`
- **Priorité** : P1
- **Taille** : M
- **Zone** : médicale
- **Parcours démo** : E (P8)
- **Exigences** : SAN-62 (#70), SAN-63 (#71), SAN-11 (#47)
- **Tickets liés** : #389, #7

**Corps**

**Besoin** : En tant que CPMT, je veux faire avancer un trajet de réintégration étape par étape, avec les délais légaux, afin de tenir la procédure de 2026.

**Écrans et routes** : `/reintegration/trajets/:id`, une frise des étapes :
1. demande ;
2. invitation (date, recommandée, document) ;
3. évaluation (date, note) ;
4. formulaire d'évaluation de réintégration (conclusion, document) ;
5. réponse de l'employeur (plan ou rapport motivé) ;
6. réponse du travailleur (délai `REINTEGRATION.REPONSE_PLAN.DELAI`) ;
7. fin.

Un compteur d'absences signale la notification au médecin-conseil après 2 absences.

**Écran composite du BFF et appels d'API** : le relais appelle :
- `GET reintegration/trajets/{id}` ;
- `POST trajets/{id}/invitation`, `/evaluation`, `/formulaire`, `/reponse-employeur`, `/reponse-travailleur`, `/absences`, `/fin` et `/annulation`.

**Manques backend à combler** : aucun connu.

**Droits par rôle** :
- Invitation, absences et fin : `reintegration:ecrire` (cpmt*, infirmier).
- Évaluation et formulaire : `reintegration:evaluer` (cpmt, cpmt-dirigeant).
- Les réponses de l'employeur et du travailleur sont saisies par les professionnels de la zone médicale pour le compte des parties (cas d'usage).

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Trajet de réintégration

  Scénario: Régulariser un délai dépassé
    Étant donné le CPMT Anne Martin sur le trajet de boulangerie-12
    Quand elle enregistre une invitation recommandée datée de J
    Alors l'étape "invitation" est faite
    Et l'échéance suivante est calculée selon REINTEGRATION.INVITATION.DELAI

  Scénario: Évaluation et formulaire
    Étant donné Anne Martin sur le trajet de construct-9
    Quand elle enregistre l'évaluation puis le formulaire avec une conclusion
    Alors l'employeur voit la conclusion dans le suivi de son affilié, sans donnée médicale

  Scénario: Deux absences
    Étant donné un trajet avec une absence enregistrée
    Quand une deuxième absence est enregistrée
    Alors la notification au médecin-conseil est signalée

  Scénario: Infirmière sans évaluation
    Étant donné l'infirmière Sophie Lambert sur un trajet
    Alors les actions "Évaluer" et "Formulaire" sont absentes
```

**Accessibilité et i18n** :
- La frise est une liste ordonnée (`ol`), avec l'étape courante marquée `aria-current="step"`.
- Portée `reintegration`.

**Hors périmètre** :
- envoi recommandé réel ;
- échanges eHealth ;
- plan de réintégration déposé par l'employeur sur le portail.

**Dépendances** : DEMO-E1.

---

### [DÉMO F] Administration : paramètres légaux versionnés, jours fériés et nomenclatures

Réf. : DEMO-F1

- **Jalon** : Lot 1 – Socle
- **Labels** : `type:exigence-fonctionnelle`, `svc:plateforme`
- **Priorité** : P0
- **Taille** : M
- **Zone** : standard
- **Parcours démo** : F (P6)
- **Exigences** : NF-61 (#181), TSK-REGLES-LEGALES (#262), DAT-07 (#243), DAT-08 (#244), NF-40 (#175)
- **Tickets liés** : #389, #14

**Corps**

**Besoin** : En tant qu'administratrice fonctionnelle, je veux modifier un paramètre légal avec une date d'effet, gérer les jours fériés et les nomenclatures, afin d'adapter le logiciel à la loi sans développement.

**Écrans et routes** :
- `/admin`, l'accueil de l'administration. Cette story crée la structure des sous-routes, pour DEMO-F2 à F5.
- `/admin/parametres-legaux` : liste par domaine (santé, réintégration, psychosocial, audit) avec la valeur en vigueur.
- `/admin/parametres-legaux/:code` : historique des versions (valeur, du, au, auteur) et « Nouvelle version » (valeur, date d'effet).
- `/admin/calendrier/:annee` : jours fériés et ajout d'un jour.
- `/admin/nomenclatures` et `/admin/nomenclatures/:code` : entrées avec leurs libellés FR, NL, DE et EN.

**Écran composite du BFF et appels d'API** : le relais appelle :
- `GET referentiels/parametres-legaux`, `GET /{code}` et `POST /{code}/valeurs` ;
- `GET calendrier/{annee}/jours-feries` et `POST calendrier/jours-feries` ;
- `GET nomenclatures`, `GET /{code}`, `POST nomenclatures` et `POST /{code}/entrees`.

**Manques backend à combler** : aucun.

**Droits par rôle** :
- Écriture : `referentiels:administrer` (admin-fonctionnel).
- Lecture : les autres rôles de `/admin` (gestionnaire, dpo, cpmt-dirigeant, cpap-dirigeant) ont `referentiels:lire`, sans boutons d'écriture.

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Paramètres légaux versionnés

  Scénario: Nouvelle version avec date d'effet
    Étant donné l'administratrice fonctionnelle Claire Dupont connectée
    Quand elle ouvre le paramètre SANTE.REPRISE.DELAI
    Alors la valeur en vigueur (10 jours ouvrables) et son historique sont affichés
    Quand elle crée une version à 8 jours ouvrables, effective au 1er janvier de l'année suivante
    Alors la version actuelle se termine la veille et la nouvelle apparaît dans l'historique, avec son auteur

  Scénario: Date d'effet passée refusée
    Quand Claire Dupont crée une version effective hier
    Alors le message d'erreur traduit du service est affiché sur le champ date

  Scénario: Jour férié et calcul des délais
    Étant donné Claire Dupont sur le calendrier de l'année en cours
    Quand elle ajoute un jour férié
    Alors il est listé
    Et une réservation d'urgence d'examen de reprise (DEMO-B2) n'utilise pas ce jour

  Scénario: Lecture seule
    Étant donné le gestionnaire Julie Peeters connectée
    Quand elle ouvre SANTE.REPRISE.DELAI
    Alors le bouton "Nouvelle version" est absent

  Scénario: Libellés multilingues obligatoires
    Quand Claire Dupont ajoute une entrée de nomenclature sans libellé néerlandais
    Alors l'enregistrement est refusé et le champ NL est signalé
```

**Accessibilité et i18n** :
- L'historique est un tableau avec `caption`.
- Les quatre libellés sont dans un `fieldset` « Libellés ».
- Portée `admin`.

**Hors périmètre** :
- import du calendrier ;
- suppression de version (interdite, on clôture) ;
- workflow d'approbation.

**Dépendances** : aucune. Crée `admin.routes.ts` pour F2 à F5 : à fusionner en premier.

---

### [DÉMO F] Administration : référentiel des risques, règles de surveillance et import

Réf. : DEMO-F2

- **Jalon** : Lot 1 – Socle
- **Labels** : `type:exigence-fonctionnelle`, `svc:plateforme`, `svc:postes-risques`
- **Priorité** : P0
- **Taille** : M
- **Zone** : standard
- **Parcours démo** : F (P6)
- **Exigences** : AFF-11 (#30), AFF-12 (#31), NF-61 (#181)
- **Tickets liés** : #389, #3

**Corps**

**Besoin** : En tant qu'administratrice fonctionnelle, je veux consulter et faire évoluer le référentiel des risques et leurs règles de surveillance, et l'importer depuis un fichier, afin que les obligations suivent la réglementation.

**Écrans et routes** :
- `/admin/risques` : liste filtrable (code, libellé, famille).
- `/admin/risques/:code` : règles versionnées (type de surveillance, fréquence, actes, vaccinations, surveillance prolongée, validité) et « Nouvelle règle ».
- `/admin/risques/import` : fichier CSV et date de validité, avec une simulation puis une confirmation et un rapport ligne par ligne.

**Écran composite du BFF et appels d'API** : le relais appelle :
- `GET postes-risques/risques` et `GET /{code}` ;
- `POST risques` et `POST risques/{code}/regles` ;
- `POST risques/import?valideDu=` (multipart).

**Manques backend à combler** : un mode simulation de l'import, à vérifier. Sinon, la simulation est hors périmètre.

**Droits par rôle** :
- Écriture : admin-fonctionnel (permission du service, à confirmer dans `Security.cs`).
- Lecture : `poste:lire`.

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Référentiel des risques

  Scénario: Règles d'un risque
    Étant donné l'administratrice fonctionnelle Claire Dupont connectée
    Quand elle ouvre le risque EX.BIO.HEPATITE_B
    Alors ses règles en vigueur (surveillance, fréquence, vaccination hépatite B) sont affichées

  Scénario: Nouvelle règle versionnée
    Quand Claire Dupont ajoute une règle à EX.PHYS.BRUIT, valable à partir d'une date future
    Alors l'ancienne règle reste dans l'historique avec sa date de fin

  Scénario: Import CSV avec erreurs
    Quand Claire Dupont importe un CSV dont une ligne a un code inconnu
    Alors le rapport indique la ligne et l'erreur
    Et les lignes valides sont traitées selon le mode idempotent du service

  Scénario: Lecture seule
    Étant donné le gestionnaire Julie Peeters connectée
    Alors les boutons d'écriture sont absents
```

**Accessibilité et i18n** :
- Le champ fichier a un libellé et un format d'aide.
- Le rapport d'import est un tableau, annoncé (`role="status"`).
- Portée `admin`.

**Hors périmètre** :
- surcharges de fréquence (AFF-13) ;
- profils de postes (portail employeur et DEMO-D2).

**Dépendances** : DEMO-F1 pour `admin.routes.ts`, rien d'autre.

---

### [DÉMO F] Administration : modèles de documents (versions, validation, publication, aperçu)

Réf. : DEMO-F3

- **Jalon** : Lot 2 – Surveillance de santé
- **Labels** : `type:exigence-fonctionnelle`, `svc:plateforme`, `svc:documents`
- **Priorité** : P0
- **Taille** : M
- **Zone** : standard (les modèles médicaux sont validés par le CPMT dirigeant)
- **Parcours démo** : F (P6)
- **Exigences** : DOC-01 (#135), DOC-02 (#136), NF-41 (#176), NF-61 (#181)
- **Tickets liés** : #389, #11

**Corps**

**Besoin** : En tant qu'administratrice fonctionnelle, je veux gérer les modèles de documents par langue, avec une validation avant publication et un aperçu, afin de faire évoluer les courriers et les formulaires sans développement.

**Écrans et routes** :
- `/admin/modeles` : liste (code, langue, zone, statut brouillon, validé ou publié, version).
- `/admin/modeles/:id` : contenu, champs de fusion, versions, et les actions « Nouvelle version », « Valider », « Renvoyer en brouillon », « Publier », « Aperçu » (fusion d'essai sur des données fictives, PDF).

**Écran composite du BFF et appels d'API** : le relais appelle :
- `GET documents/modeles`, `GET /{id}`, `POST` et `PUT` ;
- `POST /{id}/versions`, `/validation`, `/renvoi-en-brouillon`, `/publication` et `/apercu`.

**Manques backend à combler** : l'instance qui porte les modèles médicaux (`SANTE.EVALUATION.TRAVAILLEUR`, `SANTE.EVALUATION.DOSSIER`) est à confirmer (hypothèse H11).

**Droits par rôle** :
- Gérer : `documents:modele-gerer` (admin-fonctionnel).
- Valider un modèle standard : `documents:modele-valider` (admin-fonctionnel).
- Valider un modèle médical : `documents:modele-valider-medical` (cpmt-dirigeant).
- Lecture : `documents:modele-lire`.

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Modèles de documents

  Scénario: Aperçu du formulaire employeur
    Étant donné l'administratrice fonctionnelle Claire Dupont connectée
    Quand elle ouvre le modèle SANTE.EVALUATION.EMPLOYEUR en néerlandais
    Et demande un aperçu
    Alors un PDF fusionné avec des données fictives s'ouvre

  Scénario: Nouvelle version validée puis publiée
    Quand Claire Dupont crée une nouvelle version du modèle SANTE.EVALUATION.EMPLOYEUR en français
    Alors la version est en "brouillon"
    Quand elle la valide puis la publie
    Alors la version publiée est utilisée pour les prochaines décisions signées

  Scénario: Modèle médical validé par le CPMT dirigeant
    Étant donné une nouvelle version du modèle SANTE.EVALUATION.DOSSIER en brouillon
    Quand Claire Dupont tente de la valider
    Alors le bouton "Valider" est absent
    Quand le CPMT dirigeant Marc Dubois la valide
    Alors elle passe à "validé"
```

**Accessibilité et i18n** :
- L'éditeur est un champ texte libellé, avec la liste des champs de fusion insérables au clavier.
- L'aperçu s'ouvre dans un nouvel onglet annoncé.
- Portée `admin`.

**Hors périmètre** :
- éditeur WYSIWYG ;
- comparaison de versions ;
- modèles psychosociaux.

**Dépendances** : DEMO-F1 pour `admin.routes.ts`.

---

### [DÉMO F] Administration : intégrations, journal des flux et DIMONA simulée

Réf. : DEMO-F4

- **Jalon** : Lot 1 – Socle
- **Labels** : `type:exigence-fonctionnelle`, `svc:plateforme`, `svc:personnes`
- **Priorité** : P1
- **Taille** : M
- **Zone** : standard
- **Parcours démo** : F (P9)
- **Exigences** : INT-02 (#153), AFF-20 (#34), NF-14 (#166)
- **Tickets liés** : #389, #13

**Corps**

**Besoin** : En tant que gestionnaire, je veux suivre les flux d'intégration (volumes, erreurs, relance) et déclencher une exécution DIMONA simulée, afin de montrer l'alimentation automatique des travailleurs.

**Écrans et routes** :
- `/admin/integrations` : volumes par flux, erreurs récentes, journal paginé.
- `/admin/integrations/journal/:id` : détail, avec « Relancer ».
- « Exécuter maintenant » sur un flux (simulateur imposé hors production, NF-14).
- `/admin/integrations/correspondances` : correspondances BCE.

**Écran composite du BFF et appels d'API** : le relais appelle :
- `GET integrations/flux/journal`, `/erreurs` et `/volumes` ;
- `GET flux/journal/{id}` et `POST /relance` ;
- `POST flux/{flux}/executions` ;
- `GET` et `PUT correspondances` ;
- `GET bce/entreprises/{numeroBce}`.

**Manques backend à combler** : le contenu exact de l'exécution DIMONA simulée (nombre et affilié des entrées créées) est à confirmer pour rendre P9 reproductible.

**Droits par rôle** : `integrations:administrer` (admin-fonctionnel, gestionnaire).

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Intégrations

  Scénario: Exécution DIMONA simulée
    Étant donné le gestionnaire Julie Peeters connectée
    Quand elle exécute le flux DIMONA
    Alors une nouvelle entrée de journal indique le volume traité
    Et le nouveau travailleur entré apparaît dans la fiche de son affilié, avec ses obligations calculées

  Scénario: Relance d'une erreur
    Étant donné une entrée de journal en erreur
    Quand Julie Peeters la relance
    Alors le nouveau statut est affiché et annoncé

  Scénario: Profil non autorisé
    Étant donné le CPMT Anne Martin connectée
    Alors le menu Administration est absent
```

**Accessibilité et i18n** :
- Les volumes sont présentés en tableau, pas en graphique seulement.
- Portée `admin`.

**Hors périmètre** :
- flux réels BCSS, eHealth ou DmfA ;
- planification des exécutions.

**Dépendances** : DEMO-F1 pour `admin.routes.ts`. DEMO-A4 pour constater l'effet de la DIMONA (sinon, DEMO-A3).

---

### [DÉMO F] Administration : journal d'audit, bris de glace et intégrité

Réf. : DEMO-F5

- **Jalon** : Lot 1 – Socle
- **Labels** : `type:exigence-non-fonctionnelle`, `svc:plateforme`
- **Priorité** : P1
- **Taille** : M
- **Zone** : transverse (les entrées d'audit ne portent que des identifiants)
- **Parcours démo** : F (P10)
- **Exigences** : NF-04 (#159), NF-02 (#157), TSK-DROITS (#260)
- **Tickets liés** : #389, #15

**Corps**

**Besoin** : En tant que DPO, je veux consulter le journal des accès aux données sensibles, les bris de glace et l'intégrité de la chaîne, afin de prouver la conformité (RGPD art. 9, NF-04).

**Écrans et routes** :
- `/admin/audit` : entrées filtrables (zone, période, acteur, type d'objet, action), avec leur détail.
- `/admin/audit/bris-de-glace` : liste des bris de glace.
- `/admin/audit/integrite` : vérification par zone.
- `/admin/audit/droits` : matrice des droits.

**Écran composite du BFF et appels d'API** : le relais appelle :
- `GET audit/audit/entrees` et `/entrees/{id}` ;
- `GET /bris-de-glace` ;
- `GET /integrite/{zone}` ;
- `GET /droits/matrice`.

**Manques backend à combler** : aucun. Les identifiants de personnes ne sont pas résolus en noms pour la DPO, qui n'a pas `personne:lire` (hypothèse H12, moindre privilège).

**Droits par rôle** :
- `audit:lire` et une zone.
- La DPO voit toutes les zones.
- Le CPMT dirigeant voit la zone médicale, le CPAP dirigeant la zone psychosociale.

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Journal d'audit

  Scénario: Accès motivé tracé
    Étant donné le CPMT dirigeant Marc Dubois qui a ouvert la consultation de Nadia Lambert avec un motif (DEMO-C1)
    Et la DPO Isabelle Renard connectée
    Quand elle filtre le journal sur la zone médicale et la date de J
    Alors l'entrée de lecture par Marc Dubois est listée avec son motif

  Scénario: Intégrité
    Quand Isabelle Renard vérifie l'intégrité de la zone médicale
    Alors le résultat "chaîne intègre" est affiché

  Scénario: Périmètre du CPMT dirigeant
    Étant donné Marc Dubois connecté
    Quand il ouvre le journal
    Alors seule la zone médicale est proposée

  Scénario: Profil non autorisé
    Étant donné le gestionnaire Julie Peeters connectée
    Quand elle ouvre /admin/audit
    Alors l'écran indique que l'accès est réservé
```

**Accessibilité et i18n** :
- Les filtres sont dans un formulaire libellé.
- Les résultats paginés sont annoncés.
- Portée `admin`.

**Hors périmètre** :
- export du journal ;
- registre des traitements (NF-11) ;
- alertes en temps réel.

**Dépendances** : DEMO-F1 pour `admin.routes.ts`. Le scénario du motif suppose DEMO-C1 et DEMO-T1.

---

### [DÉMO H] Documents au portail employeur

Réf. : DEMO-H1

- **Jalon** : Lot 2 – Surveillance de santé
- **Labels** : `type:exigence-fonctionnelle`, `svc:bff-employeur`, `svc:documents`
- **Priorité** : P1
- **Taille** : M
- **Zone** : standard
- **Parcours démo** : H (P11)
- **Exigences** : POR-05 (#128), POR-06 (#129), SAN-33 (#58), NF-40 (#175), NF-50 (#177)
- **Tickets liés** : #389, #356, #436

**Corps**

**Besoin** : En tant qu'employeur, je veux retrouver et télécharger sur le portail les formulaires d'évaluation de santé et les documents légaux de mes affiliés, afin de respecter mes obligations sans courrier papier.

**Écrans et routes** :
- Page `portail-employeur/src/app/documents/`, route `/documents`, avec le sélecteur d'affilié existant (`ui/par-affilie.ts`).
- Liste : type, travailleur ou objet, date, langue, bouton de téléchargement.
- Une entrée de navigation.

**Écran composite du BFF et appels d'API** : nouveaux endpoints du BFF employeur, sur le modèle d'`EcransDocuments` du BFF travailleur :
- `GET /api/v1/affilies/{affilieId}/documents` : `documents?typeDestinataire=affilie&destinataireId={affilieId}`, instance standard seulement, documents publiés uniquement ;
- `GET /api/v1/affilies/{affilieId}/documents/{documentId}/contenu`. Un document d'un autre destinataire est traité comme inconnu (404).

Le périmètre est contrôlé par `PerimetreEmployeur`.

**Manques backend à combler** :
- les deux endpoints et leurs tests ;
- la casse de `typeDestinataire` alignée avec le service Documents (voir #436) ;
- l'instantané OpenAPI.

**Droits par rôle** :
- `documents:lire` pour l'employeur et le SIPP, limité à leurs `affilie_id`.
- Aucun exemplaire travailleur ou dossier n'est accessible (ADR 0009).

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Documents au portail employeur

  Scénario: Liste des formulaires
    Étant donné l'employeur Eva Claes connectée au portail employeur
    Quand elle ouvre "Documents" pour Boulangerie Démo SRL
    Alors les exemplaires employeur des décisions signées de la Boulangerie sont listés, en français
    Et elle peut télécharger le PDF de la décision "Apte" de Nadia Lambert

  Scénario: Nouvelle décision visible
    Étant donné le CPMT Anne Martin qui signe la décision de boulangerie-10 (DEMO-C2)
    Quand Eva Claes actualise la page "Documents"
    Alors en 30 secondes au plus le nouveau formulaire apparaît

  Scénario: Périmètre de l'employeur
    Quand Eva Claes demande les documents de Zorg Demo vzw par l'URL
    Alors la réponse est 403 et aucun document n'est affiché

  Scénario: Exemplaire médical jamais exposé
    Quand Eva Claes demande le contenu d'un exemplaire Travailleur par son identifiant
    Alors la réponse est 404

  Scénario: Interface en anglais
    Étant donné le portail employeur affiché en anglais
    Alors les libellés de la page "Documents" sont en anglais
```

**Accessibilité et i18n** :
- Tableau avec `caption`.
- Lien de téléchargement explicite (« Télécharger le formulaire de Nadia Lambert, PDF »).
- FR, NL, DE et EN (portail).

**Hors périmètre** :
- accusé de lecture horodaté (POR-05, ticket de suite) ;
- rapports annuels ;
- notifications.

**Dépendances** : aucune bloquante. La démonstration en direct suppose DEMO-C2.

---

### [DÉMO T] Accès renforcé à la zone médicale : montée en niveau (NF-01) et motif d'accès

Réf. : DEMO-T1

- **Jalon** : Lot 1 – Socle
- **Labels** : `type:exigence-non-fonctionnelle`, `svc:plateforme`, `zone:medicale`
- **Priorité** : P1
- **Taille** : M
- **Zone** : médicale
- **Parcours démo** : transverse (P4, P10)
- **Exigences** : NF-01 (#156), NF-04 (#159), NF-02 (#157), TSK-DROITS (#260)
- **Tickets liés** : #389, `docs/securite/authentification-forte.md` (point « Côté client, à faire »)

**Corps**

**Besoin** : En tant que CPMT, je veux que l'application me demande une authentification forte ou un motif au moment où le service l'exige, puis rejoue ma requête, afin d'accéder au dossier ou de signer sans perdre ma saisie.

**Écrans et routes** :
- Pas de page. Un intercepteur et un dialogue dans le socle de l'application interne (`app-interne/src/app/api/`) :
  - sur un 401 avec `WWW-Authenticate: ... error="insufficient_user_authentication"`, relancer l'autorisation avec `acr_values` (Keycloak) ou `claims` (MSAL), puis rejouer la requête ;
  - sur un 403 avec le code `dossier-sante.motif-obligatoire` (ou son équivalent en réintégration), ouvrir un `<dialog>` « Motif d'accès » (motif, case « bris de glace » avec avertissement), puis rejouer avec `X-Motif-Acces` ou `X-Bris-De-Glace`.
- Le motif est gardé en mémoire pour la personne et l'onglet pendant 15 minutes (hypothèse H13), jamais stocké.

**Écran composite du BFF et appels d'API** : aucun nouveau. Le relais et les composites transmettent les en-têtes et le défi `WWW-Authenticate`, déjà exposé par le CORS.

**Manques backend à combler** : aucun côté services. La propagation des en-têtes par les composites relève de DEMO-C1.

**Droits par rôle** : inchangés. Un rôle sans permission reçoit toujours 403, jamais un défi.

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Accès renforcé

  Scénario: Montée en niveau à la signature
    Étant donné les services en mode Enforce
    Et le CPMT Hélène Fontaine connectée avec un mot de passe seul
    Quand elle signe une décision
    Alors Keycloak demande l'OTP
    Et après validation la signature aboutit, avec le formulaire intact

  Scénario: Motif hors relation de soin
    Étant donné le CPMT dirigeant Marc Dubois connecté
    Quand il ouvre la consultation de Nadia Lambert
    Alors le dialogue "Motif d'accès" s'ouvre et reçoit le focus
    Quand il saisit "Remplacement du Dr Martin"
    Alors la vue de consultation s'affiche
    Et l'accès est tracé avec ce motif (DEMO-F5)

  Scénario: Bris de glace
    Quand Marc Dubois coche "bris de glace" et saisit un motif
    Alors un avertissement explique l'alerte au CPMT dirigeant
    Et la requête est rejouée avec X-Bris-De-Glace

  Scénario: Abandon
    Quand Marc Dubois annule le dialogue
    Alors aucune requête n'est rejouée
    Et l'écran indique que l'accès nécessite un motif

  Scénario: Pas de défi pour un rôle non autorisé
    Étant donné le planificateur Nicolas Lemaire
    Quand une requête vers le dossier de santé est faite
    Alors la réponse 403 est affichée sans dialogue de motif
```

**Accessibilité et i18n** :
- `<dialog>` natif avec focus piégé et rendu à l'élément d'origine.
- Avertissement du bris de glace en `role="alert"`.
- Clés dans la portée globale (`errors.*`, `acces.*`), en FR, NL et DE.

**Hors périmètre** :
- fédération CSAM (#282) ;
- configuration Entra ID ;
- motif pré-rempli par liste.

**Dépendances** : aucune. Utilisée par DEMO-C1, DEMO-C2, DEMO-E1 et DEMO-F5.

---

### [DÉMO] Vérification de bout en bout des parcours P1 à P11

Réf. : DEMO-T2

- **Jalon** : Lot 3 – Réintégration et portails complets
- **Labels** : `type:tache-technique`, `svc:plateforme`
- **Priorité** : P0
- **Taille** : L
- **Zone** : standard et médicale
- **Parcours démo** : tous (P1 à P11)
- **Exigences** : REC-01 (#250), REC-02 (#251), REC-03 (#252), CTR-23 (#235), NF-14 (#166), NF-31 (#171), NF-50 (#177)
- **Tickets liés** : #389, #393, #436

**Corps**

**Besoin** : En tant que responsable produit, je veux que chaque parcours de démo soit vérifié à l'écran sur un environnement neuf, afin de présenter la démo sans incident.

**Écrans et routes** : l'application interne, le portail employeur et le portail travailleur, avec les comptes de la section 2.

**Écran composite du BFF et appels d'API** : aucun nouveau.

Méthode :
- `deploy/local/demo.ps1 --projet veilla-verif`, sur un projet compose distinct. Le projet `veilla` de l'utilisateur et ses volumes ne sont jamais touchés.
- Déroulé de P1 à P11 avec la liste de contrôle du script (DEMO-T3).
- Pour chaque parcours :
  - interface en FR, NL et DE ;
  - console du navigateur ;
  - temps d'ouverture des dossiers (NF-31) ;
  - contrôle automatique d'accessibilité.
- Bugs ouverts en tickets.
- Rapport daté dans `docs/03-qualite/`.
- Outillage automatique (Playwright, axe) : à décider par le QA et l'architecte, une nouvelle dépendance doit être justifiée.

**Manques backend à combler** : ceux qui sont découverts, en tickets distincts.

**Droits par rôle** : vérifier pour chaque parcours le scénario de refus du rôle non autorisé.

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Vérification de bout en bout

  Scénario: Environnement neuf
    Étant donné demo.ps1 lancé sur le projet compose veilla-verif
    Quand l'amorçage se termine
    Alors le manifeste liste 3 affiliés, 40 travailleurs, 8 décisions signées, 1 brouillon et 1 examen en cours
    Et le projet compose veilla de l'utilisateur est inchangé

  Scénario: Parcours indispensables
    Quand P1 à P6 sont déroulés avec les comptes prévus
    Alors chaque étape produit le résultat attendu du script
    Et aucun écran ne répond "ecran.non-implemente"
    Et la console du navigateur ne contient aucune erreur

  Scénario: Langues
    Quand chaque écran de P1 à P6 est affiché en néerlandais puis en allemand
    Alors aucune clé de traduction n'est manquante

  Scénario: Performance
    Quand le dossier de Nadia Lambert et la consultation sont ouverts 20 fois
    Alors le 95e percentile est inférieur à 2 secondes

  Scénario: Parcours souhaitables
    Quand P7 à P11 sont déroulés
    Alors chaque écart est consigné dans le rapport, avec un ticket et une priorité
```

**Accessibilité et i18n** : contrôle WCAG 2.1 AA de chaque écran des parcours (clavier, lecteur d'écran sur 2 écrans au moins, contraste), en FR, NL et DE.

**Hors périmètre** :
- tests de charge ;
- tests d'intrusion ;
- environnement Azure.

**Dépendances** :
- toutes les stories P0 ;
- #393 (données, PR #438) ;
- #436 ;
- les P1 livrées pour P7 à P11.

---

### [DÉMO] Script de démo de 15 à 20 minutes

Réf. : DEMO-T3

- **Jalon** : Lot 3 – Réintégration et portails complets
- **Labels** : `type:tache-technique`, `svc:plateforme`
- **Priorité** : P0
- **Taille** : M
- **Zone** : sans objet
- **Parcours démo** : tous
- **Exigences** : aucune exigence directe. Le script démontre P1 à P11 et illustre SAN-30, AFF-14, NF-01, NF-04 et NF-40.
- **Tickets liés** : #389

**Corps**

**Besoin** : En tant que commercial ou responsable produit, je veux un script minuté de la démo, avec les comptes, les données et un plan de secours, afin de présenter veilla en 15 à 20 minutes à un SEPP.

**Écrans et routes** : livrable `docs/01-produit/demo-script.md`.

Trame indicative :

| Temps | Parcours |
|---|---|
| 1 min | Introduction |
| 2 min | P1, tableau de bord d'Anne Martin |
| 2 min | P2, recherche de Nadia Lambert et dossier |
| 3 min | P3, planificateur : reprise de construct-10 en urgence |
| 5 min | P4 : salle d'attente, consultation de Nadia Lambert, décision, signature, PDF FR, portail travailleur, portail employeur |
| 3 min | P5 : proposition d'Eva Claes, validation, obligations recalculées |
| 2 min | P6 : paramètre légal versionné, modèle NL |
| 2 min en réserve | P7 à P10 selon le public : vaccins, réintégration, DIMONA, audit |

Le script comprend aussi :
- la préparation : `demo.ps1` le matin même, car les rendez-vous sont à J ;
- les comptes à ouvrir, dans des profils de navigateur séparés ;
- le plan B : un service arrêté reste affiché grâce aux sections tolérantes, et des captures de secours sont prêtes ;
- la remise à zéro.

**Écran composite du BFF et appels d'API** : sans objet.

**Manques backend à combler** : sans objet.

**Droits par rôle** : le script fait apparaître au moins un refus (le planificateur sans donnée médicale) et un motif d'accès.

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Script de démo

  Scénario: Répétition chronométrée
    Étant donné un environnement préparé selon le script
    Quand une personne autre que l'auteur déroule le script
    Alors la démo tient en 20 minutes au plus, sans étape bloquée

  Scénario: Données traçables
    Alors chaque étape cite le compte, la personne ou l'affilié du manifeste à utiliser

  Scénario: Plan de secours
    Étant donné un service indisponible pendant la démo
    Alors le script indique l'étape de repli
```

**Accessibilité et i18n** :
- Le script prévoit un passage d'interface en néerlandais.
- Une version NL du script est à décider (question Q2).

**Hors périmètre** :
- vidéo enregistrée ;
- démo sur Azure.

**Dépendances** : DEMO-T2. La rédaction peut démarrer en vague 3, puis être ajustée après T2.

---

### [DÉMO] Présentation commerciale : architecture, conformité et feuille de route

Réf. : DEMO-T4

- **Jalon** : Lot 3 – Réintégration et portails complets
- **Labels** : `type:tache-technique`, `svc:plateforme`
- **Priorité** : P1
- **Taille** : M
- **Zone** : sans objet
- **Parcours démo** : support de la démo
- **Exigences** : ARC-04, ARC-06 (#189), NF-01 (#156), NF-04 (#159), NF-13, NF-14 (#166), NF-50 (#177), ARC-09
- **Tickets liés** : #389

**Corps**

**Besoin** : En tant que commercial, je veux une présentation courte qui explique l'architecture, la conformité et la feuille de route, afin d'accompagner la démo face à un SEPP.

**Écrans et routes** : livrable dont le format et l'emplacement sont à décider (question Q1), par exemple `docs/00-pilotage/presentation-commerciale.md` ou des diapositives. Contenu :
- problème et proposition de valeur ;
- architecture (microservices, trois zones, BFF par canal, outbox) ;
- conformité :
  - RGPD art. 9 ;
  - secret médical et relation de soin ;
  - NF-01 ;
  - journal d'audit infalsifiable ;
  - hébergement UE (Belgium Central) ;
  - pseudonymisation hors production ;
  - WCAG 2.1 AA ;
  - FR, NL et DE ;
- couverture du cahier des charges par lot, chiffrée depuis les tickets GitHub ;
- feuille de route (lots 3 à 5).

**Écran composite du BFF et appels d'API** : sans objet.

**Manques backend à combler** : sans objet.

**Droits par rôle** : sans objet. Aucune donnée personnelle réelle ni secret.

**Critères d'acceptation**

```gherkin
# language: fr
Fonctionnalité: Présentation commerciale

  Scénario: Affirmations traçables
    Alors chaque affirmation d'architecture ou de conformité renvoie à un ADR, à un document de docs/ ou à un ticket

  Scénario: Couverture chiffrée
    Alors la couverture du cahier des charges est calculée à partir de l'état des tickets à la date de la présentation

  Scénario: Validation
    Alors la présentation est validée par le responsable produit avant la première démo
```

**Accessibilité et i18n** : contrastes et tailles lisibles en projection. Langue FR, version NL à décider (Q2).

**Hors périmètre** :
- tarification ;
- engagements contractuels ;
- planning chiffré (`project-manager`).

**Dépendances** : aucune pour démarrer. Les chiffres finaux viennent après DEMO-T2.

---

## 6. Hypothèses

- **H1** : `docs/00-pilotage/etat-des-lieux-backlog.md` et `docs/architecture/etat-des-lieux-architecture.md` sont absents du dépôt. Le chemin critique repose sur le plan de démo et le code au 2026-11-14.
- **H2** : Nadia Lambert est `boulangerie-0`, d'après `Catalogue.cs` (« première travailleuse de la Boulangerie »), et le compte `travailleur` lui est lié par la PR #438. Le profil Keycloak actuel s'appelle encore Théo Mertens (question Q6).
- **H3** : les personnes désignées par une clé (`boulangerie-10`, `construct-9`…) prennent le nom du manifeste. Les critères gardent la clé.
- **H4** : J est le jour où `demo.ps1` est lancé. Lancé un jour non ouvrable, les rendez-vous tombent au prochain jour ouvrable.
- **H5** : en local, le contrôle NF-01 est en mode `Log`. La signature passe sans montée en niveau, et DEMO-T1 sert à montrer le mode `Enforce`.
- **H6** : les droits sont ceux de `matrice-droits.csv` et de `roles.ts` au 2026-11-14.
- **H7** : jalon de chaque story :
  - la majorité de ses exigences le fixe ;
  - les stories transverses sont en Lot 3, car elles clôturent la démo.
- **H8** : le `centreId` du stock de vaccins correspond au lieu de planification « Centre SEPP Bruxelles ».
- **H9** : `POST rendez-vous/{id}/absence` n'a pas de `RequirePermission` explicite. Le bouton suit la réponse du service.
- **H10** : la salle d'attente se rafraîchit toutes les 30 secondes, sans temps réel (SignalR hors périmètre).
- **H11** : l'instance Documents qui porte les modèles médicaux est à confirmer. Le jeu de démo les valide avec `cpmt.dirigeant`.
- **H12** : la DPO voit des identifiants, pas des noms, dans le journal d'audit.
- **H13** : le motif d'accès est conservé 15 minutes en mémoire, par personne et par onglet.
- **H14** : les labels `svc:referentiels`, `svc:audit`, `svc:integrations` et `svc:communications` n'existent pas. Ces services sont étiquetés `svc:plateforme`, avec le service propriétaire cité dans le corps.
- **H15** : nom et prénom ne sont pas chiffrés dans Personnes (seul le NISS l'est, DAT-06 et ARC-45). Sinon, la recherche par nom demande un index aveugle (DEMO-A1, Q4).

## 7. Questions ouvertes

| N° | Question | Destinataire | Bloquante ? |
|---|---|---|---|
| Q1 | Quels sont le format (diapositives ou Markdown), le public et l'emplacement de la présentation commerciale ? | Responsable produit | Non (T4) |
| Q2 | La démo se fait-elle en français seulement, ou avec un passage en néerlandais ? Faut-il un script et une présentation en NL ? | Responsable produit | Non (T3, T4) |
| Q3 | Pour DEMO-C4, faut-il relayer `documents-medicale` ou créer un composite ? Un ADR est à rédiger. | Architecte solution et architecte sécurité | Non (P1) |
| Q4 | Pour DEMO-A1, l'extension PostgreSQL de recherche (`unaccent`, `pg_trgm`) est-elle acceptable ? Faut-il un index aveugle si les noms sont chiffrés ? | Architecte solution | Non, mais à trancher avant le développement de A1 |
| Q5 | Le gestionnaire doit-il voir un tableau des délais tous affiliés, sans donnée médicale ? Le suivi par affilié suffit-il ? | Responsable produit | Non |
| Q6 | Le profil Keycloak du compte `travailleur` est-il renommé en Nadia Lambert par la PR #438 ? Quels noms le manifeste attribue-t-il aux clés citées ? | Auteur de la PR #438 | Non |
| Q7 | Où est tracée la décision DA-02 du 2026-11-14 ? Elle est introuvable dans `docs/`. | Responsable produit ou chef de projet | Non |
| Q8 | #351 est au jalon Lot 3, alors que son écran est couvert par DEMO-D2 (Lot 2). Faut-il scinder #351 ou changer son jalon ? | Responsable produit | Non |
