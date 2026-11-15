# Revue trimestrielle des droits (NF-02)

Le cahier des charges exige un contrôle d'accès par rôle et par périmètre, le moindre privilège et une **revue trimestrielle des
droits** (NF-02). Ce document décrit la procédure, ce qui est automatique et ce qui reste manuel.

## Les deux niveaux de droits à revoir

| Niveau | Source de vérité | Qui le change | Contrôle automatique |
|---|---|---|---|
| **Matrice rôle → permissions** (ce que chaque rôle peut faire) | `RolePermissions` dans `Security.cs`, figée dans [`matrice-droits.csv`](matrice-droits.csv) | Une demande de fusion (revue de code) | Oui : `MatriceDroitsTests` échoue au moindre écart |
| **Attribution rôle → personnes** (qui a quel rôle) | Entra ID (internes), fédération CSAM (employeurs, travailleurs) | L'administrateur de l'annuaire, sur demande | Non : revue manuelle (access reviews Entra) |

Le périmètre (affilié, région, centre) est porté par les claims du jeton (`affilie_id`, `personne_id`) et vérifié par chaque
service propriétaire ; il n'est pas modifiable dans l'application.

## Matrice versionnée et test de non-régression

- [`matrice-droits.csv`](matrice-droits.csv) : une ligne par couple `role;permission`, triée, avec une colonne `origine`
  (`3.3` si la permission correspond à une cellule de la matrice de synthèse du cahier des charges §3.3, `hors-3.3` si elle vient
  d'un autre chapitre ou d'une décision d'architecture).
- `tests/Sepp.BuildingBlocks.Tests/MatriceDroitsTests.cs` compare `RolePermissions` à ce fichier : produit cartésien rôle ×
  permission (permission en trop ou en moins), rôles du code et de la matrice identiques, permission non attribuée ou non
  déclarée, doublons, ordre du fichier, rôle inconnu sans aucun droit, manifeste Entra aligné.
- **Modifier un droit impose donc de modifier le CSV dans la même demande de fusion** : le diff du CSV est la trace lisible du
  changement et le point d'attention du relecteur (principe du moindre privilège).

## Procédure trimestrielle

À réaliser chaque trimestre par le DPO avec le CPMT dirigeant, le CPAP dirigeant et l'administrateur fonctionnel ; la date et les
décisions sont consignées dans le registre de revue (ticket de suivi du trimestre).

1. **Exporter la matrice en vigueur** (automatique), avec un compte portant `audit:lire` (DPO, CPMT dirigeant, CPAP dirigeant) :
   `GET /api/v1/audit/droits/matrice` (JSON) ou `GET /api/v1/audit/droits/matrice?format=csv` (CSV), via l'API Management.
   Comparer avec `docs/securite/matrice-droits.csv` de la version déployée : les deux doivent être identiques (le test garantit
   l'identité au moment de la fusion ; l'export prouve ce qui tourne réellement).
2. **Exporter les attributions de rôles** (semi-automatique) : depuis Entra ID, pour l'application d'API `sepp-api`, la liste des
   utilisateurs et groupes affectés à chaque rôle d'application :

   ```text
   az rest --method GET --url "https://graph.microsoft.com/v1.0/servicePrincipals/<id-service-principal-sepp-api>/appRoleAssignedTo?$top=999" \
     --query "value[].{role:appRoleId, principal:principalDisplayName, type:principalType}" -o tsv
   ```

   Les identifiants de rôle (`appRoleId`) sont ceux de [`entra-app-roles.json`](entra-app-roles.json). Les attributions aux comptes
   techniques (rôles `Application`) sont comparées à la liste attendue de ce même manifeste. L'export contient des données
   nominatives : il est conservé dans l'espace documentaire du DPO, jamais dans le dépôt.
3. **Revue par les responsables** (manuelle) : Entra **Identity Governance > Access reviews** (licence Entra ID Governance /
   P2), une revue récurrente trimestrielle par rôle sensible, relecteur = responsable du périmètre :

   | Rôles | Relecteur |
   |---|---|
   | `cpmt`, `cpmt-dirigeant`, `infirmier`, `assistant-medical` | CPMT dirigeant |
   | `cpap`, `cpap-dirigeant` | CPAP dirigeant |
   | `dpo`, `admin-fonctionnel`, `exploitation` | Direction |
   | autres rôles internes | Responsable de centre / de région |

   Réglages recommandés : refus par défaut si le relecteur ne répond pas, retrait automatique des accès refusés, justification
   obligatoire à l'approbation. Les rôles `Application` (comptes de service) ne sont pas dans une access review : ils se contrôlent
   à l'étape 2.
4. **Employeurs, SIPP, travailleurs** (manuelle) : ces profils viennent de la fédération CSAM ; la revue porte sur le rattachement
   des personnes de contact à l'affilié (service Affiliés, contacts avec rôle et période, AFF-03), pas sur l'annuaire interne.
5. **Zones sensibles** : vérifier que les rôles `dossier-sante:*`, `dossier-psy:*` et `audit:zone-*` ne sont portés que par les
   rôles attendus (voir le CSV) et que les accès « bris de glace » du trimestre ont été examinés (`GET /api/v1/audit/bris-de-glace`).
6. **Consigner et corriger** : les retraits sont faits dans Entra le jour même ; toute évolution de la matrice passe par une
   demande de fusion qui modifie le CSV.

## Automatique et manuel

| Étape | Statut |
|---|---|
| Garde-fou contre toute dérive de la matrice dans le code | Automatique (test, à chaque demande de fusion) |
| Export de la matrice déployée | Automatique (endpoint d'Audit, à la demande) |
| Export des attributions Entra | Commande Graph à lancer par un administrateur de l'annuaire |
| Campagne de revue par les responsables | Entra access reviews, configuration manuelle une fois, puis récurrente |
| Décision, consignation | Manuelle |

## Écarts constatés entre le cahier des charges (§3.3, §3.1) et le code

Relevés le 2026-10-25 (#157), puis arbitrés par le responsable produit le 2026-11-15 (#401). Les décisions sont
consignées dans la section suivante ; la liste ci-dessous garde le constat initial.

1. **Décision d'évaluation de santé, écriture** : le cahier indique « Écriture » pour la colonne « CPMT / infirmier ». Le code
   donne `decision:ecrire` au seul CPMT et au CPMT dirigeant ; l'infirmier n'a que `decision:lire`. Écart probable
   volontaire (la décision est un acte du médecin), à confirmer.
2. **CPMT dirigeant** : le §3.1 lui attribue « Gestionnaire des dossiers » ; le code ne lui donne ni `affilie:ecrire` ni
   `personne:ecrire` (réservés au gestionnaire, à l'employeur et au SIPP).
3. **Dossier psychosocial, employeur** : « Selon procédure » dans le cahier ; aucune permission `dossier-psy:*` n'est
   accordée à l'employeur (comportement le plus restrictif). La procédure n'est pas définie.
4. **Dossier psychosocial, travailleur** : « Ses propres documents » ; aucune permission dédiée, le travailleur ne lit ses
   documents que par `documents:lire` (périmètre vérifié par Documents).
5. **Extensions non prévues par la synthèse §3.3** (permissions hors cahier mais cohérentes avec d'autres chapitres) : le
   travailleur reçoit `affilie:lire-employeurs` (dénomination de ses employeurs, POR-11) alors que la synthèse indique « Non »
   pour les données de l'affilié ; l'employeur et le SIPP ont `decision:lire` et `analyse-risques:lire` comme le gestionnaire.
   Le SIPP, absent de la synthèse, est aligné sur l'employeur.
6. La synthèse ne couvre que cinq familles de données ; les autres permissions du CSV (colonne `hors-3.3`) sont des
   répartitions proposées « à valider » dans les commentaires du code (reprise, réintégration, planification, etc.).

## Décisions du responsable produit (#401, 2026-11-15)

| Écart | Décision | Mise en œuvre |
|---|---|---|
| 1. Décision d'évaluation | Validée : l'infirmier rédige et modifie ; seul le CPMT signe | `decision:ecrire` accordé à l'infirmier ; nouveau `decision:signer` (CPMT, CPMT dirigeant) sur la signature et les autres actes à portée légale (voir ci-dessous) |
| 2. CPMT dirigeant | Validée : gestionnaire des dossiers | `affilie:ecrire` et `personne:ecrire` accordés au CPMT dirigeant |
| 3. Psychosocial, employeur « selon procédure » | **Ouverte** : procédure à définir, proposition en cours | Aucun droit accordé (comportement le plus restrictif) |
| 4. Psychosocial, travailleur « ses propres documents » | Validée : droit dédié | `dossier-psy:publications-propres-lire` accordé au travailleur : il ne voit que les publications que le CPAP lui adresse explicitement, jamais le dossier (même mécanisme que `dossier-psy:publications-lire` pour l'employeur). Droit déclaré sans route : à appliquer par le service Psychosocial (lot 5) |
| 5a. Travailleur, `affilie:lire-employeurs` (POR-11) | Validée : extension maintenue et encadrée | `GET /api/v1/mes-employeurs` ne rend que l'identifiant, la dénomination et la langue des affiliés où la personne du jeton a une occupation active (le régime linguistique, inutilisé, est retiré) |
| 5b. SIPP aligné sur l'employeur | Validée : alignement maintenu et encadré | Test `MatriceDroitsTests.Le_sipp_n_a_jamais_plus_de_droits_que_l_employeur` ; périmètre limité à l'affilié du jeton (claim `affilie_id`), vérifié par chaque service |

Répartition des routes de Surveillance médicale autrefois sous `decision:ecrire` :

| Route | Droit | Motif |
|---|---|---|
| `POST /examens/{id}/decision`, `PUT /decisions/{id}` | `decision:ecrire` | Rédaction et modification d'un brouillon, sans effet hors de la zone médicale |
| `POST /decisions/{id}/signature` | `decision:signer` + authentification forte + `If-Match` | Signature qualifiée et émission du formulaire : acte légal du CPMT, qui en devient l'auteur (SAN-32, ADR 0017) |
| `POST /examens/{id}/cloture` | `decision:signer` | La clôture valide l'examen et satisfait les obligations légales : acte du CPMT |
| `PUT /dossiers/{id}/gestionnaire`, `POST /dossiers` avec `gestionnaireCpmtId` | `dossier-sante:gerer` (CPMT, CPMT dirigeant) | Désignation du CPMT gestionnaire |
| `POST /dossiers/{id}/transferts`, `POST /transferts/{id}/transmission` | `dossier-sante:gerer` | Transfert du dossier vers un autre SEPP ou SIPP (SAN-42) : acte du CPMT |
| `POST /examens/{id}/propositions-frequence/{propositionId}` | `decision:signer` | Décision du CPMT sur la fréquence de surveillance (art. I.4-32, SAN-23) |
| `POST /decisions/{id}/recours` | `decision:ecrire` | Enregistrement de l'introduction d'une concertation ou d'un recours (SAN-34), ouvert à l'infirmier |
| `POST /decisions/{id}/recours/{recoursId}/issue` | `decision:signer` | L'issue peut réformer et retransmettre la décision émise (SAN-34) |
| `POST /dossiers/{id}/declarations-mp`, `POST /declarations-mp/{id}/envoi`, `/demandes-information/{id}/reponse` | `decision:signer` | Déclaration de maladie professionnelle elle-même (diagnostic, transmission et réponses à Fedris) : acte du médecin (SAN-70) |
| `POST /declarations-mp/{id}/suivi`, `POST /declarations-mp/{id}/demandes-information` | `decision:ecrire` | Suivi administratif du statut Fedris et enregistrement d'une demande reçue (SAN-71), ouverts à l'infirmier |

Arbitrages complémentaires du responsable produit sur la PR #488 (ADR 0017) :

- **Auteur légal fixé à la signature.** Le brouillon n'a pas d'auteur légal. Il porte son rédacteur réel (`RedigeePar`),
  son dernier modificateur et la date de modification (`ModifieePar`, `ModifieeLe`), ainsi qu'une version de contenu.
  - Le CPMT qui signe devient l'auteur. Il lui faut `decision:signer`, une relation de soin sur l'examen de la décision
    (professionnel de cet examen ou CPMT gestionnaire du dossier) ou, à défaut, un bris de glace motivé, tracé et signalé
    (ARC-41), ainsi que l'authentification forte. Ni un simple motif ni un autre examen du dossier, même un examen
    spontané ouvert sur motif, ne suffisent pour signer (#495). Un vaccinateur hors de cette relation passe aussi par le
    bris de glace (décision du responsable produit du 2026-11-15).
  - Un dossier sans CPMT gestionnaire ne bloque plus la rédaction.
  - La règle « seul l'auteur signe » est supprimée.
- **Version relue.** La modification et la signature exigent `If-Match`, avec la valeur `Version` rendue par
  `GET /decisions/{id}/complete` (zone médicale seulement). Sans cet en-tête, la réponse est 400 ; avec une version
  dépassée, 409.
- **Brouillon d'un autre.** Tout professionnel, CPMT compris, ne modifie le brouillon d'un autre qu'en relation de soin
  sur l'examen de la décision (professionnel de cet examen ou gestionnaire du dossier), sinon par bris de glace motivé.
  Ni un simple motif ni un autre examen du dossier, même un examen spontané, ne suffisent (revue de #496, P2-1).
- **Désignation du gestionnaire.** Seul le gestionnaire actuel ou un CPMT en relation de soin sur le dossier (seul
  examen non annulé d'un rendez-vous planifié) désigne le gestionnaire. Sinon, un bris de glace motivé est exigé, car un
  simple motif permettrait de se désigner gestionnaire puis de signer (revue de #496, P1-1, ADR 0017 point 7).
- **Un rendez-vous, un examen.** Un rendez-vous ne porte qu'un examen non annulé : refus applicatif (409,
  `examen.rendez-vous-deja-honore`) et index unique partiel en base. Sinon un CPMT hors relation rouvrirait sans motif
  un rendez-vous déjà honoré pour établir la relation (revue de #496, P1-1 bis). Limite connue : le rendez-vous ne
  désigne pas encore le professionnel convoqué, ce qui fait l'objet d'un ticket de suite.
- **Authentification forte.** La clôture d'examen (`decision:signer`) l'exige explicitement. Les routes
  `dossier-sante:gerer` l'exigent par leur préfixe. Depuis #494, toutes les routes `decision:ecrire` et `decision:signer`
  l'exigent aussi par leur préfixe (`decision:lire` excepté) : la rédaction et la modification ouvertes à l'infirmier par
  #401 sont couvertes (détail dans [authentification-forte.md](authentification-forte.md#routes-protégées)).
- **Langue.** Elle est conservée dans `mes-employeurs`.
- **Limite du contrôle du gestionnaire.** Le service n'a pas d'annuaire. Il contrôle seulement que l'identifiant désigné
  n'est pas vide, et que seul un CPMT (`dossier-sante:gerer`) le désigne. Il ne peut pas vérifier que cet identifiant
  appartient à un CPMT.
