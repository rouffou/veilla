import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { LanguageService, RUNTIME_CONFIG } from '@veilla/shared';
import { catchError, map, Observable, of, startWith } from 'rxjs';
import {
  Accueil,
  Creneau,
  DocumentPersonnel,
  Fichier,
  Questionnaire,
  Reponse,
  RendezVous,
  TypeDemande,
} from './bff-travailleur.models';

/** Catégorie d'erreur présentée à l'utilisateur (ProblemDetails du BFF). */
export type TypeErreur =
  'indisponible' | 'interdit' | 'introuvable' | 'invalide' | 'reseau' | 'inattendue';

export interface ErreurApi {
  readonly type: TypeErreur;
  readonly status: number;
  /** Code fonctionnel renvoyé par le BFF ou le service (ex. `rendez-vous.annulation-tardive`). */
  readonly code: string | null;
  /** Message fonctionnel (en français, issu du service). */
  readonly detail: string | null;
}

export type Etat<T> =
  | { readonly statut: 'chargement' }
  | { readonly statut: 'ok'; readonly valeur: T }
  | { readonly statut: 'erreur'; readonly erreur: ErreurApi };

export const CHARGEMENT: Etat<never> = { statut: 'chargement' };

/** Traduit une réponse d'erreur HTTP (ProblemDetails RFC 9457) en erreur d'affichage. */
export function versErreurApi(error: unknown): ErreurApi {
  if (!(error instanceof HttpErrorResponse)) {
    return { type: 'inattendue', status: 0, code: null, detail: null };
  }
  const body = (error.error ?? {}) as Record<string, unknown>;
  const code = typeof body['code'] === 'string' ? body['code'] : null;
  const detail = typeof body['detail'] === 'string' ? body['detail'] : null;
  const type: TypeErreur =
    error.status === 0
      ? 'reseau'
      : error.status === 503 || error.status === 502 || error.status === 504
        ? 'indisponible'
        : error.status === 401 || error.status === 403
          ? 'interdit'
          : error.status === 404
            ? 'introuvable'
            : error.status === 400 || error.status === 409 || error.status === 422
              ? 'invalide'
              : 'inattendue';
  return { type, status: error.status, code, detail };
}

/** Enveloppe un appel dans un état chargement → valeur | erreur, pour les signaux des écrans. */
export function charger<T>(source: Observable<T>): Observable<Etat<T>> {
  return source.pipe(
    map((valeur): Etat<T> => ({ statut: 'ok', valeur })),
    catchError((error: unknown) => of<Etat<T>>({ statut: 'erreur', erreur: versErreurApi(error) })),
    startWith<Etat<T>>(CHARGEMENT),
  );
}

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

  annuler(rendezVousId: string): Observable<void> {
    return this.http.post<void>(`${this.base}/rendez-vous/${encodeURIComponent(rendezVousId)}/annulation`, null);
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
