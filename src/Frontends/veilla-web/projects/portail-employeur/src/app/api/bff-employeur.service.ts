import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { LanguageService, RUNTIME_CONFIG } from '@veilla/shared';
import { catchError, map, Observable, of, startWith } from 'rxjs';
import {
  Fichier,
  FicheAffilie,
  ListeNominative,
  ListeNominativeDetail,
  MesAffilies,
  Page,
  Poste,
  Proposition,
  PropositionListeCorps,
  PropositionPosteCorps,
  PropositionSoumise,
  Risque,
  TableauDeBord,
  Travailleur,
} from './bff-employeur.models';

/** Catégorie d'erreur présentée à l'utilisateur (ProblemDetails du BFF). */
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
 * Client typé du BFF employeur (ARC-43). Le jeton est ajouté par `apiAuthInterceptor` ;
 * la langue active est transmise pour les libellés (risques) et l'export CSV.
 */
@Injectable({ providedIn: 'root' })
export class BffEmployeurService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(RUNTIME_CONFIG).apiBaseUrl}/api/v1`;
  private readonly langues = inject(LanguageService);

  mesAffilies(): Observable<MesAffilies> {
    return this.http.get<MesAffilies>(`${this.base}/affilies`);
  }

  fiche(affilieId: string): Observable<FicheAffilie> {
    return this.http.get<FicheAffilie>(this.affilie(affilieId));
  }

  tableauDeBord(affilieId: string): Observable<TableauDeBord> {
    return this.http.get<TableauDeBord>(`${this.affilie(affilieId)}/tableau-de-bord`);
  }

  travailleurs(
    affilieId: string,
    recherche: string,
    page: number,
    taille: number,
  ): Observable<Page<Travailleur>> {
    let params = new HttpParams().set('page', page).set('taille', taille);
    if (recherche.trim()) {
      params = params.set('recherche', recherche.trim());
    }
    return this.http.get<Page<Travailleur>>(`${this.affilie(affilieId)}/travailleurs`, { params });
  }

  postes(affilieId: string): Observable<readonly Poste[]> {
    return this.http.get<readonly Poste[]>(`${this.affilie(affilieId)}/postes`, {
      params: this.langue(),
    });
  }

  risques(): Observable<readonly Risque[]> {
    return this.http.get<readonly Risque[]>(`${this.base}/risques`, { params: this.langue() });
  }

  listesNominatives(affilieId: string): Observable<readonly ListeNominative[]> {
    return this.http.get<readonly ListeNominative[]>(
      `${this.affilie(affilieId)}/listes-nominatives`,
    );
  }

  listeNominative(affilieId: string, listeId: string): Observable<ListeNominativeDetail> {
    return this.http.get<ListeNominativeDetail>(
      `${this.affilie(affilieId)}/listes-nominatives/${encodeURIComponent(listeId)}`,
    );
  }

  /** POR-06 : export CSV produit par le BFF ; le nom du fichier vient de Content-Disposition. */
  telechargerListeCsv(affilieId: string, listeId: string): Observable<Fichier> {
    return this.http
      .get(`${this.affilie(affilieId)}/listes-nominatives/${encodeURIComponent(listeId)}/csv`, {
        params: this.langue(),
        responseType: 'blob',
        observe: 'response',
      })
      .pipe(
        map((response) => ({
          nom: nomDeFichier(response.headers.get('Content-Disposition')) ?? 'liste-nominative.csv',
          contenu: response.body ?? new Blob([], { type: 'text/csv' }),
        })),
      );
  }

  propositions(affilieId: string): Observable<readonly Proposition[]> {
    return this.http.get<readonly Proposition[]>(`${this.affilie(affilieId)}/propositions`);
  }

  proposerPoste(
    affilieId: string,
    posteId: string,
    corps: PropositionPosteCorps,
  ): Observable<PropositionSoumise> {
    return this.http.post<PropositionSoumise>(
      `${this.affilie(affilieId)}/postes/${encodeURIComponent(posteId)}/propositions`,
      corps,
    );
  }

  proposerListe(
    affilieId: string,
    listeId: string,
    corps: PropositionListeCorps,
  ): Observable<PropositionSoumise> {
    return this.http.post<PropositionSoumise>(
      `${this.affilie(affilieId)}/listes-nominatives/${encodeURIComponent(listeId)}/propositions`,
      corps,
    );
  }

  private affilie(affilieId: string): string {
    return `${this.base}/affilies/${encodeURIComponent(affilieId)}`;
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
