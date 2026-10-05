/*
 * Modèles du BFF employeur (src/Bff/Employeur, routes /api/v1). Les dates sont des chaînes ISO 8601.
 */

export interface AffilieResume {
  readonly id: string;
  readonly numeroBce: string;
  readonly denomination: string;
  readonly statut: string;
}

export interface MesAffilies {
  readonly affilies: readonly AffilieResume[];
  readonly introuvables: readonly string[];
}

export interface Adresse {
  readonly rue: string;
  readonly numero: string;
  readonly boite: string | null;
  readonly codePostal: string;
  readonly localite: string;
  readonly codePays: string;
}

export interface Site {
  readonly id: string;
  readonly nom: string;
  readonly uniteEtablissement: string;
  readonly numeroUniteEtablissement: string;
  readonly adresse: Adresse;
}

export interface Contact {
  readonly id: string;
  readonly nom: string;
  readonly fonction: string | null;
  readonly role: string;
  readonly email: string | null;
  readonly telephone: string | null;
  readonly valideDu: string;
}

export interface FicheAffilie {
  readonly id: string;
  readonly numeroBce: string;
  readonly denomination: string;
  readonly formeJuridique: string;
  readonly codeNace: string;
  readonly commissionParitaire: string;
  readonly categorieTarifaire: string;
  readonly dateAffiliation: string;
  readonly dateFin: string | null;
  readonly langue: string;
  readonly statut: string;
  readonly sites: readonly Site[];
  readonly contacts: readonly Contact[];
}

export interface Page<T> {
  readonly elements: readonly T[];
  readonly total: number;
  readonly page: number;
  readonly taille: number;
}

/** Travailleur vu par l'employeur : jamais de NISS (DAT-06). */
export interface Travailleur {
  readonly id: string;
  readonly nom: string;
  readonly prenom: string;
  readonly dateNaissance: string;
}

export interface RisquePoste {
  readonly code: string;
  readonly libelle: string;
  readonly categorie: string | null;
  readonly niveauExposition: string;
  readonly exposeDepuis: string;
}

export interface Poste {
  readonly id: string;
  readonly intitule: string;
  readonly description: string | null;
  readonly statut: string;
  readonly expose: boolean;
  readonly risques: readonly RisquePoste[];
}

export interface Risque {
  readonly code: string;
  readonly libelle: string;
  readonly categorie: string;
}

export interface ListeNominative {
  readonly id: string;
  readonly type: string;
  readonly version: number;
  readonly dateReference: string;
  readonly dateGeneration: string;
  readonly nombreLignes: number;
  readonly derniere: boolean;
}

export interface LigneListe {
  readonly personneId: string;
  readonly nom: string | null;
  readonly prenom: string | null;
  readonly posteId: string;
  readonly poste: string | null;
  readonly codesRisques: readonly string[];
  readonly dateDerniereEvaluation: string | null;
  readonly origine: string;
}

export interface ListeNominativeDetail {
  readonly liste: ListeNominative;
  readonly lignes: readonly LigneListe[];
}

export type RaisonIndisponibilite = 'fonctionnalite-a-venir' | 'service-indisponible';

/** Indicateur POR-02 : `valeur` n'est présente que si `disponible` (aucun chiffre fictif). */
export interface Indicateur {
  readonly code: string;
  readonly disponible: boolean;
  readonly valeur: number | null;
  readonly raison: RaisonIndisponibilite | null;
}

export interface TableauDeBord {
  readonly affilieId: string;
  readonly date: string;
  readonly indicateurs: readonly Indicateur[];
}

export type NatureProposition = 'poste-risque' | 'liste-nominative';

export interface Proposition {
  readonly id: string;
  readonly nature: NatureProposition;
  readonly cibleId: string;
  readonly cible: string;
  readonly motif: string;
  readonly statut: string;
  readonly dateProposition: string;
  readonly dateDecision: string | null;
  readonly motifRefus: string | null;
}

export type TypeModification = 'Ajout' | 'Modification' | 'Retrait';
export type NiveauExposition = 'Faible' | 'Moyen' | 'Eleve';

export interface LigneRisqueSaisie {
  readonly type: TypeModification;
  readonly risqueCode: string;
  readonly niveauExposition: NiveauExposition | null;
}

export interface PropositionPosteCorps {
  readonly motif: string;
  readonly valideDu: string;
  readonly lignes: readonly LigneRisqueSaisie[];
}

export interface LigneListeSaisie {
  readonly type: TypeModification;
  readonly personneId: string;
  readonly posteId: string;
}

export interface PropositionListeCorps {
  readonly motif: string;
  readonly lignes: readonly LigneListeSaisie[];
}

export interface PropositionSoumise {
  readonly id: string;
  readonly statut: string;
}

/** Fichier téléchargé (POR-06). */
export interface Fichier {
  readonly nom: string;
  readonly contenu: Blob;
}
