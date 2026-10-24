# Service Affiliés

Propriétaire des exigences AFF-01 à AFF-06 (cahier des charges §4.1, entités §15.3). Base `affilies`, port local 5111.

| Exigence | Réalisation |
|---|---|
| AFF-01 fiche employeur | Agrégat `Affilie` : numéro BCE (format et modulo 97), dénomination, forme juridique, NACE-BEL, commission paritaire, catégorie A–D, dates d'affiliation et de fin, langue, régime linguistique, statut |
| AFF-02 hiérarchie | `Groupe` (facultatif) → affilié → `UniteEtablissement` (numéro UE BCE) → `Site` (adresse, latitude/longitude) → `Departement` (hiérarchie libre `parent_id`) ; clôture au lieu de suppression |
| AFF-03 contacts | `Contact` avec rôle et période de validité ; une modification clôture la ligne et en crée une nouvelle (DAT-04) |
| AFF-04 concertation | `OrganeConcertation` (Comité PPT, délégation syndicale) et `ReunionConcertation` (date, `document_id` de l'ordre du jour, participation du SEPP) |
| AFF-05 historique | Table `modification_affilie` : version, action, auteur (`sub`), horodatage UTC, valeurs avant/après en JSON (parties modifiées seulement), écrite dans la transaction de la modification ; `GET /api/v1/affilies/{id}/historique` |
| AFF-06 fusion, scission, transfert | `OperationAffilie` (projetée, réalisée, annulée ; date d'effet ; absorbant ; bénéficiaires ; SEPP de contrepartie) et événement `affilies.operation-affilie-modifiee.v1`. Le transfert des dossiers de santé relève de la Surveillance médicale (SAN-42), hors de ce service |

## Événements publiés

`affilies.affilie-cree.v1`, `affilies.affilie-modifie.v1` (quand la fiche elle-même change : identité, catégorie, statut…),
`affilies.operation-affilie-modifiee.v1`. Le numéro BCE y figure sous sa forme canonique à dix chiffres.

## Événement consommé : mise à jour depuis la BCE (AFF-01, AFF-02, INT-04)

`integrations.donnees-bce-recues.v1` (abonnement `affilies` ← `integrations`, filtré sur ce sujet dans `infra/variables.tf`)
est traité par `DonneesBceRecuesHandler`, idempotent (inbox ARC-31, et des données identiques ne modifient rien) :

1. l'affilié est retrouvé par le **numéro BCE** de l'événement (pas par son `AffilieId`) ;
2. comme l'événement ne porte que des identifiants (ARC-06), les données sont relues chez Intégrations par
   `GET /api/v1/bce/entreprises/{numeroBce}` avec le compte technique du service (client OIDC `veilla-affilies`, rôle `affilies`,
   permission `integrations:bce-lire`, configuration `Affilies:CompteTechnique` et `Affilies:ServicesInternes:Integrations`) ;
3. la **dénomination, la forme juridique et le code NACE** de la fiche sont ceux de la BCE ; chaque **unité d'établissement** est
   ajoutée (début = date de début BCE, sinon date d'extraction), modifiée (nom, adresse) ou, si la BCE ne la connaît plus, **fermée à la
   date d'extraction** (`Validity`, DAT-04 : jamais supprimée) ; le tout dans l'historique AFF-05 sous l'identité `system` (action
   `affilie.donnees-bce-appliquees`). Le statut d'affiliation, la catégorie, la commission paritaire et les langues ne viennent pas de la
   BCE et ne sont pas touchés ; ni le contrat ni l'API ne portent d'adresse de siège (la fiche n'a d'adresse que par unité).
4. **La BCE fait foi** (décision du responsable produit, #315) : ces valeurs remplacent les saisies manuelles, sans exception ni
   verrou par champ. Quand une valeur diffère à la fois de la BCE et de la dernière valeur reçue de la BCE (colonnes
   `denomination_bce`, `forme_juridique_bce`, `code_nace_bce` ; `null` tant que la BCE n'a rien appliqué), c'est une saisie manuelle : elle est
   remplacée et le gestionnaire en est informé par un écart **informatif** `valeur-manuelle-remplacee` (référence `denomination`,
   `forme-juridique` ou `code-nace`). L'écart ne porte aucune valeur (ni l'ancienne ni la nouvelle ; l'ancienne figure dans l'historique AFF-05) et
   rien n'en est journalisé. Il ne bloque rien, reste ouvert aux réceptions suivantes et n'est clos que par le gestionnaire
   (`informatif: true` dans l'API). Les statut et siège de l'entreprise (point 2 de #315) ne sont pas repris : la fiche n'a ni statut BCE ni
   adresse de siège, et leur format dépend de la confirmation INT-04 (#155).

Ce que le gestionnaire ne sait pas résoudre seul est consigné dans la table `ecart_synchronisation` (un seul écart ouvert par numéro BCE,
code et référence) et **ne fait pas boucler le message** : affilié inconnu, affilié clôturé (absorbé, scindé, transféré), identification ou
unité invalide, unité rattachée à un autre affilié, unité fermée chez l'affilié mais active à la BCE, unité absente de la BCE qui porte des
sites ouverts (fermeture non automatique), liste d'unités vide, données BCE introuvables chez Intégrations. Un écart qui ne se représente plus
à la réception suivante est clos automatiquement.
`GET /api/v1/ecarts-bce?ouverts=true` (liste) et `POST /api/v1/ecarts-bce/{id}/resolution` (marquer traité) sont réservés au gestionnaire
de dossiers. Une **panne technique** de la lecture chez Intégrations (indisponibilité, accès refusé) fait échouer le message pour reprise
par le bus.

## Employeurs du travailleur connecté (POR-11, #340)

`GET /api/v1/mes-employeurs[?date=]` rend, pour la personne du claim `personne_id`, la **dénomination**, la **langue** de
correspondance et le **régime linguistique** des seuls affiliés chez qui elle a une occupation active à la date (aujourd'hui, heure de
Belgique, par défaut) : `[{ affilieId, denomination, langue, regimeLinguistique }]`. Rien d'autre de la fiche (ni BCE, ni adresse, ni contacts).

- Permission dédiée **`affilie:lire-employeurs`**, accordée au seul rôle `travailleur`, qui n'a toujours pas `affilie:lire`
  (le groupe de routes de lecture, `/affilies/{id}`, `/affilies`, reste interdit au travailleur).
- Le service **ne fait pas confiance à l'appelant** : la route n'accepte ni identifiant de personne ni liste d'affiliés. La personne est lue
  uniquement dans le jeton (une valeur unique et valide, sinon 403 `perimetre.interdit`) et les affiliés sont déduits de la **projection
  locale des occupations**, table `occupation_locale`, alimentée par `personnes.occupation-debutee.v1` et `personnes.occupation-terminee.v1`
  (`OccupationDebuteeHandler`, `OccupationTermineeHandler`, inbox idempotente ; les deux événements complètent la même ligne dans n'importe quel
  ordre, une fin plus ancienne que celle déjà reçue est ignorée). Seuls des identifiants et des dates sont conservés (ARC-06). Une occupation
  dont le début n'est pas encore reçu n'est pas active ; le dernier jour d'occupation est inclus (comme DIMONA).
- Abonnement `affilies` ← `personnes`, filtré sur ces deux sujets dans `infra/variables.tf` (`AbonnementsTerraformTests`).

## Autorisations (§3.3)

- Lecture (`affilie:lire`) : profils internes ; employeur et SIPP limités à leurs affiliés.
- Employeurs du travailleur (`affilie:lire-employeurs`) : travailleur, limité aux affiliés de ses occupations actives (voir ci-dessus).
- Écriture (`affilie:ecrire`) : le gestionnaire de dossiers modifie toute la fiche, crée les affiliés et les groupes.
  L'employeur et le SIPP ont une écriture **partielle** : contacts et organes de concertation de leurs affiliés uniquement.

### Claim `affilie_id`

Le périmètre d'un utilisateur externe (rôles `employeur`, `sipp`) est lu dans le jeton d'accès :

- nom : `affilie_id` ;
- valeur : identifiant (UUID) de l'affilié représenté, tel que renvoyé par ce service ;
- un claim par affilié (claim répété ou tableau JSON dans le jeton) ; les valeurs qui ne sont pas des UUID sont ignorées ;
- sans ce claim, un employeur ou un SIPP n'accède à aucun affilié ;
- les profils internes n'en ont pas besoin.

En local (Keycloak), le claim provient d'un attribut utilisateur multivalué `affilie_id` exposé par un mappeur
« User Attribute » dans le jeton d'accès. En production, il est émis à partir du mandat de l'utilisateur externe (ADR 0005).
