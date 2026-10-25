import { HttpClient, HttpHeaders, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { RUNTIME_CONFIG } from '@veilla/shared';
import { Observable } from 'rxjs';

/** Services aval joignables par le relais du BFF interne (liste fermée côté BFF). */
export type ServiceAval =
  | 'referentiels'
  | 'affilies'
  | 'personnes'
  | 'postes-risques'
  | 'obligations'
  | 'planification'
  | 'surveillance-medicale'
  | 'reintegration'
  | 'documents'
  | 'communications'
  | 'audit'
  | 'integrations';

export type Parametres = Readonly<Record<string, string | number | boolean | null | undefined>>;

export interface OptionsRequete {
  readonly params?: Parametres;
  /** Motif d'accès (en-tête `X-Motif-Acces`), exigé par les routes de la zone médicale. */
  readonly motifAcces?: string;
}

function enParams(parametres?: Parametres): HttpParams {
  let params = new HttpParams();
  for (const [cle, valeur] of Object.entries(parametres ?? {})) {
    if (valeur !== null && valeur !== undefined && valeur !== '') params = params.set(cle, String(valeur));
  }
  return params;
}

function enOptions(options?: OptionsRequete): { params: HttpParams; headers?: HttpHeaders } {
  const params = enParams(options?.params);
  return options?.motifAcces
    ? { params, headers: new HttpHeaders({ 'X-Motif-Acces': options.motifAcces }) }
    : { params };
}

/**
 * Client HTTP du BFF interne (ARC-43). Le jeton est ajouté par `apiAuthInterceptor`.
 * - `service('personnes').get('personnes/123')` → `${apiBaseUrl}/personnes/api/v1/personnes/123` (relais) ;
 * - `ecran('travailleurs/123')` → `${apiBaseUrl}/api/v1/ecrans/travailleurs/123` (écran composite).
 */
@Injectable({ providedIn: 'root' })
export class InterneApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(RUNTIME_CONFIG).apiBaseUrl.replace(/\/+$/, '');

  /** Adresse d'une route d'un service relayé. */
  urlService(service: ServiceAval, chemin: string): string {
    return `${this.base}/${service}/api/v1/${chemin.replace(/^\/+/, '')}`;
  }

  /** Adresse d'un écran composite du BFF. */
  urlEcran(chemin: string): string {
    return `${this.base}/api/v1/ecrans/${chemin.replace(/^\/+/, '')}`;
  }

  service(service: ServiceAval) {
    const url = (chemin: string) => this.urlService(service, chemin);
    return {
      get: <T>(chemin: string, options?: OptionsRequete): Observable<T> =>
        this.http.get<T>(url(chemin), enOptions(options)),
      post: <T>(chemin: string, corps: unknown = {}, options?: OptionsRequete): Observable<T> =>
        this.http.post<T>(url(chemin), corps, enOptions(options)),
      put: <T>(chemin: string, corps: unknown, options?: OptionsRequete): Observable<T> =>
        this.http.put<T>(url(chemin), corps, enOptions(options)),
      delete: <T>(chemin: string, options?: OptionsRequete): Observable<T> =>
        this.http.delete<T>(url(chemin), enOptions(options)),
    };
  }

  ecran<T>(chemin: string, options?: OptionsRequete): Observable<T> {
    return this.http.get<T>(this.urlEcran(chemin), enOptions(options));
  }
}
