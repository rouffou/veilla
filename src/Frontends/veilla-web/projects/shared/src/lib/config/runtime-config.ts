import { InjectionToken, Provider } from '@angular/core';

/**
 * Configuration d'exécution chargée au démarrage depuis `assets/config.json`.
 *
 * Aucune valeur d'environnement n'est compilée dans le bundle : la même image est
 * promue d'un environnement à l'autre (CTR-05). En conteneur, le fichier est généré
 * au démarrage à partir des variables d'environnement (voir `docker/`).
 */
export interface VeillaRuntimeConfig {
  /** URL de base du BFF de l'application (ex. `https://api.example.be/bff-employeur`). */
  readonly apiBaseUrl: string;
  readonly auth: VeillaAuthConfig;
}

export interface VeillaAuthConfig {
  /** Émetteur OIDC (Keycloak en local, Entra ID pour les internes, fédération CSAM pour les portails). */
  readonly authority: string;
  readonly clientId: string;
  /** Portées demandées, séparées par des espaces. */
  readonly scope: string;
}

export const RUNTIME_CONFIG = new InjectionToken<VeillaRuntimeConfig>('VEILLA_RUNTIME_CONFIG');

export const DEFAULT_RUNTIME_CONFIG_URL = 'assets/config.json';

export class RuntimeConfigError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'RuntimeConfigError';
  }
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

/** Valide la structure du fichier de configuration et renvoie un objet typé. */
export function parseRuntimeConfig(raw: unknown): VeillaRuntimeConfig {
  const missing: string[] = [];
  const root = (raw ?? {}) as Record<string, unknown>;
  const auth = (root['auth'] ?? {}) as Record<string, unknown>;

  if (!isNonEmptyString(root['apiBaseUrl'])) missing.push('apiBaseUrl');
  for (const key of ['authority', 'clientId', 'scope'] as const) {
    if (!isNonEmptyString(auth[key])) missing.push(`auth.${key}`);
  }
  if (missing.length > 0) {
    throw new RuntimeConfigError(`Configuration invalide : ${missing.join(', ')} manquant(s).`);
  }

  return {
    apiBaseUrl: (root['apiBaseUrl'] as string).replace(/\/+$/, ''),
    auth: {
      authority: (auth['authority'] as string).replace(/\/+$/, ''),
      clientId: auth['clientId'] as string,
      scope: auth['scope'] as string,
    },
  };
}

/** Charge la configuration avant l'amorçage d'Angular (appelé depuis `main.ts`). */
export async function loadRuntimeConfig(
  url: string = DEFAULT_RUNTIME_CONFIG_URL,
  fetchFn: typeof fetch = fetch,
): Promise<VeillaRuntimeConfig> {
  const response = await fetchFn(url, { cache: 'no-store', credentials: 'same-origin' });
  if (!response.ok) {
    throw new RuntimeConfigError(`Impossible de charger ${url} (HTTP ${response.status}).`);
  }
  return parseRuntimeConfig(await response.json());
}

export function provideRuntimeConfig(config: VeillaRuntimeConfig): Provider {
  return { provide: RUNTIME_CONFIG, useValue: config };
}

/**
 * Affiche un message d'erreur minimal et accessible lorsque l'amorçage échoue
 * (les traductions ne sont pas encore disponibles : message trilingue statique).
 */
export function renderBootstrapError(error: unknown, doc: Document = document): void {
  console.error(error);
  const main = doc.createElement('main');
  main.setAttribute('role', 'alert');
  main.className = 'vl-bootstrap-error';
  const messages: [string, string][] = [
    ['fr', "L'application n'a pas pu démarrer. Veuillez réessayer plus tard."],
    ['nl', 'De toepassing kon niet starten. Probeer het later opnieuw.'],
    ['de', 'Die Anwendung konnte nicht gestartet werden. Bitte versuchen Sie es später erneut.'],
  ];
  for (const [lang, text] of messages) {
    const p = doc.createElement('p');
    p.lang = lang;
    p.textContent = text;
    main.appendChild(p);
  }
  doc.body.replaceChildren(main);
}
