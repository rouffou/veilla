# Service Réintégration (zone médicale)

Notifications d'incapacité, estimation du potentiel de travail, trajet formel de réintégration, absences aux rendez-vous,
échanges eHealth et délais légaux (cahier des charges §2.1, §3.3, §5.8 SAN-60 à SAN-65, §15.3, §15.4). Base `reintegration`,
port local 5121 (compose), espace de noms `Sepp.Reintegration`. Zone de sensibilité **médicale** (ARC-04) : base, clés de
chiffrement et droits propres, sur le modèle exact du service Surveillance médicale.

## Secret médical (§2.1, §3.3, NF-04)

Point de passage unique : `Application/Commun.cs` → `GardeReintegration`.

1. **Matrice des droits** : le dossier de réintégration (incapacité, estimations, trajets, absences, échanges eHealth, tableau
   de bord) n'est accessible qu'avec `reintegration:lire` / `reintegration:ecrire` (CPMT, CPMT dirigeant, infirmier). Le
   conseiller en prévention, le gestionnaire, l'employeur, le CPAP et l'assistant médical reçoivent `403`. L'évaluation de
   réintégration et son formulaire exigent `reintegration:evaluer` (CPMT, CPMT dirigeant).
2. **Relation de soin** : l'utilisateur est le gestionnaire du dossier (saisie manuelle, ou prise en charge par
   `POST /incapacites/{id}/prise-en-charge`), l'auteur d'une estimation du potentiel de travail ou l'évaluateur d'un trajet de
   l'incapacité. Une incapacité reçue par le flux eHealth n'a pas de gestionnaire : la première prise en charge demande un motif.
3. **Hors relation de soin**, motif obligatoire (en-tête `X-Motif-Acces`, encodé en pourcentage, 300 caractères au plus,
   jamais dans l'URL) ; sinon `403 reintegration.motif-obligatoire`.
4. **Bris de glace** : `X-Bris-De-Glace: true` + motif (`400` sinon) ; trace `brisDeGlace = true`, alerte au CPMT dirigeant
   par le service Audit.
5. **Journalisation de chaque accès** par `IAuditTrail` (zone `medicale`, service `reintegration`) : type d'objet
   `reintegration.<partie>` (incapacite, estimation, trajet, absences, ehealth, recherche, tableau-de-bord, suivi) et identifiant
   **de l'incapacité**, pour que le DPO retrouve tous les accès à un dossier. Lecture tracée et validée avant d'être servie ;
   écriture tracée dans sa transaction (ARC-32).
6. **Externes** : suivi du trajet sans donnée médicale (statuts, échéances, conclusion du formulaire) avec
   `reintegration-suivi:lire` (employeur et SIPP limités à leur affilié par le claim `affilie_id`, gestionnaire) ; dépôt du plan
   ou du rapport motivé avec `reintegration-plan:deposer` ; réponse du travailleur au plan avec
   `reintegration-reponse:enregistrer` (claim `personne_id`). Chaque accès est tracé en zone médicale.

## Chiffrement applicatif (ARC-45)

`FieldEncryptor` (AES-256-GCM, format `v2`), clés propres au service dans la zone médicale, section
`ZoneMedicale:Encryption` : clé de données courante (`CurrentKeyId`) et clés de données enveloppées (`Deks:<id>`) par la
clé racine RSA-HSM `reintegration-fields-kek` (`Enveloppe:Type`, `Enveloppe:CleKek`), déballées au démarrage ; anciennes
clés du format `v1` en lecture seule (`Keys:<id>`). Voir ADR 0012. Colonnes chiffrées : `estimation_potentiel.commentaire_chiffre`, `trajet.note_evaluation_chiffre`,
`echange_ehealth.contenu_chiffre`. Restent en clair : identifiants, dates, statuts et codes. Aucune donnée médicale dans les
événements, les journaux (le service d'arrière-plan ne journalise que des compteurs et des types d'erreur) ni les erreurs
(ARC-06) ; vérifié par les tests d'intégration (SQL brut, outbox, journaux capturés).

## Schéma (#272, §15.3)

`incapacite`, `estimation_potentiel`, `trajet`, `etape_trajet`, `absence_rdv`, `echange_ehealth`, projections
`parametre_legal_local`, `calendrier_local`, `obligation_reintegration`, outbox et inbox. UUID v7 (DAT-01), références entre
services par identifiant (DAT-02), colonnes d'audit et verrou optimiste (DAT-03), suppression logique des racines (DAT-05),
migration `Initial` versionnée (DAT-09).

## Exigences couvertes

| Exigence | Réalisation |
|---|---|
| SAN-60 | Consommation de `integrations.incapacite-notifiee` (publié par le flux eHealth d'Intégrations, idempotent, prolongation = même `IncapaciteId`) ; saisie manuelle `POST /incapacites`, publiée par `reintegration.incapacite-saisie` (#348) ; échéance « information du travailleur » (`REINTEGRATION.INFORMATION_TRAVAILLEUR`) avec alerte et enregistrement de l'information (`POST /incapacites/{id}/information-travailleur`) ; fin d'incapacité à la reprise (`obligations.reprise-enregistree`) |
| SAN-61 | `POST /incapacites/{id}/estimations` (CPMT ou infirmier) : date, résultat, méthode standardisée (code), commentaire chiffré ; échéance `REINTEGRATION.ESTIMATION_POTENTIEL` |
| SAN-62 | Saga persistante `Trajet` (ADR 0008) : demande → invitation recommandée → évaluation → formulaire → plan ou rapport motivé → acceptation ou refus (`REINTEGRATION.REPONSE_PLAN.DELAI`) → fin ; date limite d'invitation recopiée (`REINTEGRATION.INVITATION.DELAI`) ; publie `reintegration.trajet-demarre` / `trajet-termine` (Obligations ouvre puis clôt l'évaluation de réintégration) ; compensation par annulation (trajet, incapacité annulée, envois eHealth non effectués) |
| SAN-63 | Compteur d'absences par trajet (`absence_rdv`) : saisie ou `planification.absence-rendez-vous-constatee` sur une obligation de réintégration (projection de `obligations.obligation-creee`), idempotent par rendez-vous ; au seuil `REINTEGRATION.ABSENCES_MEDECIN_CONSEIL`, information du médecin-conseil par eHealth (une fois par trajet) |
| SAN-64 | Port `ICanalEHealth`, `CanalEHealthSimule` (déterministe), squelette `CanalEHealthReel` (INT-04) ; échanges sortants (médecin traitant avec accord du travailleur, médecin-conseil, coordinateur Retour au travail) envoyés par le service d'arrière-plan avec reprises puis abandon et relance manuelle ; réception des réponses rattachées à l'envoi |
| SAN-65 | `GET /tableau-de-bord/delais` : échéances des dossiers et trajets ouverts, statut `AVenir` / `Proche` (alerte N jours ouvrables avant) / `Depassee`, alertes émises par les minuteries |
| ADR 0008 | Minuteries en base (`prochaine_echeance`, `prochaine_minuterie`, `minuteries_declenchees`) traitées par `TraitementReintegrationService` (`FOR UPDATE SKIP LOCKED`, `TimeProvider`, jours ouvrables belges avec les jours fériés de `referentiels.jours-feries-modifies`) |

## Paramètres légaux et réglages

- Délais légaux : paramètres `REINTEGRATION.*` reçus par `referentiels.parametre-legal-modifie` (ARC-21). À défaut,
  `Reintegration:DelaisParDefaut` (mêmes valeurs que le catalogue de Référentiels et le §2.1 : 4 semaines, 8 semaines, 6 mois,
  20 travailleurs, 2 absences, 49 et 14 jours calendrier) — **à valider** par le département juridique.
- Réglages d'organisation (`Reintegration:Suivi`, **à valider**) : alerte 5 jours ouvrables avant échéance, délai du plan de
  l'employeur non suivi (non fixé par le cahier), 5 tentatives d'envoi eHealth.
- `Reintegration:Adaptateurs:EHealth` = `Simulateur` | `Reel` (obligatoire) ; `Reintegration:Traitement` (`Actif`, `Intervalle`).

## Événements

- **Publiés** : `reintegration.trajet-demarre.v1`, `reintegration.trajet-termine.v1`, `reintegration.incapacite-saisie.v1`
  (codes `CodesReintegration`), traces `audit.acces-donnee-sensible.v1`. `incapacite-saisie` est publiée à la saisie manuelle
  d'une incapacité (identifiants, dates, `Source` = `SAISIE_MANUELLE`, aucune donnée médicale) pour qu'Obligations ouvre
  l'estimation due ; Obligations la dédoublonne avec la même incapacité reçue par eHealth (personne, affilié, date de début ;
  voir `docs/architecture/evenements.md`).
- **Consommés** : `integrations.incapacite-notifiee`, `obligations.obligation-creee` (types de réintégration),
  `obligations.reprise-enregistree`, `planification.absence-rendez-vous-constatee`, `referentiels.parametre-legal-modifie`,
  `referentiels.jours-feries-modifies` (abonnements et filtres de sujets dans `infra/variables.tf`).

## À valider et suites

- Codes des résultats d'estimation et de la conclusion du formulaire (réglementation 2026) : provisoires.
- Information du travailleur : l'échéance et l'alerte sont gérées, l'envoi du courrier lui-même (Documents, Communications)
  n'est pas automatisé.
- Une incapacité saisie à la main puis reçue aussi par eHealth (ou l'inverse) donne deux dossiers dans Réintégration : seul
  Obligations les dédoublonne. La fin et l'annulation d'une incapacité saisie ne sont pas publiées (l'estimation due reste
  ouverte dans Obligations, comme pour une notification eHealth erronée).
- Effectif de l'employeur (trajet obligatoire) saisi sur le dossier : aucune source automatique.
- Exigence d'accord du travailleur pour le médecin-conseil et le coordinateur ; réouverture d'une incapacité après annulation de
  la reprise ; poursuite du trajet après la reprise du travail.
- Adaptateur eHealth réel (eHealthBox, certificat, délibération CSI) hors périmètre.
