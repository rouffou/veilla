/**
 * Matrice des rôles internes (noms portés par le claim `roles`) et des fonctions de l'application.
 * Elle reflète les permissions de `RolePermissions` (Security.cs) : l'interface ne montre que ce que le profil
 * peut utiliser, mais seuls les services font foi (un rôle manquant ici ne donne aucun droit, il masque un écran).
 */
export const ROLES = {
  cpmt: 'cpmt',
  cpmtDirigeant: 'cpmt-dirigeant',
  infirmier: 'infirmier',
  assistantMedical: 'assistant-medical',
  conseillerSecurite: 'cp-securite',
  conseillerErgonome: 'cp-ergonome',
  conseillerHygieniste: 'cp-hygieniste',
  cpap: 'cpap',
  cpapDirigeant: 'cpap-dirigeant',
  gestionnaire: 'gestionnaire',
  planificateur: 'planificateur',
  responsableCentre: 'responsable-centre',
  direction: 'direction',
  adminFonctionnel: 'admin-fonctionnel',
  dpo: 'dpo',
} as const;

const R = ROLES;

/** Tous les profils internes (un jeton employeur, sipp ou travailleur n'en porte aucun). */
export const ROLES_INTERNES: readonly string[] = Object.values(ROLES);

/** Profils médicaux : dossier de santé, consultation, vaccinations (dossier-sante:lire). */
export const ROLES_MEDICAUX: readonly string[] = [R.cpmt, R.cpmtDirigeant, R.infirmier];

/** Rôles autorisés par fonction de l'application (routes paresseuses et navigation). */
export const ROLES_PAR_FONCTION = {
  /** Fiche travailleur (personne:lire). */
  travailleurs: [
    R.cpmt, R.cpmtDirigeant, R.infirmier, R.assistantMedical, R.cpap, R.cpapDirigeant, R.conseillerSecurite,
    R.conseillerErgonome, R.conseillerHygieniste, R.gestionnaire, R.planificateur,
  ],
  /** Fiche affilié (affilie:lire). */
  affilies: [
    R.cpmt, R.cpmtDirigeant, R.infirmier, R.cpap, R.cpapDirigeant, R.conseillerSecurite, R.conseillerErgonome,
    R.conseillerHygieniste, R.gestionnaire, R.planificateur, R.responsableCentre, R.direction, R.adminFonctionnel,
  ],
  /** Agenda, rendez-vous, salle d'attente (planification:lire). */
  planification: [R.planificateur, R.responsableCentre, R.assistantMedical, R.cpmt, R.cpmtDirigeant, R.infirmier],
  /** Consultation, examen, décision (dossier-sante:lire). */
  consultation: ROLES_MEDICAUX,
  /** Vaccinations (dossier-sante:lire). */
  vaccinations: ROLES_MEDICAUX,
  /** Réintégration : dossier (CPMT, infirmier) et suivi sans donnée médicale (gestionnaire). */
  reintegration: [R.cpmt, R.cpmtDirigeant, R.infirmier, R.gestionnaire],
  /** File de travail : propositions poste-risque, demandes de copie, décisions en brouillon. */
  travail: ROLES_MEDICAUX,
  /** Administration : référentiels, modèles, intégrations, audit. */
  admin: [R.adminFonctionnel, R.gestionnaire, R.dpo, R.cpmtDirigeant, R.cpapDirigeant],
} as const satisfies Record<string, readonly string[]>;

export type Fonction = keyof typeof ROLES_PAR_FONCTION;
