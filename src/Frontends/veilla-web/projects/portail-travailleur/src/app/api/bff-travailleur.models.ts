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
}

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
  readonly categorie: 'evaluation-sante' | 'autre';
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

export interface Fichier {
  readonly nom: string;
  readonly contenu: Blob;
}
