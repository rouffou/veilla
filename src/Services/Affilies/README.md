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

## Autorisations (§3.3)

- Lecture (`affilie:lire`) : profils internes ; employeur et SIPP limités à leurs affiliés.
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
