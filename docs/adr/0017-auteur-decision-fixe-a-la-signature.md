# ADR 0017 — Décision d'évaluation de santé : auteur légal fixé à la signature

## Statut
Acceptée le 2026-11-15 par le responsable produit (#401, PR #488). Modifie une règle du domaine de Surveillance médicale
(agrégat `Decision`).

## Contexte
Avant #401, seul le CPMT rédigeait la décision d'évaluation de santé. Le CPMT qui créait le brouillon en était l'auteur
légal, et le domaine n'autorisait que lui à la signer.

Le responsable produit a ouvert la rédaction et la modification à l'infirmier (`decision:ecrire`) et réservé la signature
au CPMT (`decision:signer`). L'ancienne règle devenait alors intenable :
- un brouillon de l'infirmier aurait l'infirmier pour auteur, et personne ne pourrait le signer ;
- la première itération de #401 désignait comme auteur le CPMT gestionnaire du dossier. Elle échouait sans gestionnaire
  (`decision.cpmt-requis`), attribuait au gestionnaire une décision qu'il n'avait ni écrite ni relue, et ne permettait pas
  à un CPMT remplaçant de signer.

La revue de la PR #488 a aussi demandé de tracer qui a préparé et modifié le brouillon, et de garantir que le CPMT signe la
version qu'il a relue.

## Décision
1. **Le brouillon n'a pas d'auteur légal.** Il porte son rédacteur réel (`RedigeePar`), son dernier modificateur
   (`ModifieePar`, `ModifieeLe`) et une version de contenu (`VersionContenu`, 1 à la rédaction, +1 à chaque modification).
   `AuteurId` reste vide.
2. **Le CPMT qui signe devient l'auteur légal.** `Decision.Signer` fixe `AuteurId` au signataire. Les conditions sont
   vérifiées en amont du domaine :
   - le droit `decision:signer` (CPMT, CPMT dirigeant) ;
   - une relation de soin avec le travailleur, ou un motif d'accès (ARC-41) ;
   - l'authentification forte (NF-01).

   L'empreinte envoyée au prestataire de signature porte sur le formulaire signataire compris.
3. **La règle « seul l'auteur signe » disparaît, y compris pour un brouillon rédigé par un CPMT.** Raisons :
   - un brouillon n'engage personne : la signature est l'acte qui engage, et c'est elle qui désigne l'auteur ;
   - garder la règle pour les seuls brouillons d'un CPMT obligerait le domaine à connaître le rôle du rédacteur, et
     bloquerait la signature par un remplaçant quand le CPMT rédacteur est absent ;
   - le risque d'une signature « à l'insu » du rédacteur est couvert par trois mécanismes : la traçabilité
     (`RedigeePar`, `ModifieePar`, journal d'accès par dossier), le motif obligatoire hors relation de soin, et le contrôle
     de version.
4. **La signature et la modification portent sur la version relue.**
   - `POST /decisions/{id}/signature` et `PUT /decisions/{id}` exigent l'en-tête `If-Match`, avec la valeur `Version` de
     `GET /decisions/{id}/complete`.
   - Sans cet en-tête, la réponse est 400 (`decision.version-requise`) ; avec une version dépassée, 409
     (`decision.version-obsolete`).
   - Ce contrôle s'ajoute au verrou optimiste technique (`version`, DAT-03), qui couvre les écritures concurrentes, mais
     pas la relecture par l'utilisateur.
5. **Modification par l'infirmier du brouillon d'un autre.** Elle n'est permise qu'en relation de soin avec le
   travailleur, jamais sur simple motif (`decision.relation-de-soin-requise`). L'infirmier garde ses propres brouillons
   avec les règles ARC-41 habituelles.
6. **Migration `AuteurDecisionALaSignature`.**
   - Les décisions existantes reçoivent `redigee_par = auteur_id` et `version_contenu = 1`.
   - Les décisions signées gardent leur auteur.
   - L'auteur des brouillons existants est remis à vide. Le conserver laisserait croire qu'un brouillon a déjà un
     auteur légal, et le signataire le remplacerait de toute façon. L'information n'est pas perdue, puisqu'elle passe
     dans `redigee_par`.
   - La migration inverse recopie `redigee_par` dans `auteur_id` quand ce dernier est vide.

## Alternatives écartées
- **Auteur = CPMT gestionnaire du dossier** (première itération) : refus sans gestionnaire, attribution d'un acte non
  relu, aucun remplacement possible.
- **Garder « seul l'auteur signe » pour les brouillons d'un CPMT** : bénéfice faible, couvert par la traçabilité et le
  contrôle de version ; règle de domaine dépendante des rôles.
- **Empreinte du contenu au lieu d'un numéro de version** : l'empreinte d'un texte médical court exposée hors de la zone
  pourrait être attaquée par dictionnaire. Un numéro de version suffit pour le contrôle de relecture et ne révèle rien.

## Conséquences
- `DecisionDto` (zone médicale) expose `AuteurId` (vide avant signature), `RedigeePar`, `ModifieePar`, `ModifieeLe` et
  `Version`. Le formulaire expose `CpmtId`, vide tant que la décision n'est pas signée.
- Les clients qui signent ou modifient une décision envoient `If-Match`. Sont concernés l'outil de données de démo et la
  solution de tests de bout en bout. Les instantanés OpenAPI de Surveillance médicale déclarent l'en-tête.
- Le BFF interne et l'application interne ne relaient pas encore la signature. L'écran de décision (DEMO-C*) devra
  afficher le rédacteur et le dernier modificateur, puis signer avec `If-Match`.
