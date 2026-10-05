# Service Surveillance médicale (zone médicale)

Dossier de santé, consultation, décisions d'évaluation de santé, vaccinations et maladies professionnelles
(cahier des charges §2.1, §3.3, §5.4 à §5.7, §5.9, §14.5, §15.2-15.3). Base `surveillance_medicale`, port local 5118
(compose), espace de noms `Sepp.SurveillanceMedicale`. Zone de sensibilité **médicale** (ARC-04) : base, clés de
chiffrement et droits propres.

## Secret médical (§2.1, §3.3, NF-04)

Point de passage unique : `Application/Commun.cs` → `GardeDossier`.

1. **Matrice des droits** : le contenu du dossier n'est accessible qu'avec `dossier-sante:lire` / `dossier-sante:ecrire`
   (CPMT, CPMT dirigeant, infirmier). Un conseiller en prévention, le gestionnaire, l'employeur, le CPAP, l'assistant
   médical reçoivent `403` sur tout le dossier. Les décisions (`decision:ecrire`) sont rédigées et signées par le CPMT.
2. **Relation de soin** (définition retenue, ARC-41) : l'utilisateur est en relation de soin avec la personne s'il est
   - le **CPMT gestionnaire** du dossier (celui qui l'ouvre, ou désigné par `PUT /dossiers/{id}/gestionnaire`) ;
   - le **professionnel d'un examen** (en cours ou clôturé) du dossier ;
   - le **vaccinateur** d'une dose enregistrée dans le dossier.
   L'ouverture d'un examen sur un **rendez-vous planifié** pour la personne (projection de `RendezVousPlanifie`) établit
   la relation sans motif : la convocation vaut prise en charge.
3. **Hors relation de soin**, un motif est obligatoire : en-tête `X-Motif-Acces` (encodé en pourcentage s'il contient
   des caractères non ASCII ; jamais dans l'URL, 300 caractères au plus, sans donnée de santé). Sans motif : `403`
   `dossier-sante.motif-obligatoire`.
4. **Bris de glace** : `X-Bris-De-Glace: true` + motif obligatoire (`400` sinon). La trace porte `brisDeGlace = true` ;
   le service Audit publie `audit.bris-de-glace-signale` pour l'alerte au CPMT dirigeant.
5. **Journalisation de chaque accès** par `IAuditTrail` (zone `medicale`, service `surveillance-medicale`) : type d'objet
   `dossier-sante.<partie>` (dossier, consultation, examen, decision, export, vaccinations, transfert, conservation…) et
   identifiant **du dossier**, pour que le DPO retrouve tous les accès à un dossier. Une lecture est tracée et validée
   avant que la donnée soit rendue ; une écriture est tracée dans la même transaction (outbox, ARC-32).
6. Le résumé d'une décision (catégorie, mesures codées, validité) est lisible avec `decision:lire` (autres conseillers,
   gestionnaire, employeur limité à son affilié par le claim `affilie_id`, travailleur limité à lui-même par le claim
   `personne_id`) ; chaque lecture est journalisée. Une décision en brouillon reste invisible hors zone médicale.

## Chiffrement applicatif (ARC-45)

`FieldEncryptor` (AES-256-GCM) avec des clés **propres à la zone médicale**, section de configuration
`ZoneMedicale:Encryption` (`CurrentKeyId`, `Keys:<id>`), distincte de la section `Encryption` des autres services. En
Azure : secrets Key Vault protégés par HSM, injectés par référence (CTR-16). Colonnes chiffrées (suffixe `_chiffre`) :
anamnèse et examen clinique (`observation_clinique`), valeurs et commentaire des actes (`resultat_acte`), réponses aux
questionnaires, titre et description des pièces jointes (le fichier reste au service Documents, référence
`document_id`), remarque de vaccination, lecture des tests tuberculiniques, justification et recommandations de la
décision, commentaire des recours, contenu des déclarations de maladie professionnelle et des demandes Fedris, paquet
des transferts. Restent en clair : identifiants, dates, statuts, codes de référentiel, catégorie de décision (elle sort
de la zone), indicateur « résultat inhabituel » (alertes SAN-23).

## Exigences couvertes

| Exigence | Réalisation |
|---|---|
| SAN-20 | `GET /dossiers/{id}/consultation` : `personne_id` (aucun NISS), postes et risques (projections `AffectationModifiee` + `ProfilRisquePosteModifie`), historique des examens et décisions, examens dus (`ObligationCreee`, satisfaits à la clôture), questionnaires, résultats, alertes, rappels vaccinaux, expositions |
| SAN-21 | Saisie structurée : observation clinique, actes `Biometrie`, `Vision`, `Audiometrie`, `Spirometrie`, `Ecg`, `Biologie`. Port `IImportAppareil` : adaptateurs `HL7` (v2 ORU^R01, segments OBX), `CSV` (`code;valeur;unité`) et `SIMULATEUR` ; le contenu importé n'est ni stocké ni journalisé |
| SAN-22 | Modèles de questionnaire versionnés (`/protocoles/questionnaires`), réponses chiffrées ; pré-remplissage par le travailleur (portail, claim `personne_id`) ou sur tablette (assistant médical) via `POST /questionnaires/pre-remplissage` (écriture seule) ; reprise des dernières réponses (`…/pre-rempli`) |
| SAN-23 | Valeurs de référence historisées (DAT-04) ; mesure hors intervalle → résultat inhabituel → proposition d'augmenter la fréquence (art. I.4-32), acceptée ou refusée par le CPMT. Acceptée, la nouvelle fréquence est saisie comme surcharge travailleur dans Postes et risques (AFF-13) |
| SAN-24 | Modèles de texte propres à chaque CPMT (`/modeles-texte`). **Dictée vocale hors périmètre** : un composant de reconnaissance vocale (poste client ou service externe) alimentera les champs de saisie ; aucune donnée audio n'est prévue dans ce service |
| SAN-30 | `GET /decisions/{id}/formulaire?exemplaire=Employeur|Travailleur|Dossier` : données du formulaire (annexe I.4-2) — voir *Contrat avec Documents* |
| SAN-31 | Décisions apte, apte avec mesures, inaptitude temporaire, inaptitude définitive, mutation, écartement (maternité), avec validité et règles de cohérence |
| SAN-32 | Port `ISignatureQualifiee` + `SignatureSimulee` ; la demande ne porte que l'empreinte SHA-256 du formulaire. **eID / itsme réel hors périmètre** (prestataire de signature qualifiée à distance à choisir) |
| SAN-33 | La signature émet `DecisionEmise` (catégorie, mesures codées, validité — jamais le motif médical) ; voies de concertation et de recours fournies au formulaire |
| SAN-34 | Concertation et recours auprès du médecin-inspecteur social : délais d'introduction et d'issue en jours ouvrables (paramètres `SurveillanceMedicale:DelaisRecours`, à valider), signalement des introductions tardives, issue ; une décision réformée est retransmise (`DecisionEmise`, même `DecisionId`) |
| SAN-40 | Dossier unique par `personne_id` (index unique), transversal aux employeurs ; parties art. I.4-85 à I.4-87 ; **expositions** alimentées par `MesurageEnregistre` pour les personnes rattachées au groupe d'exposition (`POST /dossiers/{id}/groupes-exposition`), ou saisies |
| SAN-41 | Pièces jointes (référence `document_id` du service Documents, métadonnées chiffrées) |
| SAN-42 | Transferts sortants (demande → export chiffré + empreinte → transmission) et entrants (contrôle d'intégrité, intégration) ; port `ICanalTransfertDossier` + simulateur — **canal réel à confirmer** (eHealthBox ou équivalent) |
| SAN-43 | `GET /dossiers/{id}/export` : export JSON structuré par parties, journalisé comme export (PDF ultérieurement par Documents) |
| SAN-44 | Archivage → date de purge = dernière activité + max(`SANTE.DOSSIER.CONSERVATION_MINIMUM` (≥ 15 ans), durées par type d'exposition) ; proposition, **validation humaine** par le CPMT dirigeant (`dossier-sante:purger`, motif), destruction physique (seule suppression physique du service) et **preuve de destruction** (empreinte SHA-256, volume, auteur) ; refus avec prolongation |
| SAN-50 | Schémas vaccinaux par risque, tests tuberculiniques (pose, lecture), calcul des doses et rappels dus |
| SAN-51 | Lots par centre (numéro, péremption, quantité), décompte à l'administration, alertes de péremption ; publie `VaccinationAdministree` (l'enregistrement Vaccinnet / e-Vax, SAN-52, est fait par Intégrations) |
| SAN-70 | Déclaration de maladie professionnelle préremplie (expositions, employeurs, dernière décision) ; port `IFedris` + simulateur |
| SAN-71 | Suivi : statut Fedris, demandes d'information et réponses |
| §14.5 | `Examen`, `Decision`, `PolitiqueDelaiReprise` (contrôle du délai de l'examen de reprise à la clôture), `CloturerExamen`, `IDossierSanteRepository` |

## Événements

- **Publiés** : `surveillance-medicale.examen-cloture.v1`, `surveillance-medicale.decision-emise.v1`,
  `surveillance-medicale.vaccination-administree.v1` (contrats existants, ARC-06), et les traces `audit.acces-donnee-sensible.v1`.
- **Consommés** (projections idempotentes : inbox + écriture par clé) : `obligations.obligation-creee`,
  `planification.rendez-vous-planifie`, `personnes.affectation-modifiee`, `postes-risques.profil-risque-poste-modifie`,
  `prevention.mesurage-enregistre`, `referentiels.parametre-legal-modifie` (`SANTE.DOSSIER.CONSERVATION_MINIMUM`, `SANTE.REPRISE.DELAI`).

## Contrat avec le service Documents (SAN-30, SAN-33)

1. À l'émission, `DecisionEmise` (catégorie, mesures, validité) déclenche la génération du formulaire.
2. Documents lit les données par exemplaire : `GET /api/v1/decisions/{id}/formulaire?exemplaire=Employeur` (permission
   `decision:lire`, aucune donnée médicale), `…=Travailleur` (+ recommandations) et `…=Dossier` (+ justification) en
   zone médicale (`dossier-sante:lire`, compte technique à définir). L'identité de la personne est lue au service
   Personnes ; les voies de concertation et de recours (types, délais, dates limites) sont fournies.
3. Documents produit le PDF/A en trois exemplaires ; la signature qualifiée a déjà été apposée côté Surveillance
   médicale sur l'empreinte du formulaire (référence `referenceSignature`). Le rattachement du `document_id` produit à la
   décision (`Decision.AssocierDocument`) sera exposé quand le contrat de Documents sera figé.

## Lacunes et suites

- Adaptateurs réels hors périmètre : signature qualifiée (eID/itsme), Fedris, canal de transfert, pilotes d'appareils
  (correspondances LOINC). La configuration `SurveillanceMedicale:Adaptateurs:<Port>` n'accepte que `Simulateur`.
- Consommation de `affilies.operation-affilie-modifiee` (transferts collectifs AFF-06) à ajouter.
- Valeurs de référence, schémas vaccinaux (codes de risque du jeu d'exemple `EX.`) et durées de conservation initiales
  sont indicatifs : **à valider par le département médical**.
- Claim `personne_id` du travailleur : à émettre par le fournisseur d'identité pour le portail travailleur.
