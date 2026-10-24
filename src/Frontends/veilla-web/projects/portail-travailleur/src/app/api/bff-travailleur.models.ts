/*
 * Modèles du BFF travailleur (src/Bff/Travailleur, ARC-43). Aucun identifiant de personne n'est échangé :
 * le BFF le lit dans le jeton (claim personne_id).
 */

export interface RendezVous {
  readonly id: string;
  readonly affilieId: string;
  readonly lieuId: string;
  readonly debut: string;
  readonly fin: string;
  readonly typeActe: string;
  readonly statut: string;
  readonly motifAnnulation: string | null;
  /** POR-11 : date de confirmation de présence (absente tant que non confirmée). */
  readonly presenceConfirmeeLe?: string | null;
  readonly nombreDeplacements?: number;
}

/**
 * POR-11 : employeur du travailleur (occupation active). La dénomination et la langue viennent d'Affiliés (permission
 * `affilie:lire-employeurs`) ; `null` si le service est indisponible ou ne connaît pas l'affilié.
 */
export interface Employeur {
  readonly affilieId: string;
  readonly denomination: string | null;
  readonly langue: string | null;
  readonly typeTravailleur: string;
  readonly dateDebut: string;
  readonly dateFin: string | null;
}

/** POR-11 : examen à planifier de la personne connectée (type, échéances, statut ; jamais de contenu médical). */
export interface ExamenAPlanifier {
  readonly obligationId: string;
  readonly affilieId: string;
  readonly type: string;
  readonly libelle: string;
  readonly dateDue: string;
  readonly dateLimite: string | null;
  readonly statut: string;
  readonly enRetard: boolean;
}

/** POR-11 : raisons codées d'annulation (liste fermée, jamais de motif médical). */
export const RAISONS_ANNULATION = [
  'ConflitHoraire',
  'ObligationProfessionnelle',
  'ProblemeTransport',
  'ChangementSituation',
  'ExamenDejaEffectue',
  'Autre',
] as const;

export type RaisonAnnulation = (typeof RAISONS_ANNULATION)[number];

/** Longueur maximale du texte libre d'annulation (alignée sur le service Planification). */
export const LONGUEUR_MAX_COMMENTAIRE = 200;

export interface Creneau {
  readonly id: string;
  readonly lieuId: string;
  readonly debut: string;
  readonly fin: string;
  readonly typeActe: string;
}

export interface Question {
  readonly code: string;
  readonly libelle: string;
  readonly typeReponse: 'OuiNon' | 'Texte' | 'Nombre' | 'Choix';
  readonly obligatoire: boolean;
  readonly choix: readonly string[];
}

export interface Questionnaire {
  readonly code: string;
  readonly version: number;
  readonly titre: string;
  readonly questions: readonly Question[];
}

export interface Reponse {
  readonly codeQuestion: string;
  readonly valeur: string;
}

export interface DocumentPersonnel {
  readonly id: string;
  readonly categorie: 'evaluation-sante' | 'carnet-vaccination' | 'copie-dossier' | 'autre';
  readonly codeModele: string;
  readonly langue: string;
  readonly date: string;
  readonly format: string;
  readonly taille: number;
}

export interface Accueil {
  readonly personneId: string;
  readonly prochainsRendezVous: readonly RendezVous[] | null;
  readonly documentsRecents: readonly DocumentPersonnel[] | null;
  readonly questionnairesDisponibles: number | null;
  readonly indisponibles: readonly string[];
}

/** Types de demande que le travailleur peut adresser sans passer par son employeur (POR-12). */
export const TYPES_DEMANDE = ['CONSULTATION_SPONTANEE', 'VISITE_PRE_REPRISE'] as const;
export type TypeDemande = (typeof TYPES_DEMANDE)[number];

/** Types d'acte proposés à la réservation en ligne (codes partagés des types d'examen, §5.1). */
export const TYPES_ACTE = [
  'EVALUATION_PREALABLE',
  'EVALUATION_PERIODIQUE',
  'EXAMEN_REPRISE',
  'VISITE_PRE_REPRISE',
  'CONSULTATION_SPONTANEE',
] as const;

/**
 * POR-13, SAN-43 : suivi d'une demande de copie du dossier de santé. Aucun contenu médical : la copie est un document
 * publié (`documentId`), téléchargé comme les autres documents.
 */
export interface DemandeCopie {
  readonly id: string;
  readonly deposeeLe: string;
  readonly dateLimite: string;
  readonly statut: 'Deposee' | 'Validee' | 'Publiee' | 'Refusee';
  readonly enRetard: boolean;
  readonly motifRefus: 'AucunDossier' | 'IdentiteNonConfirmee' | null;
  readonly documentId: string | null;
  readonly publieeLe: string | null;
}

export interface Fichier {
  readonly nom: string;
  readonly contenu: Blob;
}
