import { HttpErrorResponse } from '@angular/common/http';
import { catchError, map, Observable, of, startWith } from 'rxjs';

/** Catégorie d'erreur présentée à l'utilisateur (ProblemDetails RFC 9457 du BFF). */
export type TypeErreur =
  'indisponible' | 'interdit' | 'introuvable' | 'invalide' | 'reseau' | 'inattendue';

export interface ErreurApi {
  readonly type: TypeErreur;
  readonly status: number;
  /** Code fonctionnel renvoyé par le BFF ou le service (ex. `perimetre.interdit`). */
  readonly code: string | null;
  /** Message fonctionnel (en français, issu du service) pour les erreurs de validation. */
  readonly detail: string | null;
}

/** État d'une ressource chargée : en cours, disponible ou en erreur. */
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
