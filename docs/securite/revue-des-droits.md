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

Relevés le 2026-10-25, **non corrigés** (les droits n'ont pas été modifiés ; à arbitrer par le métier) :

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
