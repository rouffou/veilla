# -*- coding: utf-8 -*-
# Données extraites du cahier des charges SEPP v1.1
# Format REQS : ID | section | titre court | texte de l'exigence

REQS_RAW = r"""
AFF-01|4.1|Fiche employeur|Fiche employeur : numéro BCE, dénomination, forme juridique, code NACE, commission paritaire, catégorie tarifaire (A à D), date d'affiliation et de fin, langue, régime linguistique.
AFF-02|4.1|Hiérarchie de l'affilié|Hiérarchie : groupe, entité juridique, unités d'établissement (BCE), sites physiques, départements. Un travailleur peut être rattaché à plusieurs niveaux.
AFF-03|4.1|Contacts de l'affilié|Contacts : personne de contact, conseiller en prévention interne, membres du Comité PPT ou délégation syndicale, personne de confiance, avec rôle et période.
AFF-04|4.1|Organes de concertation|Organes de concertation : présence d'un Comité PPT, dates de réunions, ordre du jour, participation du SEPP.
AFF-05|4.1|Historisation des modifications|Historique : toute modification est horodatée et versionnée (qui, quand, avant, après).
AFF-06|4.1|Fusion, scission et transfert d'affilié|Fusion, scission et transfert d'affilié entre SEPP, avec transfert encadré des dossiers de santé.
AFF-10|4.2|Catalogue de postes et fonctions|Catalogue de postes et fonctions propre à l'affilié, rattaché à un référentiel commun (métiers types).
AFF-11|4.2|Association poste ↔ risques (annexe I.4-5)|Association poste ↔ risques selon un référentiel importé de l'annexe I.4-5 (risques chimiques, physiques, biologiques, ergonomiques, psychosociaux, poste de sécurité, poste de vigilance, travail de nuit, conduite, etc.).
AFF-12|4.2|Règles de surveillance portées par le risque|Chaque risque porte automatiquement ses règles : type de surveillance, fréquence, actes médicaux supplémentaires, vaccinations, surveillance prolongée.
AFF-13|4.2|Surcharge de fréquence par le CPMT|Le CPMT peut surcharger une fréquence pour un poste, un groupe ou un travailleur, avec motif et période de validité.
AFF-14|4.2|Validation CPMT du lien poste ↔ risque|Toute modification du lien poste ↔ risque exige la validation du CPMT et trace l'avis du Comité PPT (date, pièce jointe).
AFF-20|4.3|Alimentation DIMONA / DmfA via BCSS|Alimentation automatique par les flux DIMONA et DmfA via la BCSS (entrées, sorties, type de contrat, étudiants, intérimaires).
AFF-21|4.3|Import manuel / fichier des travailleurs|Import manuel ou par fichier (CSV, Excel) pour les cas hors flux, avec contrôle du NISS et détection des doublons.
AFF-22|4.3|Rattachement travailleur ↔ postes|Rattachement du travailleur à un ou plusieurs postes, avec dates de début et de fin. Le système en déduit les risques et les obligations.
AFF-23|4.3|Cas particuliers de travailleurs|Cas particuliers : intérimaires (utilisateur et agence), stagiaires, étudiants, jeunes, travailleurs détachés, travailleurs de nuit, bénévoles.
AFF-24|4.3|Protection de la maternité|Protection de la maternité : l'employeur ou la travailleuse déclare une grossesse ou un allaitement ; le système déclenche l'examen et les mesures liées aux risques du poste.
AFF-30|4.4|Génération des listes nominatives|Génération automatique des listes légales : travailleurs soumis à la surveillance, par type (poste de sécurité, vigilance, activité à risque défini, etc.), avec la date de la dernière évaluation.
AFF-31|4.4|Proposition de modification des listes par l'employeur|Proposition de modification par l'employeur via le portail, validation par le CPMT, historique conservé au moins 5 ans.
AFF-32|4.4|Alertes de non-couverture|Alertes : travailleur exposé sans surveillance planifiée, poste sans analyse de risques, liste non revue depuis 12 mois (seuil paramétrable).
SAN-01|5.2|Calcul permanent des échéances|Calcul permanent des échéances par travailleur à partir des postes, des risques, de la dernière évaluation et des surcharges.
SAN-02|5.2|Statuts des obligations|Statuts : à planifier, planifié, convoqué, réalisé, absent, reporté, annulé, excusé, sorti de l'entreprise.
SAN-03|5.2|Regroupement intelligent des obligations|Regroupement intelligent : un seul rendez-vous couvre plusieurs obligations dues dans une fenêtre paramétrable.
SAN-04|5.2|Recalcul sur événement|Recalcul immédiat après chaque événement (changement de poste, sortie, nouveau risque, résultat inhabituel).
SAN-10|5.3|Génération des convocations|Génération de convocations par lot ou à l'unité, par courrier, e-mail, SMS ou portail, selon la préférence de l'affilié.
SAN-11|5.3|Envoi recommandé des convocations|Envoi recommandé électronique ou papier lorsque la loi l'exige (loi de 2003, invitations de réintégration).
SAN-12|5.3|Réservation en ligne|Réservation en ligne par le travailleur ou l'employeur dans des créneaux ouverts.
SAN-13|5.3|Rappels et reconvocations|Rappels automatiques J-7 et J-1 (paramétrables), gestion des absences et reconvocation.
SAN-20|5.4|Vue « poste de consultation »|Vue « poste de consultation » : identité, postes et risques, historique, examens dus, questionnaires déjà remplis, résultats d'actes.
SAN-21|5.4|Saisie clinique structurée et import d'appareils|Saisie structurée : anamnèse, examen clinique, biométrie, vision, audiométrie, spirométrie, ECG, biologie, avec import direct des appareils (HL7, fichiers, pilotes).
SAN-22|5.4|Questionnaires de santé préalables|Questionnaires de santé remplis à l'avance sur le portail ou sur tablette en salle d'attente.
SAN-23|5.4|Valeurs de référence et alertes|Valeurs de référence et alertes en cas de résultat inhabituel, avec proposition d'augmenter la fréquence (art. I.4-32).
SAN-24|5.4|Dictée vocale et modèles de texte|Dictée vocale et modèles de texte par CPMT.
SAN-30|5.5|Formulaire d'évaluation de santé (annexe I.4-2)|Génération du formulaire d'évaluation de santé conforme à l'annexe I.4-2, en trois exemplaires : employeur, travailleur, dossier.
SAN-31|5.5|Décisions d'évaluation de santé|Décisions : apte, apte avec mesures, inaptitude temporaire, inaptitude définitive, mutation, écartement (maternité), avec durée de validité.
SAN-32|5.5|Signature électronique qualifiée du CPMT|Signature électronique qualifiée du CPMT (eID ou itsme).
SAN-33|5.5|Transmission de la décision|Transmission automatique à l'employeur (portail) et au travailleur, avec mention des voies de concertation et de recours.
SAN-34|5.5|Suivi concertation et recours|Suivi des procédures de concertation et de recours : dates, délais, décision du médecin-inspecteur social.
SAN-40|5.6|Dossier de santé unique par NISS|Dossier unique par travailleur (NISS), transversal aux employeurs successifs affiliés au SEPP, structuré selon les parties prévues aux art. I.4-85 à I.4-87 (dont les données d'exposition).
SAN-41|5.6|Pièces jointes du dossier de santé|Pièces jointes : rapports de spécialistes, imageries, résultats externes.
SAN-42|5.6|Transfert sécurisé du dossier de santé|Transfert sécurisé du dossier vers un autre SEPP ou SIPP lors d'un changement d'employeur, et réception des dossiers entrants.
SAN-43|5.6|Droit d'accès du travailleur|Droit d'accès du travailleur : export PDF ou transmission à son médecin traitant.
SAN-44|5.6|Archivage et purge du dossier de santé|Archivage et purge selon des durées paramétrables par type d'exposition, avec validation humaine avant destruction.
SAN-50|5.7|Schémas vaccinaux et tests|Schémas vaccinaux par risque (hépatite B, tétanos, grippe, etc.) et tests tuberculiniques, avec rappels.
SAN-51|5.7|Lots et stock de vaccins|Enregistrement des lots, dates de péremption et stock de vaccins par centre.
SAN-52|5.7|Enregistrement Vaccinnet / e-Vax|Enregistrement automatique dans Vaccinnet (Flandre) ou e-Vax (Wallonie–Bruxelles).
SAN-60|5.8|Notifications d'incapacité|Réception des notifications d'incapacité (employeur, médecin-conseil) et information du travailleur dès 4 semaines d'incapacité.
SAN-61|5.8|Estimation du potentiel de travail|Estimation du potentiel de travail après 8 semaines, selon méthode standardisée, par CPMT ou infirmier.
SAN-62|5.8|Workflow du trajet de réintégration|Workflow du trajet formel : demande, invitation recommandée, évaluation, formulaire d'évaluation de réintégration, plan de réintégration ou rapport motivé de l'employeur, acceptation ou refus du travailleur (14 jours calendrier), fin du trajet.
SAN-63|5.8|Compteur d'absences et médecin-conseil|Compteur d'absences aux rendez-vous et notification au médecin-conseil après 2 absences.
SAN-64|5.8|Échanges eHealth réintégration|Échanges avec le médecin traitant, le médecin-conseil et le coordinateur Retour au travail via eHealth.
SAN-65|5.8|Tableau de bord des délais de réintégration|Tableau de bord des délais légaux avec alertes avant dépassement.
SAN-70|5.9|Déclaration de maladie professionnelle (Fedris)|Déclaration d'une maladie professionnelle vers Fedris et le médecin-inspecteur, préremplie depuis le dossier.
SAN-71|5.9|Suivi des dossiers Fedris|Suivi des dossiers déclarés et des demandes d'information de Fedris.
RIS-01|6.1|Demande d'intervention|Demande d'intervention : créée par l'employeur (portail), par un CP du SEPP ou par obligation légale ; qualification, discipline, urgence, estimation en unités de prévention.
RIS-02|6.1|Missions pluridisciplinaires|Mission : affectation à un ou plusieurs conseillers, pluridisciplinarité possible, jalons, statut.
RIS-03|6.1|Visites des lieux de travail (hors ligne)|Visites des lieux de travail : planification, check-lists par secteur, saisie mobile hors ligne sur tablette, photos, géolocalisation, signature du représentant de l'employeur.
RIS-04|6.1|Analyse des risques paramétrable|Analyse des risques : méthode paramétrable (Kinney, SOBANE, matrice probabilité × gravité), inventaire des dangers, évaluation, mesures, responsables, échéances.
RIS-05|6.1|Plan d'action global et annuel|Plan d'action : mesures issues de toutes les disciplines consolidées dans un plan global et un plan annuel d'action par affilié, suivi des mesures jusqu'à clôture.
RIS-06|6.1|Rapports de prévention|Rapports : modèles par type (visite, avis, analyse, enquête), génération Word ou PDF, validation par un pair si requis, envoi au portail.
RIS-07|6.1|Base de connaissance|Base de connaissance : fiches de danger, bonnes pratiques, textes légaux, réutilisables dans les rapports.
SEC-01|6.2|Enquête accidents du travail graves|Enquête sur les accidents du travail graves : réception de la notification, enquête, rapport circonstancié transmis à l'inspection dans le délai légal (paramétré), suivi des mesures.
SEC-02|6.2|Statistiques d'accidents|Statistiques d'accidents par affilié : fréquence, gravité, causes, en lien avec les déclarations reçues.
SEC-03|6.2|Avis équipements, EPI, incendie, chantiers|Avis sur équipements de travail, EPI, plans de prévention incendie, chantiers temporaires ou mobiles.
SEC-04|6.2|Suivi des contrôles périodiques|Suivi des contrôles périodiques obligatoires signalés par l'affilié (installations électriques, levage, etc.), sans s'y substituer.
ERG-01|6.3|Analyse de poste ergonomique|Analyse de poste : postures, manutention, travail sur écran, gestes répétitifs, avec méthodes standard (par ex. NIOSH, RULA, REBA).
ERG-02|6.3|Recommandations d'aménagement|Recommandations d'aménagement, y compris pour une réintégration ou une pré-reprise, liées au dossier du travailleur sans exposer de données médicales.
ERG-03|6.3|Questionnaires collectifs TMS / écran|Questionnaires collectifs (troubles musculo-squelettiques, travail sur écran) avec restitution anonymisée.
HYG-01|6.4|Inventaire des agents|Inventaire des agents chimiques, cancérogènes, biologiques et physiques par site et poste, avec fiches de données de sécurité.
HYG-02|6.4|Stratégie de mesurage|Stratégie de mesurage : groupes d'exposition homogènes, points de mesure, planning.
HYG-03|6.4|Gestion des mesures|Gestion des mesures : bruit, vibrations, poussières, agents chimiques, ambiance thermique, éclairage, rayonnements ; import des fichiers d'appareils et des résultats de laboratoire.
HYG-04|6.4|Comparaison aux valeurs limites|Comparaison automatique aux valeurs limites d'exposition belges (table de référence mise à jour) et aux valeurs d'action.
HYG-05|6.4|Parc d'appareils de mesure|Gestion du parc d'appareils de mesure : réservation, étalonnage, maintenance.
HYG-06|6.4|Alimentation des expositions du dossier de santé|Alimentation des données d'exposition du dossier de santé : l'exposition mesurée pour un groupe est liée aux travailleurs du groupe, sans que l'hygiéniste accède au dossier médical.
PSY-01|7.1|Premier contact psychosocial|Premier contact : enregistrement de l'entretien personnel préalable (délai paramétrable, 10 jours calendrier par défaut), information du demandeur sur les options.
PSY-02|7.1|Demande d'intervention informelle|Demande d'intervention psychosociale informelle : entretiens, intervention auprès d'un tiers, conciliation, avec accord écrit du demandeur.
PSY-03|7.1|Demande d'intervention formelle|Demande d'intervention psychosociale formelle à caractère principalement collectif ou individuel, et pour faits de violence ou de harcèlement moral ou sexuel au travail.
PSY-04|7.1|Workflow de la demande formelle|Workflow formel : réception du document signé, accusé de réception, recevabilité, information de l'employeur (sans identité si non requis), mesures conservatoires, examen, avis, communication aux parties, transmission éventuelle à l'inspection.
PSY-05|7.1|Délais légaux et protection contre les représailles|Gestion des délais légaux de chaque étape avec alertes, et suivi de la protection contre les représailles (date d'introduction, périmètre de protection).
PSY-06|7.1|Coordination avec la personne de confiance|Coordination avec la personne de confiance de l'entreprise, sans partage automatique du dossier.
PSY-10|7.2|Analyse des risques psychosociaux|Analyse des risques psychosociaux générale ou d'une situation de travail spécifique : méthode, questionnaires en ligne anonymes, entretiens, focus groups.
PSY-11|7.2|Garantie d'anonymat|Garantie d'anonymat : seuil minimal de répondants par groupe avant restitution (paramétrable, par exemple 5), agrégation obligatoire.
PSY-12|7.2|Restitution et intégration au plan d'action|Restitution sous forme de rapport et intégration des mesures dans le plan d'action global (section 6.1).
PSY-20|7.3|Chiffrement et journal d'accès psychosocial|Chiffrement distinct du dossier psychosocial et journal d'accès consultable par le CPAP dirigeant.
PSY-21|7.3|Registre des faits de tiers|Registre des faits de tiers tenu par l'employeur : consultable via le portail, sans lien automatique avec les dossiers individuels.
PSY-22|7.3|Conservation des dossiers psychosociaux|Durées de conservation propres aux dossiers psychosociaux (paramétrables), avec purge contrôlée.
PLA-01|8|Ressources planifiables|Ressources planifiables : conseillers, infirmiers, assistants, salles, cabines, appareils (audiomètre, spiromètre, sonomètre), unités mobiles.
PLA-02|8|Lieux de prestation|Lieux : centres médicaux fixes, cabinets en entreprise, unités mobiles (cars), consultations à distance (téléconsultation pour les actes qui le permettent).
PLA-03|8|Modèles d'agenda|Modèles d'agenda : plages par type d'acte et durée standard (paramétrable par CPMT et par type d'examen), jours de présence, congés importés depuis l'outil RH existant (lecture seule).
PLA-04|8|Planification automatique|Planification automatique : proposition de sessions par affilié ou par zone géographique à partir des échéances dues, avec optimisation des déplacements et regroupement par site.
PLA-05|8|Tournées des unités mobiles|Tournées des unités mobiles : itinéraire, chauffeur, emplacements, raccordements, capacité journalière.
PLA-06|8|Créneaux d'urgence légale|Gestion des urgences légales : créneaux réservés pour les examens de reprise, consultations spontanées et pré-reprises afin de tenir les 10 jours ouvrables.
PLA-07|8|Replanification en masse|Replanification en masse en cas d'absence d'un conseiller, avec notification automatique des personnes convoquées.
PLA-08|8|Salle d'attente|Salle d'attente : enregistrement à l'arrivée (borne ou accueil), file d'attente, appel en salle.
PLA-09|8|Synchronisation M365 / Google|Synchronisation des agendas avec Microsoft 365 ou Google (lecture et écriture, sans données médicales dans l'intitulé).
PRE-01|9|Enregistrement automatique des prestations|Enregistrement automatique d'une prestation à la clôture de chaque acte (examen, visite, mesure, rapport, intervention psychosociale), avec discipline, durée, conseiller, affilié.
PRE-02|9|Table de conversion en unités de prévention|Table de conversion prestations ↔ unités de prévention, versionnée par année et par catégorie d'affilié.
PRE-03|9|Solde d'unités par affilié|Solde d'unités par affilié : unités acquises via la cotisation forfaitaire (donnée reçue du système de facturation), consommées, réservées par les missions planifiées, restantes.
PRE-04|9|Répartition santé / autres prestations|Répartition entre prestations de surveillance de santé et autres prestations selon les règles de l'arrêté royal sur le financement (paramétrable).
PRE-05|9|Alertes sur le solde d'unités|Alertes : solde bas, dépassement prévisible, unités non utilisées en fin d'année, avec proposition d'actions de prévention à l'affilié.
PRE-06|9|Consultation du solde sur le portail|Consultation du solde et de l'historique par l'employeur sur le portail.
PRE-07|9|Export vers la facturation|Interface d'export (API ou fichier) vers la facturation, validée mensuellement par un gestionnaire, sans données médicales.
POR-01|10.1|Authentification employeur CSAM|Authentification via CSAM (eID, itsme) et gestion des accès par l'employeur via la gestion des accès de la sécurité sociale, avec délégation à un secrétariat social ou à un SIPP.
POR-02|10.1|Tableau de bord employeur|Tableau de bord : examens dus, en retard, planifiés ; missions en cours ; mesures du plan d'action ; solde d'unités.
POR-03|10.1|Gestion travailleurs, postes et risques (portail)|Gestion des travailleurs, postes et risques (propositions soumises à validation, voir AFF-14 et AFF-31).
POR-04|10.1|Demandes employeur|Demandes : examen de reprise, pré-reprise, estimation du potentiel de travail, visite, analyse, intervention, avec suivi du statut.
POR-05|10.1|Réception des décisions et rapports|Réception des décisions d'évaluation de santé, rapports et avis, avec accusé de lecture horodaté.
POR-06|10.1|Téléchargement des documents légaux|Téléchargement des documents légaux : listes nominatives, rapports annuels destinés au Comité PPT, attestations.
POR-10|10.2|Authentification forte travailleur|Authentification forte (itsme, eID) et accès mobile.
POR-11|10.2|Gestion des rendez-vous (travailleur)|Rendez-vous : consulter, confirmer, déplacer dans les créneaux autorisés, annuler avec motif.
POR-12|10.2|Questionnaires et demandes du travailleur|Questionnaires de santé à remplir avant l'examen, et demande de consultation spontanée ou de pré-reprise sans passer par l'employeur.
POR-13|10.2|Documents personnels du travailleur|Documents personnels : formulaires d'évaluation de santé, carnet de vaccination, demande de copie du dossier.
POR-14|10.2|Langues et accessibilité du portail travailleur|Choix de la langue (FR, NL, DE, EN) et conformité aux règles d'accessibilité (voir section 13.6).
DOC-01|10.3|Éditeur de modèles|Éditeur de modèles (courriers, rapports, formulaires) avec champs de fusion, versions par langue, validation avant publication.
DOC-02|10.3|PDF/A et signature qualifiée|Génération PDF/A pour l'archivage et signature électronique qualifiée.
DOC-03|10.3|Canaux de communication|Canaux : portail, e-mail sans données sensibles (notification seule), SMS, courrier via prestataire d'impression, recommandé électronique.
DOC-04|10.3|eBox entreprise et citoyen|eBox entreprise et eBox citoyen comme canaux officiels lorsque le destinataire est inscrit.
DOC-05|10.3|Journal des communications|Journal complet des communications par dossier (quoi, à qui, quand, par quel canal, preuve de dépôt).
REP-01|11.1|Rapport annuel du SEPP|Rapport annuel du SEPP destiné à l'administration, selon le modèle en vigueur.
REP-02|11.1|Données pour le rapport annuel du SIPP|Données par affilié pour son rapport annuel du service interne, fournies au format demandé par le SPF.
REP-03|11.1|Rapport de la section de surveillance médicale|Rapport de la section de surveillance médicale : volumes par type d'examen, décisions, vaccinations, maladies professionnelles déclarées, trajets de réintégration.
REP-04|11.1|Données agrégées pour l'inspection|Données agrégées et anonymisées pour les enquêtes de l'inspection ou des autorités de santé publique.
REP-10|11.2|Taux de réalisation de la surveillance|Taux de réalisation de la surveillance de santé par affilié, centre, région (dus, réalisés, en retard).
REP-11|11.2|Respect des délais légaux|Respect des délais légaux : reprises, consultations spontanées, réintégration, accidents graves, demandes psychosociales.
REP-12|11.2|Absences et occupation des agendas|Taux d'absence aux convocations et d'occupation des agendas.
REP-13|11.2|Charge et prévision des besoins|Charge par conseiller et par discipline, prévision des besoins sur 3 et 12 mois.
REP-14|11.2|Suivi des plans d'action|Suivi des plans d'action des affiliés : mesures ouvertes, en retard, clôturées.
REP-20|11.3|Tableaux de bord intégrés|Tableaux de bord intégrés avec filtres et droits appliqués.
REP-21|11.3|Entrepôt de données pseudonymisé|Entrepôt de données pseudonymisé alimenté quotidiennement, accessible à un outil décisionnel (Power BI ou équivalent), sans données médicales identifiantes.
REP-22|11.3|Exports et rapports planifiés|Exports CSV et Excel, rapports planifiés envoyés par e-mail.
INT-01|12|API REST OpenAPI + OAuth2|API REST documentée (OpenAPI) pour tout objet métier non médical, avec authentification OAuth2.
INT-02|12|Tableau de suivi des flux|Chaque flux dispose d'un tableau de suivi : volumes, erreurs, rejets, relance manuelle.
INT-03|12|Autorisations BCSS / eHealth|Les accès BCSS et eHealth respectent les autorisations du Comité de sécurité de l'information ; le SEPP fournit les délibérations à jour.
INT-04|12|Confirmation des formats et services externes|Les formats et services exacts (versions, schémas) sont à confirmer auprès de chaque organisme lors de l'analyse détaillée.
NF-01|13.1|Authentification forte (MFA / eID / itsme)|Authentification forte (MFA) pour tous les internes ; eID ou itsme obligatoire pour signer et pour accéder aux données médicales.
NF-02|13.1|Contrôle d'accès par rôle et périmètre|Contrôle d'accès par rôle et par périmètre, principe du moindre privilège, revue trimestrielle des droits.
NF-03|13.1|Chiffrement en transit et au repos|Chiffrement en transit (TLS 1.2 minimum) et au repos ; clés distinctes pour les données médicales et psychosociales.
NF-04|13.1|Journal d'audit infalsifiable|Journal d'audit infalsifiable de toute lecture et modification de données sensibles, conservé au moins 10 ans (paramétrable). Inclut l'accès « bris de glace » avec alerte au CPMT dirigeant (§3.3).
NF-05|13.1|Tests d'intrusion|Tests d'intrusion avant mise en production puis annuels ; correction des failles critiques sous 72 heures.
NF-06|13.1|Alignement ISO 27001 / NIS2|Alignement sur ISO 27001 et, le cas échéant, sur les exigences NIS2 applicables au SEPP.
NF-10|13.2|Analyse d'impact (AIPD)|Analyse d'impact (AIPD) avant la mise en production.
NF-11|13.2|Registre des traitements|Registre des traitements alimenté par l'application (finalités, catégories, durées).
NF-12|13.2|Outils pour les droits des personnes|Outils pour les droits des personnes : accès, rectification, limitation, export.
NF-13|13.2|Hébergement UE et sous-traitants|Hébergement dans l'Union européenne, sous-traitants listés et contractualisés.
NF-14|13.2|Pseudonymisation hors production|Pseudonymisation des environnements de test et de formation ; aucune donnée réelle hors production.
NF-20|13.3|Durées de conservation paramétrées|Durées de conservation paramétrées par type d'objet (dossier de santé selon exposition, dossier psychosocial, rapports, journaux).
NF-21|13.3|Archivage à valeur probante|Archivage électronique à valeur probante (PDF/A, horodatage, intégrité vérifiable).
NF-22|13.3|Purge contrôlée|Purge contrôlée : liste proposée, validation par le responsable du traitement, preuve de destruction.
NF-30|13.4|Disponibilité|Disponibilité de 99,5 % en heures ouvrables (7 h – 20 h), 99 % hors heures.
NF-31|13.4|Temps de réponse|Temps de réponse : 2 secondes au 95e percentile pour l'ouverture d'un dossier ; 10 secondes pour un rapport standard.
NF-32|13.4|Montée en charge|Montée en charge : au moins 500 utilisateurs internes simultanés et 5 000 sur les portails (à ajuster).
NF-33|13.4|Sauvegardes, RPO/RTO|Sauvegardes quotidiennes, RPO 1 heure, RTO 4 heures, test de restauration semestriel.
NF-34|13.4|Mode hors ligne sur tablette|Mode hors ligne sur tablette pour les visites et les unités mobiles, avec synchronisation et résolution de conflits.
NF-40|13.5|Interface multilingue FR/NL/DE (+EN portails)|Interface et documents en français, néerlandais et allemand ; anglais pour les portails.
NF-41|13.5|Langue des documents|Langue des documents déterminée par le régime linguistique de l'affilié ou le choix du travailleur, selon la législation sur l'emploi des langues.
NF-50|13.6|Accessibilité EN 301 549 / WCAG 2.1 AA|Portails conformes à la norme EN 301 549 (WCAG 2.1 niveau AA).
NF-51|13.6|Ergonomie de saisie rapide|Interfaces internes optimisées pour la saisie rapide : raccourcis clavier, recherche globale, au plus 3 clics pour les actions fréquentes.
NF-52|13.6|Compatibilité navigateurs et tablettes|Compatibilité avec les navigateurs récents et tablettes (iPadOS, Android).
NF-60|13.7|Environnements séparés|Solution web, hébergement cloud ou sur site au choix du SEPP, environnements de développement, test, recette et production séparés.
NF-61|13.7|Paramétrage métier sans développement|Paramétrage métier sans développement (référentiels, délais, modèles, workflows simples).
NF-62|13.7|Supervision et tableaux de bord techniques|Supervision, alertes et tableaux de bord techniques mis à disposition du SEPP.
NF-63|13.7|Réversibilité|Réversibilité : export complet des données dans un format ouvert et documenté en fin de contrat.
ARC-01|14.1|Découpage DDD en contextes délimités|Découpage par domaine métier (Domain-Driven Design) : un service correspond à un contexte délimité, a une équipe responsable et un cycle de déploiement indépendant.
ARC-02|14.1|Une base de données par service|Une base de données par service. Aucun service n'accède directement à la base d'un autre ; les échanges passent uniquement par API ou par événements.
ARC-03|14.1|Clean architecture dans chaque service|Clean architecture dans chaque service : le domaine métier ne dépend d'aucune technologie (section 14.5).
ARC-04|14.1|Trois zones de sensibilité|Trois zones de sensibilité (standard, médicale, psychosociale), avec pour chacune un réseau, des bases, des clés de chiffrement et des droits distincts.
ARC-05|14.1|Asynchrone par défaut|Communication asynchrone par événements par défaut. Les appels synchrones sont réservés aux lectures nécessaires à une interaction utilisateur.
ARC-06|14.1|Pas de donnée clinique hors zone|Aucune donnée clinique ou psychosociale ne quitte sa zone dans un événement ou une API : seuls des identifiants, des statuts et des catégories de décision en sortent.
ARC-07|14.1|Services sans état et scalables|Services sans état, scalables horizontalement, avec une configuration externalisée.
ARC-08|14.1|Plateforme cible Azure|La plateforme cible est Microsoft Azure ; les services sont conteneurisés et exécutés, au choix, sur App Service, Container Apps ou AKS (section 14.8). Langages et frameworks maintenus (LTS) et justifiés.
ARC-09|14.4|Matrice de traçabilité exigences ↔ services|Toute exigence fonctionnelle a un service propriétaire unique. Cette matrice de traçabilité est tenue à jour et sert de base au plan de recette (REC-01).
ARC-20|14.5|Tests d'architecture (domaine pur)|Le domaine ne référence aucun framework, ORM ou bibliothèque d'infrastructure. Règle vérifiée automatiquement par des tests d'architecture (ArchUnit, NetArchTest ou équivalent).
ARC-21|14.5|Règles légales en politiques injectées|Chaque règle légale paramétrable est portée par le domaine sous forme de politique injectée (par exemple PolitiqueDelaiReprise), alimentée par le service Référentiels.
ARC-22|14.5|Cas d'usage transactionnel + outbox|Un cas d'usage correspond à une transaction ; les événements de domaine qu'il produit sont publiés via l'outbox.
ARC-23|14.5|Stratégie de tests|Tests : domaine et application testés unitairement sans infrastructure (couverture cible 80 %), adaptateurs testés avec des conteneurs éphémères, contrats entre services vérifiés par tests de contrat (Pact ou équivalent).
ARC-24|14.5|Gabarit de service commun|Un gabarit de service commun (structure, observabilité, sécurité, pipeline) est fourni pour garantir l'homogénéité des services.
ARC-30|14.6|Communication synchrone résiliente|Synchrone : REST (JSON, contrat OpenAPI) ou gRPC entre BFF et services, avec délais d'expiration, reprises avec temporisation et disjoncteur.
ARC-31|14.6|Bus d'événements et consommateurs idempotents|Asynchrone : bus d'événements avec livraison au moins une fois ; consommateurs idempotents (clé d'idempotence, table des messages traités).
ARC-32|14.6|Outbox transactionnelle|Outbox transactionnelle : l'événement est écrit dans la même transaction que la donnée, puis publié, pour ne jamais perdre ni inventer un événement.
ARC-33|14.6|Sagas pour les processus longs|Les processus longs (reprise, réintégration, procédure psychosociale formelle) sont orchestrés par des sagas, avec compensation et minuteries pour les délais légaux. Exemple de référence : saga « examen de reprise » (§14.6).
ARC-34|14.6|Versionnement des API et événements|API et événements versionnés, schémas enregistrés dans un registre, compatibilité ascendante garantie sur au moins une version.
ARC-35|14.6|CQRS pour les écrans composites|CQRS pour les écrans composites (tableau de bord employeur, vue consultation) : modèles de lecture dédiés construits à partir des événements.
ARC-40|14.7|Fournisseur d'identité OIDC unique|Fournisseur d'identité OIDC unique ; fédération avec CSAM (itsme, eID) pour les utilisateurs externes ; jetons de courte durée et propagation de l'identité de bout en bout.
ARC-41|14.7|Autorisation fine centralisée (OPA)|Autorisation fine par rôle, périmètre et relation de soin, définie de manière centralisée et évaluée dans chaque service (Open Policy Agent ou équivalent). Inclut la matrice des droits du §3.3.
ARC-42|14.7|API Gateway|API Gateway : terminaison TLS, pare-feu applicatif, limitation de débit, routage vers les BFF.
ARC-43|14.7|Un BFF par canal|Un BFF par canal (interne, employeur, travailleur) pour agréger et adapter les données au front, sans logique métier.
ARC-44|14.7|Coffre de secrets et HSM|Secrets et clés dans un coffre ; clés des zones médicale et psychosociale protégées par HSM, avec rotation automatique.
ARC-45|14.7|Chiffrement applicatif des champs sensibles|Chiffrement applicatif des champs sensibles (NISS, contenu clinique, contenu psychosocial) en plus du chiffrement du stockage.
ARC-46|14.7|mTLS et segmentation réseau|mTLS entre services et segmentation réseau par zone de sensibilité.
ARC-47|14.7|Observabilité OpenTelemetry|Observabilité : traces distribuées (OpenTelemetry), journaux structurés sans données sensibles, métriques, identifiant de corrélation de bout en bout.
ARC-50|14.9|Déploiement indépendant et progressif|Chaque service est déployable indépendamment, avec déploiement progressif (slots, révisions ou canary selon la plateforme) et retour arrière automatisé.
ARC-51|14.9|Infrastructure as Code|Toute l'infrastructure est décrite en code et versionnée ; aucun changement manuel en production.
ARC-52|14.9|Région Azure Belgium Central + PRA UE|Hébergement dans la région Azure Belgium Central (Bruxelles, trois zones de disponibilité) ; cette région n'étant pas appairée, la reprise après sinistre est configurée explicitement vers une seconde région de l'UE.
ARC-53|14.9|Vérification de disponibilité des services Azure|La disponibilité de chaque service Azure retenu (et de sa redondance de zone) dans la région est vérifiée avant le choix définitif ; à défaut, une alternative dans l'UE est documentée.
CTR-00|14.8|Choix de la plateforme d'exécution (ADR)|La plateforme d'exécution est choisie parmi App Service, Container Apps et AKS, pour l'ensemble de la solution ou par groupe de services. Le choix est documenté (décision d'architecture) au regard des critères du §14.8, des coûts et des compétences de l'équipe d'exploitation.
CTR-01|14.8|Une image OCI par service|Une image OCI par microservice, BFF et front, construite par un Dockerfile versionné dans le dépôt du service.
CTR-02|14.8|Build multi-stage|Construction en plusieurs étapes (multi-stage) : les outils de build ne sont pas présents dans l'image finale.
CTR-03|14.8|Images de base minimales|Images de base minimales et approuvées (distroless ou Alpine, par exemple), mises à jour au moins mensuellement et à chaque vulnérabilité critique.
CTR-04|14.8|Exécution non root|Exécution avec un utilisateur non root, système de fichiers en lecture seule lorsque la plateforme le permet, aucune capacité superflue.
CTR-05|14.8|Aucun secret dans l'image|Aucun secret ni configuration d'environnement dans l'image : une même image est promue de l'environnement de test jusqu'à la production, et reste portable entre App Service, Container Apps et AKS.
CTR-06|14.8|Scan, SBOM et signature des images|Chaque image est analysée (vulnérabilités, licences), accompagnée d'une nomenclature logicielle (SBOM) et signée ; le déploiement refuse toute image non signée ou présentant une vulnérabilité critique.
CTR-07|14.8|Registre ACR Premium privé|Registre privé Azure Container Registry (niveau Premium, point de terminaison privé), avec rétention des versions déployées et étiquettes immuables (version sémantique et empreinte).
CTR-10|14.8|Unité d'isolement par zone|Une unité d'isolement par zone de sensibilité (standard, médicale, psychosociale) plus une pour la plateforme : plan App Service, environnement Container Apps ou espace de noms AKS avec pool de nœuds dédié, chacun dans son sous-réseau.
CTR-11|14.8|Réseau en refus par défaut|Réseau en refus par défaut (groupes de sécurité réseau, politiques réseau sur AKS), accès aux données uniquement par points de terminaison privés, chiffrement de bout en bout entre services.
CTR-12|14.8|Sondes de santé et arrêt propre|Sondes de santé (démarrage, vivacité, disponibilité) pour chaque conteneur ; arrêt propre sur signal (fin des traitements en cours, publication de l'outbox).
CTR-13|14.8|Dimensionnement et autoscale|Ressources CPU et mémoire dimensionnées pour chaque service ; mise à l'échelle automatique sur la charge ou la longueur des files.
CTR-14|14.8|Haute disponibilité (≥ 2 instances)|Au moins deux instances par service en production, avec redondance de zone activée lorsque la région et le niveau de service le permettent.
CTR-15|14.8|Services managés pour l'état|Services sans état : aucune donnée persistante dans les conteneurs. Bases de données, bus et stockage objet sont des services managés, sauvegardés indépendamment.
CTR-16|14.8|Identités managées et Key Vault|Accès aux ressources Azure par identités managées ; secrets lus dans Azure Key Vault à l'exécution (références Key Vault ou pilote CSI sur AKS), jamais dans le code ni les manifestes.
CTR-17|14.8|Azure Policy|Azure Policy impose les règles CTR-04, CTR-06, CTR-07 et CTR-11 sur toutes les ressources (complétée par Gatekeeper ou Kyverno sur AKS).
CTR-20|14.8|Chaîne CI/CD par service|Chaîne CI/CD par service : compilation, tests, tests d'architecture, analyse de code et de dépendances, construction de l'image, signature, publication au registre.
CTR-21|14.8|Déploiements déclaratifs (Bicep/Terraform, GitOps)|Infrastructure et déploiements déclaratifs : Bicep ou Terraform pour App Service et Container Apps ; Helm et GitOps (Argo CD ou Flux) en complément sur AKS.
CTR-22|14.8|Environnements dev/test/recette/préprod/prod|Environnements développement, test, recette, préproduction et production séparés (abonnements ou groupes de ressources distincts) ; la préproduction reproduit la topologie de production.
CTR-23|14.8|Environnement local Docker Compose|Environnement local complet pour les développeurs (Docker Compose) avec données fictives.
CTR-24|14.8|Environnements éphémères par PR|Environnements éphémères par demande de fusion pour les tests d'intégration, détruits automatiquement.
DAT-01|15.1|Identifiants UUID v7|Identifiants techniques UUID (version 7, ordonnés dans le temps), générés par le service propriétaire. Les identifiants métier (NISS, numéro BCE) sont des attributs uniques.
DAT-02|15.1|Références inter-services par identifiant|Les références entre services se font uniquement par identifiant, sans clé étrangère physique ; la cohérence est assurée par les événements.
DAT-03|15.1|Colonnes d'audit et verrou optimiste|Colonnes d'audit sur toutes les tables : created_at, created_by, updated_at, updated_by, version (verrou optimiste).
DAT-04|15.1|Historisation par période de validité|Historisation par période de validité (valid_from, valid_to) pour les affectations, liens poste-risque, contacts et surcharges : on clôture une ligne au lieu de la modifier.
DAT-05|15.1|Suppression logique|Suppression logique (deleted_at) ; seule la purge légale, exécutée par un processus dédié et tracé, supprime physiquement.
DAT-06|15.1|NISS chiffré dans Personnes uniquement|Le NISS n'est stocké que dans le service Personnes, chiffré, avec un index de recherche par hachage à clé. Les autres services ne connaissent que personne_id.
DAT-07|15.1|Codes de référentiels versionnés multilingues|Codes issus de référentiels versionnés, avec libellés en français, néerlandais, allemand et anglais.
DAT-08|15.1|Horodatages UTC et jours ouvrables belges|Horodatages en UTC ; délais légaux calculés avec le calendrier belge des jours ouvrables (jours fériés paramétrés dans Référentiels).
DAT-09|15.1|Conventions de nommage et migrations|Nommage en snake_case, tables au singulier ; migrations de schéma versionnées dans le dépôt du service.
MIG-01|16.2|Inventaire et cartographie des sources|Inventaire des sources existantes, cartographie champ à champ, règles de transformation validées par le métier.
MIG-02|16.2|Reprise de l'historique complet|Reprise de l'historique complet des dossiers de santé et psychosociaux, pièces jointes comprises, avec contrôle d'intégrité (comptages, sommes de contrôle).
MIG-03|16.2|Migrations à blanc|Au moins deux migrations à blanc avec recette métier avant la bascule.
MIG-04|16.2|Bascule et plan de retour arrière|Bascule planifiée hors période de pointe, avec plan de retour arrière.
REC-01|16.3|Plan de tests par exigence|Plan de tests par exigence (identifiant du présent document), jeux de données pseudonymisés.
REC-02|16.3|Recette métier par discipline et profil|Recette métier par des représentants de chaque discipline et de chaque profil.
REC-03|16.3|Tests de charge, sécurité, accessibilité|Tests de charge, de sécurité et d'accessibilité avant chaque mise en production.
REC-04|16.3|Période de vérification de service régulier|Période de vérification de service régulier (par exemple 3 mois) avant la réception définitive.
FOR-01|16.4|Formation par profil|Formation par profil, supports en FR et NL, environnement de formation permanent.
FOR-02|16.4|Réseau de référents|Réseau de référents par centre et par discipline.
FOR-03|16.4|Guides en ligne|Guides en ligne pour les employeurs et les travailleurs.
MAI-01|16.5|Support et SLA|Support en heures ouvrables avec niveaux de gravité et délais de prise en charge contractuels (bloquant : 1 heure ; majeur : 4 heures ; mineur : 2 jours ouvrables).
MAI-02|16.5|Veille réglementaire|Veille réglementaire : adaptation du logiciel à toute modification légale avant sa date d'entrée en vigueur.
MAI-03|16.5|Feuille de route et notes de version|Feuille de route partagée, au moins deux versions majeures par an, notes de version publiées.
"""

# Tâches complémentaires dérivées du document (§3.3, §14.6, §15.3, §15.4)
EXTRA_RAW = r"""
TSK-DROITS|3.3|Implémenter la matrice des droits (synthèse)|Traduire la matrice des droits du §3.3 en politiques d'autorisation : dossier de santé (CPMT/infirmier seuls), décision (écriture CPMT, lecture CP/gestionnaire/employeur/travailleur), dossier psychosocial (CPAP seul), analyse de risques, données de l'affilié. Tout accès hors relation de soin est motivé et journalisé ; accès « bris de glace » avec alerte au CPMT dirigeant.
TSK-PROFILS|3.1|Créer les profils internes et externes|Créer les rôles : CPMT, CPMT dirigeant, infirmier·ère, assistant·e médical·e, CP sécurité, CP ergonome, CP hygiéniste, CPAP, gestionnaire de dossiers, planificateur·rice, responsable de centre/région, direction, administrateur fonctionnel, DPO ; externes : employeur/contact, SIPP, travailleur, médecin traitant/conseil (via eHealth), inspection (exports).
TSK-REGLES-LEGALES|2.1|Paramétrer les règles légales structurantes|Rendre paramétrables (délais, fréquences, durées) les règles du §2.1 : secret médical, formulaire en 3 exemplaires, examen de reprise (≥4 semaines d'absence, J à J+10 ouvrables), consultation spontanée/pré-reprise (10 j ouvrables), réintégration 2026 (8 semaines, 6 mois, 2 absences, 49 jours), conservation du dossier de santé (≥15 ans), listes nominatives.
TSK-EVENTS|15.4|Définir les contrats d'événements (catalogue 15.4)|Publier dans le registre de schémas les événements du §15.4 : AffilieCree/Modifie, OccupationDebutee/Terminee, AffectationModifiee, ProfilRisquePosteModifie, ObligationCreee/Echue, RendezVousPlanifie/Annule, ExamenCloture, DecisionEmise, VaccinationAdministree, IncapaciteNotifiee, RepriseAnnoncee, TrajetDemarre/Termine, MesurageEnregistre, MesurePreventionCreee, PrestationEnregistree, DocumentPublie. Contenu limité aux identifiants, dates, statuts et catégories (ARC-06).
TSK-SAGA-REPRISE|14.6|Saga de référence « examen de reprise »|Implémenter la saga de bout en bout : RepriseAnnoncee (BFF employeur) → ObligationCreee (échéance J+10 ouvrables) → RendezVousPlanifie sur créneau d'urgence + convocation → DecisionEmise → génération du formulaire, dépôt portail, clôture de l'obligation et enregistrement de la prestation.
TSK-GLOSSAIRE|17|Glossaire et langage omniprésent|Reprendre le glossaire du §17 comme langage omniprésent (DDD) dans le code et la documentation.
"""

SCHEMAS = {
    "affilies": "affilie, groupe, unite_etablissement, site, departement, contact, organe_concertation",
    "personnes": "personne (niss_chiffre, niss_hash), occupation, affectation, etat_particulier (chiffré)",
    "postes-risques": "poste, risque, regle_surveillance (versionnée), poste_risque, surcharge_frequence, liste_nominative",
    "obligations": "obligation, trace_calcul",
    "planification": "ressource, lieu, creneau, rendez_vous, convocation, session",
    "surveillance-medicale": "dossier_sante, examen, observation_clinique (chiffré), resultat_acte (chiffré), decision, recours, exposition, vaccination, lot_vaccin, declaration_mp",
    "reintegration": "incapacite, estimation_potentiel, trajet, etape_trajet, absence_rdv",
    "prevention": "demande_intervention, mission, visite, analyse_risque, evaluation_danger, mesure_prevention, accident, groupe_exposition, mesurage, appareil",
    "psychosocial": "dossier_psy, partie, etape_psy, analyse_collective, reponse_questionnaire",
    "prestations": "prestation, table_conversion, solde_unites",
    "documents": "modele, document, signature",
    "communications": "message, preuve_envoi",
    "audit": "entree_audit (ajout seul, chaînage par empreinte)",
}

# Services (§14.3 / §14.4) : clé -> (nom, zone, description)
SERVICES = {
    "affilies": ("Affiliés", "standard", "Structure de l'affilié, contacts, organes de concertation, historique, fusions et transferts."),
    "personnes": ("Personnes et occupations", "standard", "Identité des travailleurs (NISS protégé), occupations issues de DIMONA, affectations aux postes, cas particuliers, protection de la maternité."),
    "postes-risques": ("Postes et risques", "standard", "Catalogue de postes, référentiel des risques, règles de surveillance, surcharges, listes nominatives."),
    "obligations": ("Obligations (moteur d'échéances)", "standard", "Calcul de ce qui est dû pour chaque travailleur et pourquoi, statuts, regroupement, recalcul sur événement, alertes de non-couverture."),
    "planification": ("Planification", "standard", "Ressources, agendas, créneaux, rendez-vous, convocations, tournées, salle d'attente, synchronisation d'agendas."),
    "surveillance-medicale": ("Surveillance médicale", "medicale", "Consultations, actes, décisions et formulaire d'évaluation de santé, recours, dossier de santé, vaccinations, maladies professionnelles."),
    "reintegration": ("Réintégration", "medicale", "Notifications d'incapacité, estimation du potentiel de travail, trajet formel, absences, délais légaux."),
    "prevention": ("Prévention (sécurité, ergonomie, hygiène)", "standard", "Demandes et missions, visites hors ligne, analyses de risques, plan d'action, accidents, ergonomie, mesurages et appareils."),
    "psychosocial": ("Psychosocial", "psychosociale", "Demandes informelles et formelles, délais, protection, analyses collectives anonymes, confidentialité."),
    "prestations": ("Prestations et unités de prévention", "standard", "Enregistrement des prestations, conversion en unités, soldes, alertes, export vers la facturation."),
    "documents": ("Documents", "standard", "Modèles multilingues, génération PDF/A, signature électronique qualifiée, archivage probant."),
    "communications": ("Communications", "standard", "Envoi multicanal, eBox, recommandé électronique, journal et preuves d'envoi."),
    "integrations": ("Intégrations", "standard", "Couche anti-corruption : BCSS, DIMONA/DmfA, BCE, eHealth, Vaccinnet/e-Vax, Fedris, eBox, laboratoires, facturation."),
    "referentiels": ("Référentiels", "standard", "Nomenclatures partagées (NACE, CP, jours fériés, risques), libellés en quatre langues."),
    "audit": ("Audit", "transverse", "Journal infalsifiable des accès et modifications, accès « bris de glace »."),
    "reporting": ("Reporting", "transverse", "Rapports légaux, tableaux de bord, entrepôt pseudonymisé, exports."),
    "bff-employeur": ("BFF & portail employeur", "standard", "Portail employeur : tableau de bord, travailleurs, demandes, décisions, documents."),
    "bff-travailleur": ("BFF & portail travailleur", "standard", "Portail travailleur : rendez-vous, questionnaires, documents personnels."),
    "identite": ("Identité et autorisation", "transverse", "Authentification forte, fédération CSAM, droits par rôle, périmètre et relation de soin."),
    "plateforme": ("Plateforme, architecture & conteneurisation", "transverse", "Principes d'architecture, clean architecture, communication inter-services, conteneurs, CI/CD, infrastructure Azure, conventions de données."),
    "qualite": ("Qualité, sécurité & conformité", "transverse", "Sécurité, RGPD, conservation, performance, ergonomie et accessibilité, exploitation."),
    "deploiement": ("Migration, recette, formation & support", "transverse", "Migration des données, recette, conduite du changement, maintenance."),
}

def _ids(prefix, a, b):
    return [f"{prefix}-{i:02d}" for i in range(a, b + 1)]

# Service propriétaire de chaque exigence (§14.4 + choix pour les non fonctionnelles)
OWNER = {}
def _own(svc, ids):
    for i in ids:
        OWNER[i] = svc

_own("affilies", _ids("AFF", 1, 6))
_own("personnes", _ids("AFF", 20, 24))
_own("postes-risques", _ids("AFF", 10, 14) + ["AFF-30", "AFF-31"])
_own("obligations", _ids("SAN", 1, 4) + ["AFF-32"])
_own("planification", _ids("SAN", 10, 13) + _ids("PLA", 1, 9))
_own("surveillance-medicale", _ids("SAN", 20, 24) + _ids("SAN", 30, 34) + _ids("SAN", 40, 44) + ["SAN-50", "SAN-51", "SAN-70", "SAN-71"])
_own("reintegration", _ids("SAN", 60, 65))
_own("prevention", _ids("RIS", 1, 7) + _ids("SEC", 1, 4) + _ids("ERG", 1, 3) + _ids("HYG", 1, 6))
_own("psychosocial", _ids("PSY", 1, 6) + _ids("PSY", 10, 12) + _ids("PSY", 20, 22))
_own("prestations", _ids("PRE", 1, 7))
_own("documents", ["DOC-01", "DOC-02"])
_own("communications", _ids("DOC", 3, 5))
_own("integrations", _ids("INT", 1, 4) + ["SAN-52"])
_own("referentiels", ["NF-40", "NF-41", "DAT-07", "DAT-08"])
_own("audit", ["NF-04"])
_own("reporting", _ids("REP", 1, 4) + _ids("REP", 10, 14) + _ids("REP", 20, 22))
_own("bff-employeur", _ids("POR", 1, 6))
_own("bff-travailleur", _ids("POR", 10, 14))
_own("identite", ["NF-01", "NF-02", "ARC-40", "ARC-41", "TSK-DROITS", "TSK-PROFILS"])
_own("plateforme", [i for i in [f"ARC-{n:02d}" for n in list(range(1, 10)) + list(range(20, 25)) + list(range(30, 36)) + list(range(42, 48)) + list(range(50, 54))]]
     + ["CTR-00"] + _ids("CTR", 1, 7) + _ids("CTR", 10, 17) + _ids("CTR", 20, 24)
     + ["DAT-01", "DAT-02", "DAT-03", "DAT-04", "DAT-05", "DAT-06", "DAT-09", "TSK-EVENTS", "TSK-SAGA-REPRISE", "TSK-GLOSSAIRE", "TSK-REGLES-LEGALES"])
_own("qualite", ["NF-03", "NF-05", "NF-06"] + _ids("NF", 10, 14) + _ids("NF", 20, 22) + _ids("NF", 30, 34) + _ids("NF", 50, 52) + _ids("NF", 60, 63))
_own("deploiement", _ids("MIG", 1, 4) + _ids("REC", 1, 4) + _ids("FOR", 1, 3) + _ids("MAI", 1, 3))

# Labels de service secondaires (contributeurs)
EXTRA_SVC = {
    "PSY-20": ["audit"], "SAN-24": [], "HYG-06": ["surveillance-medicale"], "ERG-02": ["reintegration"],
    "SAN-11": ["communications"], "SAN-12": ["bff-travailleur", "bff-employeur"], "SAN-22": ["bff-travailleur"],
    "SAN-33": ["communications", "bff-employeur"], "SAN-43": ["bff-travailleur"], "SAN-64": ["integrations"],
    "SAN-70": ["integrations"], "SAN-32": ["documents"], "SAN-30": ["documents"], "AFF-20": ["integrations"],
    "AFF-24": ["obligations"], "PRE-06": ["bff-employeur"], "PRE-07": ["integrations"], "PLA-09": ["integrations"],
    "DOC-04": ["integrations"], "RIS-01": ["bff-employeur"], "PSY-12": ["prevention"], "NF-34": ["prevention"],
    "NF-50": ["bff-employeur", "bff-travailleur"], "POR-01": ["identite"], "POR-10": ["identite"],
    "TSK-SAGA-REPRISE": ["obligations", "planification", "surveillance-medicale", "documents", "communications", "prestations", "bff-employeur"],
}

# Milestones = lots (§16.1) + lot 0 fondations + transverse
MILESTONES = [
    ("Lot 0 – Fondations techniques", "Plateforme Azure, gabarit de service clean architecture, bus d'événements, identité, CI/CD, conteneurs, conventions de données (§14, §15). Prérequis à tous les lots."),
    ("Lot 1 – Socle", "Affiliés, travailleurs, flux BCSS et DIMONA, référentiel des risques, droits, portail employeur en lecture (§16.1)."),
    ("Lot 2 – Surveillance de santé", "Moteur d'échéances, planification, consultation, formulaire d'évaluation de santé, dossier de santé, vaccinations (§16.1)."),
    ("Lot 3 – Réintégration et portails complets", "Trajets, estimation du potentiel de travail, eHealth, portail travailleur (§16.1)."),
    ("Lot 4 – Gestion des risques", "Sécurité, ergonomie, hygiène, plan d'action, mobilité hors ligne (§16.1)."),
    ("Lot 5 – Psychosocial, unités de prévention et reporting", "Dossiers psychosociaux, suivi des unités, rapports légaux, entrepôt de données (§16.1)."),
    ("Transverse – Conformité, recette et mise en service", "Sécurité, RGPD, conservation, performance, migration, recette, formation et maintenance : exigences continues vérifiées à chaque mise en production (§13, §16)."),
]
M0, M1, M2, M3, M4, M5, MT = [m[0] for m in MILESTONES]

LOT = {}
def _lot(m, ids):
    for i in ids:
        LOT[i] = m

# Par défaut : lot selon service
SVC_LOT = {
    "affilies": M1, "personnes": M1, "postes-risques": M1, "integrations": M1, "referentiels": M1,
    "audit": M1, "identite": M1, "bff-employeur": M1,
    "obligations": M2, "planification": M2, "surveillance-medicale": M2, "documents": M2, "communications": M2,
    "reintegration": M3, "bff-travailleur": M3,
    "prevention": M4,
    "psychosocial": M5, "prestations": M5, "reporting": M5,
    "plateforme": M0, "qualite": MT, "deploiement": MT,
}
# Ajustements
_lot(M0, ["NF-01", "NF-02", "ARC-40", "ARC-41", "TSK-PROFILS", "NF-03", "NF-60", "NF-62", "NF-13", "NF-14", "DAT-07", "DAT-08", "INT-01"])
_lot(M1, ["TSK-DROITS", "TSK-REGLES-LEGALES", "AFF-32", "NF-61", "NF-11"])
_lot(M2, ["POR-04", "POR-05", "SAN-52", "NF-20", "NF-21", "NF-22", "SAN-12", "TSK-SAGA-REPRISE"])
_lot(M3, ["SAN-64", "NF-50"])
_lot(M4, ["NF-34"])

def lot_of(rid):
    return LOT.get(rid) or SVC_LOT[OWNER[rid]]

def parse(raw):
    out = []
    for line in raw.strip().splitlines():
        rid, sec, title, text = line.split("|", 3)
        out.append(dict(id=rid, section=sec, title=title, text=text))
    return out

REQS = parse(REQS_RAW)
EXTRAS = parse(EXTRA_RAW)

def type_of(rid):
    p = rid.split("-")[0]
    return {
        "AFF": "exigence-fonctionnelle", "SAN": "exigence-fonctionnelle", "RIS": "exigence-fonctionnelle",
        "SEC": "exigence-fonctionnelle", "ERG": "exigence-fonctionnelle", "HYG": "exigence-fonctionnelle",
        "PSY": "exigence-fonctionnelle", "PLA": "exigence-fonctionnelle", "PRE": "exigence-fonctionnelle",
        "POR": "exigence-fonctionnelle", "DOC": "exigence-fonctionnelle", "REP": "exigence-fonctionnelle",
        "INT": "interoperabilite", "NF": "exigence-non-fonctionnelle", "ARC": "architecture",
        "CTR": "infrastructure", "DAT": "donnees", "MIG": "migration", "REC": "recette",
        "FOR": "formation", "MAI": "maintenance", "TSK": "tache-technique",
    }[p]
