import { DOCUMENT, inject, Injectable, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { catchError, firstValueFrom, of } from 'rxjs';
import { AVAILABLE_LANGS, DEFAULT_LANG, VeillaLang } from './langs';

const STORAGE_KEY = 'veilla.lang';

/** Nom de chaque langue dans sa propre langue (affiché dans le sélecteur). */
export const LANGUAGE_NAMES: Record<VeillaLang, string> = {
  fr: 'Français',
  nl: 'Nederlands',
  de: 'Deutsch',
  en: 'English',
};

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly transloco = inject(TranslocoService);
  private readonly document = inject(DOCUMENT);
  readonly available = inject(AVAILABLE_LANGS);

  private readonly currentLang = signal<VeillaLang>(DEFAULT_LANG);
  readonly current = this.currentLang.asReadonly();

  /** Détermine la langue initiale (choix mémorisé, puis navigateur, puis FR) et précharge ses libellés. */
  init(): Promise<unknown> {
    const lang = this.detect();
    this.use(lang);
    return firstValueFrom(this.transloco.load(lang).pipe(catchError(() => of(null))));
  }

  use(lang: VeillaLang): void {
    if (!this.isAvailable(lang)) return;
    this.transloco.setActiveLang(lang);
    this.document.documentElement.lang = lang;
    this.currentLang.set(lang);
    try {
      localStorage.setItem(STORAGE_KEY, lang);
    } catch {
      // Stockage indisponible (navigation privée, politique du navigateur) : sans conséquence.
    }
  }

  isAvailable(lang: string): lang is VeillaLang {
    return (this.available as readonly string[]).includes(lang);
  }

  private detect(): VeillaLang {
    let stored: string | null;
    try {
      stored = localStorage.getItem(STORAGE_KEY);
    } catch {
      stored = null;
    }
    if (stored && this.isAvailable(stored)) return stored;

    const browserLangs = this.document.defaultView?.navigator?.languages ?? [];
    for (const candidate of browserLangs) {
      const short = candidate.slice(0, 2).toLowerCase();
      if (this.isAvailable(short)) return short;
    }
    return DEFAULT_LANG;
  }
}
