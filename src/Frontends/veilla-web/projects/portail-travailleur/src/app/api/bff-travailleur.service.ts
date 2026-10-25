import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { LanguageService, RUNTIME_CONFIG } from '@veilla/shared';
import { map, Observable } from 'rxjs';
import {
  Accueil,
  Creneau,
  DemandeCopie,
  DocumentPersonnel,
  Employeur,
  ExamenAPlanifier,
  Fichier,
  Questionnaire,
  RaisonAnnulation,
  Reponse,
  RendezVous,
  TypeDemande,
} from './bff-travailleur.models';

/**
 * Client typé du BFF travailleur (ARC-43). Le jeton est ajouté par `apiAuthInterceptor` ; la langue active est transmise
 * pour les libellés des questionnaires. Le travailleur ne lit jamais son dossier de santé par ce canal.
 */
@Injectable({ providedIn: 'root' })
export class BffTravailleurService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(RUNTIME_CONFIG).apiBaseUrl}/api/v1`;
  private readonly langues = inject(LanguageService);

  accueil(): Observable<Accueil> {
    return this.http.get<Accueil>(`${this.base}/accueil`, { params: this.langue() });
  }

  /** POR-11 : mes employeurs (occupations actives), pour réserver sans lien de convocation. */
  employeurs(): Observable<readonly Employeur[]> {
    return this.http.get<readonly Employeur[]>(`${this.base}/employeurs`);
  }

  /** POR-11 : mes examens à planifier. */
  examensAPlanifier(): Observable<readonly ExamenAPlanifier[]> {
    return this.http.get<readonly ExamenAPlanifier[]>(`${this.base}/examens-a-planifier`, { params: this.langue() });
  }

  /** POR-11 : mes rendez-vous. */
  rendezVous(): Observable<readonly RendezVous[]> {
    return this.http.get<readonly RendezVous[]>(`${this.base}/rendez-vous`);
  }

  creneaux(affilieId: string, typeActe: string, du: Date, au: Date): Observable<readonly Creneau[]> {
    const params = new HttpParams()
      .set('affilieId', affilieId)
      .set('typeActe', typeActe)
      .set('du', du.toISOString())
      .set('au', au.toISOString());
    return this.http.get<readonly Creneau[]>(`${this.base}/rendez-vous/creneaux`, { params });
  }

  /** Réservation pour soi-même (jamais rejouée automatiquement par le BFF). */
  reserver(creneauId: string, affilieId: string): Observable<{ readonly id: string }> {
    return this.http.post<{ readonly id: string }>(`${this.base}/rendez-vous`, { creneauId, affilieId });
  }

  /** Annulation avec raison codée et précision libre courte (sans donnée de santé). */
  annuler(rendezVousId: string, raison?: RaisonAnnulation, commentaire?: string): Observable<void> {
    const texte = commentaire?.trim();
    return this.http.post<void>(`${this.base}/rendez-vous/${encodeURIComponent(rendezVousId)}/annulation`, {
      raison: raison ?? null,
      commentaire: texte ? texte : null,
    });
  }

  /** POR-11 : déplacement atomique (un seul appel) ; jamais rejoué automatiquement. */
  deplacer(rendezVousId: string, nouveauCreneauId: string): Observable<RendezVous> {
    return this.http.post<RendezVous>(`${this.base}/rendez-vous/${encodeURIComponent(rendezVousId)}/deplacement`, {
      nouveauCreneauId,
    });
  }

  /** POR-11 : confirmation de présence. */
  confirmer(rendezVousId: string): Observable<RendezVous> {
    return this.http.post<RendezVous>(`${this.base}/rendez-vous/${encodeURIComponent(rendezVousId)}/confirmation`, null);
  }

  /** POR-12 : modèles de questionnaire de santé (écriture seule : aucune réponse déjà enregistrée n'est relue). */
  questionnaires(): Observable<readonly Questionnaire[]> {
    return this.http.get<readonly Questionnaire[]>(`${this.base}/questionnaires`, { params: this.langue() });
  }

  remplirQuestionnaire(code: string, reponses: readonly Reponse[]): Observable<{ readonly id: string }> {
    return this.http.post<{ readonly id: string }>(
      `${this.base}/questionnaires/${encodeURIComponent(code)}/reponses`,
      { reponses },
    );
  }

  /** POR-12 : consultation spontanée ou visite de pré-reprise, sans passer par l'employeur ; ni motif ni texte libre. */
  demander(affilieId: string, type: TypeDemande): Observable<{ readonly id: string }> {
    return this.http.post<{ readonly id: string }>(`${this.base}/demandes`, { affilieId, type });
  }

  /** POR-13 : mes documents publiés. */
  documents(): Observable<readonly DocumentPersonnel[]> {
    return this.http.get<readonly DocumentPersonnel[]>(`${this.base}/documents`);
  }

  /** POR-13, SAN-43 : suivi de mes demandes de copie du dossier de santé (aucun contenu médical). */
  demandesCopie(): Observable<readonly DemandeCopie[]> {
    return this.http.get<readonly DemandeCopie[]>(`${this.base}/demandes-copie-dossier`);
  }

  /** Demande de copie traitée par le médecin du travail ; la copie est ensuite publiée dans « Mes documents ». */
  demanderCopie(): Observable<DemandeCopie> {
    return this.http.post<DemandeCopie>(`${this.base}/demandes-copie-dossier`, null);
  }

  telechargerDocument(id: string): Observable<Fichier> {
    return this.http
      .get(`${this.base}/documents/${encodeURIComponent(id)}/contenu`, {
        responseType: 'blob',
        observe: 'response',
      })
      .pipe(
        map((response) => ({
          nom: nomDeFichier(response.headers.get('Content-Disposition')) ?? 'document.pdf',
          contenu: response.body ?? new Blob([], { type: 'application/pdf' }),
        })),
      );
  }

  private langue(): HttpParams {
    return new HttpParams().set('langue', this.langues.current());
  }
}

/** Extrait le nom de fichier d'un en-tête Content-Disposition (filename* RFC 5987 prioritaire). */
export function nomDeFichier(disposition: string | null): string | null {
  if (!disposition) return null;
  const etendu = /filename\*\s*=\s*(?:UTF-8|utf-8)''([^;]+)/.exec(disposition);
  if (etendu) {
    try {
      return decodeURIComponent(etendu[1].trim());
    } catch {
      return etendu[1].trim();
    }
  }
  const simple = /filename\s*=\s*"?([^";]+)"?/.exec(disposition);
  return simple ? simple[1].trim() : null;
}
