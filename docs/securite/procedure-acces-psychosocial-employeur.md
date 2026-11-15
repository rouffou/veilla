---
titre: Procédure d'accès de l'employeur au dossier psychosocial (§3.3 « selon procédure »)
auteur: security-architect
statut: en revue
date: 2026-11-15
---

# Procédure d'accès de l'employeur au dossier psychosocial

Ticket #401, point 3. La matrice des droits du cahier des charges (§3.3) donne à l'employeur un accès au dossier
psychosocial « selon procédure », mais ne définit pas cette procédure. Le chapitre 7 précise que le dossier est « un
espace fermé : seul le CPAP en charge (et son éventuel remplaçant désigné) y accède, et chaque document communiqué suit
les règles de destinataire du Code ».

Ce document est une **proposition à valider par le responsable produit**. Il ne modifie ni `Security.cs` ni
`matrice-droits.csv`. Les droits proposés seront créés au lot 5, avec le service Psychosocial (ADR 0016).

**Principe retenu** : l'employeur n'accède **jamais** au dossier psychosocial. Il reçoit seulement les documents que
le Code lui destine, quand le CPAP les lui **publie** par une action explicite. « Selon procédure » signifie donc
« selon la procédure interne du titre 3 du livre I du Code du bien-être au travail ».

## 1. Cadre légal belge

### 1.1 Sources

| Source | Objet | Fiabilité de la référence |
|---|---|---|
| Code du bien-être au travail, livre I, titre 3 (art. I.3-1 à I.3-66) | Prévention des risques psychosociaux, procédure interne, dossier individuel | Articles relus sur une version consolidée de 2022 publiée par l'ULB. **À vérifier** sur la version consolidée en vigueur (SPF Emploi). |
| Loi du 4 août 1996 relative au bien-être des travailleurs | Base légale de la procédure interne (art. 32/2), protection contre les représailles (art. 32tredecies), saisine de l'inspection (art. 32septies, § 2) | Articles cités par le Code lui-même. |
| Loi du 4 août 1996 : secret professionnel du CPAP et de la personne de confiance | Renvoi à l'article 458 du Code pénal | Le principe est certain. Le numéro d'article de la loi (32quinquiesdecies) est **à vérifier**. |
| RGPD (art. 5, 6, 9, 32) et loi du 30 juillet 2018 | Minimisation, sécurité, données de santé éventuelles | Qualification art. 9 des données psychosociales **à vérifier** avec le DPO, cas par cas (atteinte à la santé, oui ; simple conflit de travail, pas nécessairement). |

### 1.2 Demande d'intervention informelle

- Le travailleur peut demander une intervention **informelle** ou **formelle** (art. I.3-9).
- Phase préalable : entretien dans les **dix jours calendrier** du premier contact. À sa demande, le travailleur reçoit un
  document qui atteste de l'entretien (art. I.3-11).
- Intervention informelle (art. I.3-12) : entretiens, intervention auprès d'une autre personne de l'entreprise (par
  exemple la ligne hiérarchique), conciliation avec l'accord des personnes. Le type d'intervention est acté dans un
  document daté et signé, dont **le demandeur** reçoit une copie.
- **L'employeur ne reçoit aucun document.** Il ne reçoit qu'une fois par an, **sous forme collective et anonyme**, les
  conclusions des incidents répétés traités de manière informelle (art. I.3-6, § 2, dernier alinéa).

### 1.3 Demande d'intervention formelle : tronc commun

- Définition (art. I.3-13) : le travailleur demande à l'employeur de prendre des mesures, sur la base de l'analyse et de
  l'**avis** du CPAP (contenu fixé par l'art. I.3-24).
- Entretien personnel obligatoire avant la demande, dans les dix jours calendrier, attesté par un document remis au
  travailleur (art. I.3-14, § 2).
- Document daté et signé par le demandeur, accusé de réception par copie signée, refus ou acceptation notifiés dans les
  dix jours calendrier, acceptation réputée à défaut (art. I.3-15).
- Orientation : traitement collectif (sous-section 3) ou individuel (sous-section 4) selon la nature principale des
  risques (art. I.3-16).

### 1.4 Demande à caractère principalement collectif

- Le CPAP informe **l'employeur** par écrit (art. I.3-17, 1°) :
  - de l'introduction d'une demande à caractère principalement collectif et de son traitement à ce titre ;
  - de la situation à risque décrite, **sans transmettre l'identité du demandeur** ;
  - de la date à laquelle l'employeur doit rendre sa décision.
- L'employeur communique ce document au Comité ou à la délégation syndicale (art. I.3-18, § 2, 1°). Ce document doit donc
  pouvoir circuler dans l'entreprise sans rien révéler du demandeur.
- L'employeur rend une décision motivée dans les trois mois, prolongeables de trois mois s'il réalise une analyse de
  risques (art. I.3-19). Il la transmet au CPAP, qui en informe le demandeur.
- Le CPAP peut communiquer à l'employeur des **propositions de mesures**, éventuellement conservatoires, avant
  l'expiration de ce délai (art. I.3-20).
- Bascule en traitement individuel (art. I.3-21) : décision absente, absence de mesures ou mesures inadaptées, avec
  l'accord écrit du demandeur. Le CPAP avertit alors l'employeur et lui **communique l'identité du demandeur**
  (art. I.3-21, § 2).

### 1.5 Demande à caractère principalement individuel

- Le CPAP informe l'employeur par écrit de l'introduction de la demande et lui **communique l'identité du demandeur**
  (art. I.3-22).
- Examen impartial et déclarations datées et signées des personnes entendues (art. I.3-23). Ces déclarations restent dans
  le dossier.
- Avis du CPAP (art. I.3-24) : description de la demande et de son contexte, dangers, facteurs, démarches antérieures,
  propositions de mesures individuelles et collectives, avec leurs justifications.
- Remise de l'avis à **l'employeur** dans les trois mois de l'acceptation (art. I.3-25). La remise à la personne de
  confiance exige l'accord du demandeur. Une prolongation de trois mois est possible, motivée par écrit auprès de
  l'employeur, du demandeur et de l'autre personne directement impliquée.
- Le demandeur et l'autre personne directement impliquée reçoivent la date de remise et les propositions de mesures
  (art. I.3-26). Ils ne reçoivent pas l'avis lui-même de la part du SEPP.
- Le CPAP du service externe communique au **conseiller en prévention chargé de la direction du service interne** les
  propositions de mesures et leurs justifications (art. I.3-27, avec l'art. I.3-24, 5° et 6°).
- Décision motivée de l'employeur dans les deux mois de l'avis, adressée au CPAP, aux parties et au conseiller en
  prévention chargé de la direction du service interne (art. I.3-29). Les mesures individuelles sont annoncées au
  travailleur concerné, et l'avis lui est remis si elles modifient ses conditions de travail (art. I.3-28). Ces
  communications sont faites par l'employeur, pas par le SEPP.

### 1.6 Demande pour faits de violence ou de harcèlement moral ou sexuel

- Le document signé décrit les faits, leur moment et leur lieu, et l'identité de la personne mise en cause
  (art. I.3-35, § 1er). Il ne quitte pas le dossier.
- En plus de l'information de l'art. I.3-22, le CPAP informe l'employeur que le demandeur bénéficie de la **protection
  contre les représailles** (art. 32tredecies de la loi) dès la réception de la demande (art. I.3-36).
- Le CPAP avise immédiatement l'employeur qu'un **témoin** protégé bénéficie de la même protection, et lui en transmet
  l'identité (art. I.3-37, 3°).
- Si la gravité des faits le requiert, le CPAP propose à l'employeur des **mesures conservatoires** avant son avis.
  L'employeur répond par une décision motivée écrite, aussi vite que possible (art. I.3-38).
- L'avis peut être remis, sur demande écrite et avec l'accord écrit du travailleur, au Centre interfédéral pour l'égalité
  des chances et à l'Institut pour l'égalité des femmes et des hommes (art. I.3-39). Le CPAP saisit l'inspection dans les
  cas de l'art. 32septies, § 2, de la loi (art. I.3-40).
- L'employeur remet une copie de l'avis au demandeur ou à la personne mise en cause qui envisage une action en justice
  (art. I.3-42).
- **Identité de la personne mise en cause** : le Code ne prévoit pas de la communiquer à l'employeur comme une
  information autonome. Elle figure en pratique dans l'avis (description de la demande, art. I.3-24, 1°) et, le cas
  échéant, dans les propositions de mesures conservatoires (art. I.3-38). **À vérifier** avec le juriste : peut-elle être
  communiquée avant l'avis en dehors d'une proposition de mesure conservatoire ?

### 1.7 Dossier individuel et secret professionnel

- Le CPAP tient un dossier individuel dont le contenu est fixé (art. I.3-31 ; compléments pour les faits de violence ou de
  harcèlement à l'art. I.3-41). Ses **notes personnelles** ne peuvent pas y figurer (art. I.3-31, dernier alinéa).
- Le dossier est confié à la garde d'un CPAP pendant **vingt ans** à partir de l'introduction de la demande. Il relève de
  sa **responsabilité exclusive**. Il peut être électronique si l'authenticité et l'accès sont protégés (art. I.3-32).
- Il est tenu à la disposition du fonctionnaire chargé de la surveillance. Les déclarations ne vont au ministère public
  qu'avec le consentement de la personne entendue (art. I.3-33).
- Changement de service externe : transfert du dossier de CPAP à CPAP, dans des conditions qui sauvegardent le secret
  professionnel (art. I.3-34). L'employeur ne fournit que les coordonnées du nouveau service.
- Le CPAP et la personne de confiance sont tenus au **secret professionnel** de l'article 458 du Code pénal (loi du
  4 août 1996, article **à vérifier**). Les seules communications permises sont celles que le Code prévoit, aux
  destinataires qu'il désigne.

### 1.8 Analyses de risques, registre et rapports

- Analyse d'une situation de travail spécifique (art. I.3-4) : le CPAP associé ne transmet à l'employeur **que des
  données anonymes**. Les résultats communiqués par l'employeur ne contiennent que des données anonymes (art. I.3-5).
  C'est la base des restitutions PSY-10 à PSY-12.
- Registre des faits de tiers (art. I.3-3) : il est tenu par la personne de confiance ou le CPAP. Si le CPAP relève
  d'un service externe et qu'aucune personne de confiance n'est désignée, il est tenu par le conseiller en prévention
  chargé de la direction du service interne. Les déclarations ne
  comprennent l'identité du travailleur que s'il l'accepte. Seuls l'employeur, le CPAP, la personne de confiance et le
  conseiller en prévention chargé de la direction du service interne y ont accès. L'employeur conserve les déclarations
  **cinq ans**.
- Données anonymes destinées au rapport annuel du service interne, partie VIII (art. I.3-65) : elles sont transmises au
  conseiller en prévention chargé de la direction du service interne.
- Le CPMT transmet chaque année des données collectives et anonymes à l'employeur et au CPAP (art. I.3-44). Il ne reçoit
  l'avis du CPAP qu'avec l'accord du travailleur (art. I.3-47). Ce flux est hors du périmètre de l'employeur.

## 2. Ce que l'employeur voit, ce qu'il ne voit jamais, et quand

Destinataires : **E** = employeur (représentant désigné de l'affilié) ; **S** = conseiller en prévention chargé de la
direction du service interne (rôle `sipp` dans veilla) ; **D** = demandeur ; **P** = autre personne directement
impliquée (mise en cause).

### 2.1 Documents publiables à l'employeur ou au SIPP

| # | Étape | Document | Destinataire | Moment | Identités contenues | Base légale |
|---|---|---|---|---|---|---|
| E1 | Informel, bilan annuel | Conclusions des incidents répétés traités de manière informelle | E | Au moins une fois par an | Aucune (collectif et anonyme) | Art. I.3-6, § 2 |
| E2 | Formelle collective | Information de la demande : fait, traitement collectif, situation à risque, date de décision | E | Dans les meilleurs délais après acceptation | **Aucune** (identité du demandeur exclue) | Art. I.3-17, 1° |
| E3 | Formelle collective | Propositions de mesures de prévention, éventuellement conservatoires | E | Avant l'expiration du délai de trois mois | Selon les mesures (**à vérifier**) | Art. I.3-20 |
| E4 | Formelle collective, bascule | Avertissement du traitement individuel et identité du demandeur | E | Dès l'accord écrit du demandeur | Demandeur | Art. I.3-21, § 2 |
| E5 | Formelle individuelle | Information de la demande et identité du demandeur | E | Après acceptation (**moment à vérifier** : le texte ne le précise pas) | Demandeur | Art. I.3-22 |
| E6 | Formelle individuelle | Prolongation du délai d'avis, avec ses motifs | E (et D, P hors portail employeur) | Avant l'expiration des trois mois | Selon les motifs | Art. I.3-25, al. 2 |
| E7 | Formelle individuelle | **Avis** du CPAP | E | Dans les trois mois (+ trois) de l'acceptation | Parties, de fait | Art. I.3-24, I.3-25, 1° |
| E8 | Formelle individuelle | Propositions de mesures et justifications (art. I.3-24, 5° et 6°) | **S** | En même temps que l'information de D et P | Selon les mesures | Art. I.3-27 |
| E9 | Violence ou harcèlement | Information de la protection du demandeur contre les représailles | E | Dès l'acceptation | Demandeur | Art. I.3-36 |
| E10 | Violence ou harcèlement | Information de la protection d'un témoin, avec son identité | E | Immédiatement | Témoin | Art. I.3-37, 3° |
| E11 | Violence ou harcèlement | Propositions de mesures conservatoires | E | Avant l'avis, si la gravité le requiert | Parties visées par les mesures (**à vérifier**) | Art. I.3-38, al. 1er |
| E12 | Analyse d'une situation spécifique ou générale | Rapport de restitution, uniquement des données anonymes (seuil PSY-11) | E | À la clôture de l'analyse | Aucune | Art. I.3-4, al. 5 ; I.3-5 |
| E13 | Rapport annuel du service interne | Données anonymes pour la partie VIII | **S** | Annuel | Aucune | Art. I.3-65 |

### 2.2 Documents déposés par l'employeur vers le CPAP

| # | Document | Délai | Base légale |
|---|---|---|---|
| R1 | Décision motivée sur les suites de la demande collective | Trois mois (+ trois) après E2 | Art. I.3-19 |
| R2 | Décision motivée sur les suites de la demande individuelle | Deux mois après E7 | Art. I.3-29 |
| R3 | Décision motivée sur les mesures conservatoires | Aussi vite que possible après E11 | Art. I.3-38, al. 2 |
| R4 | Coordonnées du nouveau service externe, lors d'un changement de SEPP | À la demande du CPAP | Art. I.3-34, 1°, b |

Les autres destinataires de ces décisions (demandeur, Comité, SIPP) sont informés par l'employeur, hors de veilla.

### 2.3 Ce que l'employeur ne voit jamais

| Élément | Raison | Base légale |
|---|---|---|
| Le dossier individuel, en tout ou en partie, hors des documents de la section 2.1 | Garde et responsabilité exclusives du CPAP ; secret professionnel | Art. I.3-32 ; art. 458 du Code pénal |
| L'existence d'un premier contact ou d'une intervention informelle individuelle | Aucun document ne lui est destiné | Art. I.3-11, I.3-12 |
| L'attestation d'entretien, la demande signée, l'accusé de réception, le refus ou l'acceptation | Destinés au demandeur | Art. I.3-11, I.3-14, I.3-15, I.3-35 |
| L'identité du demandeur d'une demande collective, sauf bascule E4 | Exclue expressément | Art. I.3-17, 1°, c |
| L'avis d'une demande collective, sauf bascule E4 | Le collectif ne prévoit pas d'avis | Art. I.3-17 à I.3-20 |
| Les déclarations des personnes entendues | Réservées au dossier, à l'inspection et, avec consentement, au ministère public | Art. I.3-23, I.3-31, 8°, I.3-33 |
| Les notes personnelles du CPAP | Exclues du dossier lui-même | Art. I.3-31, dernier alinéa |
| Les informations destinées au demandeur et à l'autre personne (date de remise, propositions) | Destinataires désignés | Art. I.3-26 |
| La saisine de l'inspection et la remise de l'avis aux institutions pour l'égalité | Autres destinataires | Art. I.3-39, I.3-40 |
| Toute réponse individuelle à un questionnaire, tout résultat sous le seuil d'anonymat | Données anonymes uniquement | Art. I.3-4, al. 5 ; PSY-11 |
| Le journal d'accès psychosocial | Réservé au CPAP dirigeant et au DPO | PSY-20 ; `audit:zone-psychosociale` |
| Toute donnée médicale | Zone médicale | ARC-04, ARC-06 |

## 3. Mise en œuvre proposée dans veilla

### 3.1 Modèle : la publication

Le service Psychosocial (zone psychosociale, ADR 0016) porte un agrégat **`Publication`** distinct du `DossierPsy`.

- Une publication associe : un exemplaire de document figé, un **type de document publiable** (E1 à E13), un
  destinataire (`Employeur` ou `ConseillerDirectionServiceInterne`), l'affilié, une **référence de procédure** opaque,
  l'auteur, la date de publication et la date de première consultation.
- La **référence de procédure** est un identifiant aléatoire propre aux publications. Elle permet à l'employeur de
  regrouper les documents d'une même demande, sans jamais exposer l'identifiant du dossier.
- Le domaine fixe une **liste fermée** des types publiables et, pour chacun, le destinataire autorisé et la nature de la
  demande compatible (collective, individuelle, violence ou harcèlement). Exemples de règles du domaine :
  - E7 (avis) est refusé pour une demande collective sans bascule E4 ;
  - E8 n'est publiable qu'au SIPP ;
  - aucun type ne correspond aux déclarations, à la demande signée ou aux notes.
  Ces règles sont structurelles (elles traduisent le Code) et testées. Les **délais**, eux, viennent de Référentiels :
  aucune durée écrite en dur.
- L'exemplaire publié est généré par `documents-psychosociale` comme un exemplaire distinct de l'exemplaire du dossier.
  C'est le modèle des exemplaires de l'ADR 0009, avec une clé d'idempotence `publication:<id>:<destinataire>`.
  L'employeur ne lit que cet exemplaire.
- **Action explicite** : seul le CPAP en charge du dossier, ou son remplaçant désigné, publie. Le service vérifie cette
  relation, comme la relation de soin côté médical. L'écran présente le destinataire, l'affilié et un aperçu, puis demande
  une confirmation. Il n'y a ni publication automatique ni publication par lot.
- **Pas de contrôle à quatre yeux** : il imposerait qu'un second CPAP lise le dossier, contre la responsabilité exclusive
  de l'art. I.3-32.
- Dépôt de l'employeur (R1 à R3) : il est rattaché à la référence de procédure. Il alimente les minuteries de la saga de
  procédure formelle (PSY-05, ADR 0016 point 4).

### 3.2 Droits à créer (format de `Security.cs`)

| Constante | Valeur | Rôles | Origine (`matrice-droits.csv`) | Portée |
|---|---|---|---|---|
| `DossierPsyPublierEmployeur` | `dossier-psy:publier-employeur` | `cpap`, `cpap-dirigeant` | `3.3` | Publier un exemplaire au destinataire autorisé, pour un dossier dont on a la charge |
| `DossierPsyPublicationRetirer` | `dossier-psy:publication-retirer` | `cpap-dirigeant` | `hors-3.3` | Retirer une publication erronée, avec motif (option O6) |
| `DossierPsyPublicationsLire` | `dossier-psy:publications-lire` | `employeur`, `sipp` | `3.3` | Lister et lire les publications de son affilié qui lui sont destinées. **Aucun accès au dossier** |
| `DossierPsyDecisionEmployeurDeposer` | `dossier-psy:decision-employeur-deposer` | `employeur` | `3.3` | Déposer R1 à R4 sur une référence de procédure de son affilié |
| `RegistreFaitsTiersLire` | `registre-faits-tiers:lire` | `employeur`, `sipp`, `cpap`, `cpap-dirigeant` | `hors-3.3` | PSY-21, option O9 |
| `RegistreFaitsTiersEcrire` | `registre-faits-tiers:ecrire` | `cpap`, `cpap-dirigeant` | `hors-3.3` | PSY-21, option O9 |

Remarques :

- Le préfixe `dossier-psy:` déclenche **automatiquement** l'authentification forte (`AuthenticationStrength.IsRequiredFor`,
  NF-01). Pour l'employeur et le SIPP, la fédération CSAM devra donc fournir un niveau élevé (eID ou itsme). C'est voulu :
  E4, E5, E9 et E10 portent des identités. Les permissions `registre-faits-tiers:*` n'ont pas ce préfixe. Il faut ajouter
  `RequireAuthenticationStrength()` sur leurs routes, ou étendre `PrefixesSensibles`.
- Le commentaire de `Permissions` sur Documents (« les documents des zones médicale et psychosociale exigent en plus
  `dossier-sante:*` ou `dossier-psy:*` ») devra devenir une **liste fermée** :
  - `documents-psychosociale` n'accepte `dossier-psy:publications-lire` que pour un exemplaire `employeur` ou `sipp` de
    l'affilié du jeton ;
  - il n'accepte jamais `dossier-psy:lire` pour un employeur.
- Toute création suit la procédure de `revue-des-droits.md` : `Security.cs`, `matrice-droits.csv` et `MatriceDroitsTests`
  dans la même demande de fusion. L'écart #401 point 3 est alors clos.
- `cpap` et `cpap-dirigeant` n'ont pas besoin d'un droit de lecture des publications : ils les voient depuis le dossier,
  avec `dossier-psy:lire`.

### 3.3 Filtrage par affilié et par destinataire

Ces trois filtres s'appliquent **dans le service Psychosocial**. Le BFF ne filtre rien.

1. **Affilié** : `Publication.AffilieId` doit être égal au claim `affilie_id` du jeton. Sans ce claim, le service répond
   403. Le service répond 404, et non 403, pour une publication d'un autre affilié, afin de ne pas révéler son existence.
2. **Destinataire** : le rôle `employeur` ne voit que les publications `Employeur`, le rôle `sipp` que les publications
   `ConseillerDirectionServiceInterne`. Un utilisateur qui porte les deux rôles voit les deux.
3. **Contact désigné** (option O2, recommandée) : en plus du rôle, l'utilisateur (claim `sub`) doit figurer dans la liste
   des destinataires psychosociaux de l'affilié. L'affilié désigne ces contacts par écrit et le CPAP les enregistre. Au
   moment de publier, le CPAP peut exclure un contact désigné, par exemple s'il est partie à la demande.
4. L'affilié publié est **toujours** celui du dossier (`dossier_psy.affilie_id`). Le cas de l'entreprise extérieure
   (art. I.3-43) reste une question ouverte (Q5).

### 3.4 Journalisation (NF-04, PSY-20)

| Événement | Action `IAuditTrail` | Objet | Moment |
|---|---|---|---|
| Publication par le CPAP | `Creation` | `publication-psy` | Même transaction que la publication (outbox) |
| Lecture de la liste ou d'un document par l'employeur ou le SIPP | `Lecture` (`EnregistrerLectureAsync`) | `publication-psy` | **Avant** la restitution ; si la trace échoue, la lecture échoue |
| Dépôt d'une décision par l'employeur | `Creation` | `depot-employeur-psy` | Même transaction |
| Retrait d'une publication | `Suppression`, motif obligatoire | `publication-psy` | Même transaction |

- Les traces ne contiennent que des identifiants (utilisateur, publication), le rôle, l'action et un éventuel motif :
  aucune identité de partie, aucun type de demande (ARC-06).
- L'employeur lit un document qui lui est adressé. Il n'accède pas au dossier « hors relation de soin » au sens du §3.3 :
  **aucun motif ne lui est demandé**, et aucun bris de glace ne lui est ouvert.
- **Emplacement des traces (ADR 0016, point 4)** : je recommande une **instance d'Audit de zone** `audit-psychosociale`,
  sur le modèle `instance_de` de l'ADR 0009 (option O4).
  - Elle reprend le journal chaîné et infalsifiable existant, et la consultation par permission de zone : le CPAP
    dirigeant et le DPO ont déjà `audit:zone-psychosociale`.
  - Elle demande une **rubrique dédiée** `audit-psychosociale`, que le service Audit standard ne reçoit jamais. Une
    instance déclarée avec `instance_de = "audit"` publierait et s'abonnerait par défaut à la rubrique `audit` partagée
    (`infra/main.tf`) : c'est à adapter (`cloud-architect`).
  - Le bris de glace psychosocial (`audit.bris-de-glace-signale.v1` vers Communications, zone standard) doit lui aussi
    rester dans la zone, ou ne porter aucun `ObjetId`.
- Le CPAP voit dans le dossier la **date de première consultation** de chaque publication. C'est une donnée fonctionnelle,
  utile comme preuve de réception (option O1) : elle n'est pas lue dans le journal d'audit.

### 3.5 Respect de la zone psychosociale (ARC-04, ARC-06)

- **Stockage** : les publications et leurs exemplaires restent dans la zone psychosociale : base Psychosocial, stockage
  WORM et clé racine HSM de `documents-psychosociale` (ADR 0009, ADR 0012). Rien n'est copié en zone standard (option O3).
- **Lecture** : le BFF employeur appelle l'API de Psychosocial, puis de `documents-psychosociale` (flux plateforme →
  zone, déjà permis : ADR 0009 point 5, état des lieux Z5). Le BFF relaie le contenu en flux, sans cache et sans journal
  du contenu.
  - C'est la lecture déjà admise pour l'exemplaire travailleur en zone médicale : le contenu ne sort de la zone que vers
    son destinataire légal, par son canal authentifié.
  - Cette lecture d'ARC-06 (« aucune donnée psychosociale ne quitte sa zone dans une API ») est à confirmer par le
    responsable produit (O3).
- **Surface exposée** : Psychosocial expose un groupe de routes `/employeur/...` limité à la liste, à la lecture et au
  dépôt. Le BFF employeur ne connaît que ces routes. Le rôle `employeur` est refusé sur toute autre route du service. Il
  faut aussi resserrer les adresses données au BFF (état des lieux Z5, `cloud-architect`).
- **Notification** : Psychosocial publie un événement minimal, par exemple `psychosocial.publication-disponible.v1`, avec
  `PublicationId`, `AffilieId`, `Destinataire` et l'horodatage. Il ne porte **ni `PersonneId`, ni type de document, ni
  type de demande**.
  - Communications (zone standard) envoie un message générique (« un document confidentiel vous a été adressé par votre
    SEPP »), sans le mot « psychosocial » ni aucun nom (option O5).
  - Le contrat est à ajouter au catalogue `docs/architecture/evenements.md`, et l'abonnement à Terraform.
  - L'événement révèle à la zone standard qu'un affilié reçoit un document psychosocial. Cette information est connue au
    niveau de l'affilié, pas de la personne. Elle est acceptable, car Prestations reçoit déjà les clôtures psychosociales
    (ADR 0016).
- **Durée de mise à disposition** sur le portail : elle est paramétrée dans Référentiels (option O8). Ce n'est pas la
  durée de garde du dossier (vingt ans, art. I.3-32), qui reste du ressort du CPAP (PSY-22).

### 3.6 Cohérence avec l'ADR 0016

| Point de l'ADR 0016 | Effet de cette procédure |
|---|---|
| 1. Psychosocial en zone psychosociale | Publications, exemplaires et dépôts y sont hébergés. |
| 2. Demande venue d'un portail : appel synchrone au service propriétaire | Le dépôt R1 à R4 suit ce modèle. Le BFF employeur ne publie aucun événement. |
| 3. Seules les mesures collectives anonymes vont au plan d'action | E12 et les mesures collectives d'E7 peuvent alimenter `MesurePreventionCreee`. Une mesure individuelle (E3, E11) n'y va jamais. |
| 4. Saga de la procédure formelle | Les publications E2, E5, E7, E11 et les dépôts R1 à R3 sont des jalons de la saga. Leurs minuteries viennent des délais de Référentiels. |
| 4. `documents-psychosociale` | Il est requis dès la première publication. |
| 4. Journal d'accès dans la zone | Recommandation : instance `audit-psychosociale` (§3.4). Elle tranche la variante laissée à l'architecte sécurité, sous réserve de validation. |

## 4. Options soumises au responsable produit

| # | Choix | Options | Recommandation |
|---|---|---|---|
| O1 | Valeur de la publication sur le portail | (a) Canal officiel unique de la communication écrite. (b) Canal complémentaire : l'envoi officiel reste le courrier, recommandé ou électronique (e-Box entreprise). (c) Notification seule, document envoyé par courrier. | **(b)** jusqu'à l'avis du juriste sur la valeur d'un dépôt électronique pour faire courir les délais (art. I.3-19, I.3-28, I.3-29). Le modèle permet de passer à (a) sans refonte, avec la première consultation comme preuve. |
| O2 | Qui, chez l'affilié, voit les publications | (a) Tout utilisateur portant le rôle `employeur` ou `sipp`. (b) Rôle plus **contacts désignés** par l'affilié, enregistrés par le CPAP. (c) Rôle CSAM dédié (mandat spécifique). | **(b)**. (a) expose des identités à tout le personnel administratif de l'affilié. (c) dépend des mandats CSAM (hypothèse H4 de l'état des lieux). |
| O3 | Emplacement des exemplaires employeur | (a) Zone psychosociale, lus par le BFF en flux. (b) Copie dans Documents standard. | **(a)**. (b) sort de la zone des identités de parties, contre ARC-06. |
| O4 | Journal d'accès psychosocial (ADR 0016, point 4) | (a) Instance `audit-psychosociale` avec une rubrique dédiée. (b) Journal propre au service Psychosocial. | **(a)** : réutilise la chaîne infalsifiable et la consultation par zone, sans second journal à maintenir. |
| O5 | Contenu de la notification | (a) Générique, sans « psychosocial » ni nom. (b) Explicite (type de document). | **(a)** : la boîte de l'affilié peut être lue par des tiers. |
| O6 | Retrait d'une publication | (a) Impossible ; rectification par un nouveau document. (b) Retrait par le CPAP dirigeant, motif obligatoire, trace conservée. | **(b)**, limité à l'erreur de destinataire (violation de données, RGPD art. 33). L'exemplaire reste dans le dossier. |
| O7 | Dépôt des décisions de l'employeur dans le portail | (a) Oui (R1 à R4). (b) Non, courrier uniquement. | **(a)** : il ferme la boucle des délais de la saga (PSY-05). |
| O8 | Durée de mise à disposition sur le portail | Valeur paramétrée dans Référentiels | À fixer avec le DPO. Elle part de la publication et ne se confond pas avec la garde de vingt ans du dossier. |
| O9 | Registre des faits de tiers (PSY-21) dans le portail | (a) Tenu par le CPAP dans Psychosocial, lu par l'employeur et le SIPP. (b) Hors de veilla. | **(a)**, mais voir Q3 : PSY-21 dit « tenu par l'employeur », le Code dit « tenu par la personne de confiance ou le CPAP ». |

## 5. Questions ouvertes

| # | Question | Destinataire |
|---|---|---|
| Q1 | Un dépôt sur le portail vaut-il « communication écrite » et date de réception pour les délais du Code ? Faut-il une clause dans le contrat d'affiliation ? | Juriste, DPO, responsable produit |
| Q2 | La personne de confiance de l'entreprise (art. I.3-25, 2° ; PSY-06) doit-elle devenir un profil du portail ? Ce document ne le prévoit pas : elle reçoit l'avis hors de veilla. | Responsable produit |
| Q3 | Registre des faits de tiers : qui le tient dans veilla ? Le Code (art. I.3-3) contredit la formulation de PSY-21. | Responsable produit, juriste |
| Q4 | Identité de la personne mise en cause : peut-elle être communiquée à l'employeur avant l'avis, hors des mesures conservatoires (§1.6) ? | Juriste |
| Q5 | Entreprise extérieure (art. I.3-43) : faut-il publier à un affilié autre que celui du dossier ? | Juriste, responsable produit |
| Q6 | Après une désaffiliation ou un changement de SEPP (art. I.3-34), l'affilié garde-t-il l'accès à ses anciennes publications ? | Responsable produit, DPO |
| Q7 | Moment exact de l'information de l'art. I.3-22 (après acceptation ?) : le texte ne le précise pas. | Juriste |
| Q8 | Numéro d'article de la loi du 4 août 1996 sur le secret professionnel du CPAP, et vérification du titre 3 sur la version consolidée en vigueur. | Juriste |
| Q9 | Le point 4 de #401 (travailleur, « ses propres documents ») peut-il réutiliser ce mécanisme avec un destinataire `Travailleur` et la permission `dossier-psy:publications-lire-propres` ? | Responsable produit (décision séparée) |

## Hypothèses

- H1 : le SEPP est toujours un **service externe**. Les documents destinés au « conseiller en prévention chargé de la
  direction du service interne » (art. I.3-27, I.3-65) vont donc au rôle `sipp` de l'affilié.
- H2 : un utilisateur du portail employeur n'agit que pour un affilié à la fois (claim `affilie_id` unique), comme pour
  les autres services.
- H3 : l'avis (E7) est publié tel que rédigé par le CPAP. veilla ne le caviarde pas : la responsabilité de son contenu
  appartient au CPAP.
- H4 : la fédération CSAM fournit pour les employeurs un niveau d'authentification suffisant pour satisfaire
  `AuthenticationStrength` (eID ou itsme).

## Suite

Après validation :

1. Rédiger un ADR (prochain numéro libre dans `docs/adr/README.md`) qui fige O1 à O9 et tranche le point 4 de
   l'ADR 0016.
2. Ouvrir les tickets du lot 5 : droits et matrice, agrégat `Publication`, routes employeur, exemplaires de
   `documents-psychosociale`, instance `audit-psychosociale`, notification et écrans du portail employeur.
