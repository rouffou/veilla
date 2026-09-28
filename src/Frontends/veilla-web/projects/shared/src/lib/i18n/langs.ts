import { InjectionToken } from '@angular/core';

/** Langues prises en charge. FR/NL/DE partout, EN en plus pour les portails (NF-40, POR-14). */
export type VeillaLang = 'fr' | 'nl' | 'de' | 'en';

export const INTERNAL_LANGS: readonly VeillaLang[] = ['fr', 'nl', 'de'];
export const PORTAL_LANGS: readonly VeillaLang[] = ['fr', 'nl', 'de', 'en'];
export const DEFAULT_LANG: VeillaLang = 'fr';

export const AVAILABLE_LANGS = new InjectionToken<readonly VeillaLang[]>('VEILLA_AVAILABLE_LANGS');
