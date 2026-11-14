# ADR 0016 — Placement des capacités des lots 4 et 5 (prévention, psychosocial, prestations, reporting)

## Statut
Proposée le 2026-11-14 par l'architecte solution, à valider par le responsable produit. Le point 4 est aussi à valider
par l'architecte sécurité. Référence : [état des lieux de l'architecture](../architecture/etat-des-lieux-architecture.md),
§3.5. Prolonge les ADR 0008, 0009 et 0011.

## Contexte
Les lots 4 (23 tickets) et 5 (36 tickets) ajoutent quatre services prévus au §14.3 : Prévention, Psychosocial,
Prestations et Reporting. Le catalogue Terraform leur donne déjà une zone et des abonnements. Certains de ces
abonnements supposent que les BFF publient des événements :
- `prevention ← bff-employeur` ;
- `psychosocial ← bff-travailleur`.

Or l'ADR 0008 a écarté la publication par un BFF, qui n'a ni base ni outbox (ARC-32). Plusieurs exigences traversent
les services :
- HYG-06 (exposition vers le dossier de santé) ;
- ERG-02 (étiquetée en zone médicale) ;
- PSY-12 (mesures psychosociales dans le plan d'action) ;
- PSY-20 (journal d'accès psychosocial) ;
- NF-34 (hors-ligne).

## Décision
1. **Un service par contexte, à l'emplacement fixé par le §14.3.**
   - Prévention (RIS, SEC, ERG, HYG) : zone standard.
   - Psychosocial (PSY) : zone psychosociale.
   - Prestations (PRE) : zone standard.
   - Reporting (REP) : zone standard.
   - Aucun regroupement : chaque service a un propriétaire fonctionnel distinct (ARC-09) et le backlog est déjà étiqueté
     ainsi.
2. **Une demande venue d'un portail passe par un appel synchrone au service propriétaire.** C'est le cas de RIS-01, de
   PRE-06, des questionnaires PSY-10 et de la demande PSY-01. Le BFF appelle l'API du service propriétaire (ARC-30),
   qui persiste la demande et publie par son outbox, comme pour la reprise (ADR 0008).
   - Les abonnements `prevention ← bff-employeur` et `psychosocial ← bff-travailleur` sont retirés.
   - Pour PSY-10, l'anonymat (PSY-11) est garanti par le service Psychosocial : il ne stocke aucun lien entre une
     réponse et son répondant. Le BFF n'en conserve aucune trace.
3. **Interactions entre services** :
   - HYG-06 : Prévention publie `prevention.mesurage-enregistre.v1` (groupe, agent, niveau, date : ARC-06). Surveillance
     médicale relie elle-même l'exposition aux travailleurs du groupe, en zone médicale. Son gestionnaire existe déjà.
   - ERG-02 : la recommandation d'aménagement liée à un travailleur reste dans le service médical concerné
     (Réintégration ou Surveillance médicale). Prévention ne reçoit qu'une demande d'analyse de poste, avec des
     identifiants et un code.
   - Plan d'action (RIS-05, POR-02, REP-14) : son propriétaire est Prévention, qui consomme
     `MesurePreventionCreee`. Psychosocial n'y publie que des mesures collectives issues d'analyses anonymes
     (PSY-11, PSY-12), jamais une mesure tirée d'un dossier individuel.
   - Prestations consomme les clôtures d'actes (`ExamenCloture`, `TrajetTermine`, et à venir les clôtures de
     Prévention et de Psychosocial), qui ne portent qu'une discipline, une durée et des unités. L'export de facturation
     (PRE-07) passe par Intégrations.
   - Reporting construit ses projections à partir des événements, puis les complète par les instantanés de rattrapage
     (ADR 0011, #369 : tous les producteurs doivent les fournir avant la mise en service de Reporting). L'entrepôt
     pseudonymisé (REP-21) est alimenté par un job Container Apps quotidien.
     - L'écran « tableau de bord » du BFF interne (ADR 0013) reste opérationnel et en temps réel.
     - Reporting porte les indicateurs consolidés et légaux.
4. **Confidentialité psychosociale.**
   - La procédure formelle (PSY-04, PSY-05) est une saga hébergée par Psychosocial, sur le modèle de l'ADR 0008 :
     agrégat persistant et minuteries en base.
   - `documents-psychosociale` est ajouté au catalogue dès que Psychosocial produit son premier document (ADR 0009).
   - **Journal d'accès (PSY-20)** : les traces d'accès aux dossiers psychosociaux sont tenues **dans la zone
     psychosociale**, soit par une instance d'Audit de zone (modèle `instance_de` de l'ADR 0009), soit par un journal
     propre à Psychosocial.
     - Elles ne sont pas publiées sur la rubrique `audit` partagée, dont le service consommateur est en zone standard.
     - Une trace « tel utilisateur a accédé au dossier psychosocial de telle personne » révèle à elle seule l'existence
       du dossier.
     - **Choix entre les deux variantes à faire par l'architecte sécurité.**
5. **Hors-ligne (NF-34)** : l'application PWA synchronise ses données par l'API du service propriétaire de la donnée
   (Prévention pour les visites RIS-03). Si le hors-ligne s'étend aux consultations médicales des unités mobiles, leur
   synchronisation se fait en zone médicale (Surveillance médicale), jamais par Prévention. Le périmètre est à préciser
   par le responsable produit.

## Alternatives
- **Publication par les BFF (abonnements actuels).** Contraire à ARC-32 sans outbox côté BFF (ADR 0008). Écartée.
- **Prestations hébergé dans Reporting ou dans Affiliés.** Prestations est transactionnel (solde, réservations,
  alertes : PRE-03 à 05), alors que Reporting est en lecture seule. Le §14.4 en fait un propriétaire distinct. Écartée.
- **Psychosocial publiant ses traces d'accès sur la rubrique `audit` partagée.** C'est la règle générale, mais elle
  sort de la zone l'existence d'un dossier psychosocial. Écartée, sous réserve de l'avis sécurité.
- **Reporting par lecture synchrone des services (sans projections).** La charge sur les producteurs serait non
  maîtrisée, et la lecture serait impossible vers les zones sensibles. Écartée.

## Conséquences
- **Terraform** :
  - retrait des abonnements vers les rubriques des BFF ;
  - entrée `documents-psychosociale` au premier document psychosocial ;
  - éventuelle instance d'Audit en zone psychosociale ;
  - activation des services à leur lot (ADR 0015).
- **Contrats** : `prevention.mesure-prevention-creee.v1` doit pouvoir être produit par Psychosocial. C'est soit un
  contrat `psychosocial.mesure-prevention-creee.v1` dédié, soit un champ `Origine`, à trancher à l'écriture
  (catalogue §15.4).
- **Rattrapage** : #369 devient un prérequis de la mise en service de Reporting.
- **Responsables** : `software-architect-dotnet` (conception interne de chaque service), `security-architect`
  (point 4), `data-architect` (pseudonymisation de l'entrepôt, REP-21).
