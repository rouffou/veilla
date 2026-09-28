import { Injectable, signal } from '@angular/core';

export type ShortcutAction = 'focus-search' | 'show-help';

const SINGLE_KEY_STORAGE = 'veilla.shortcuts.singleKey';

function isEditable(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  const tag = target.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || target.isContentEditable;
}

/**
 * Associe un événement clavier à une action (NF-51).
 * - Ctrl+K / ⌘+K : recherche globale (toujours actif).
 * - « / » : recherche globale, « ? » : aide — raccourcis à une touche, désactivables
 *   (WCAG 2.1.4) et ignorés pendant la saisie dans un champ.
 */
export function resolveShortcut(event: KeyboardEvent, singleKeyEnabled: boolean): ShortcutAction | null {
  if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === 'k') {
    return 'focus-search';
  }
  if (!singleKeyEnabled || event.ctrlKey || event.metaKey || event.altKey || isEditable(event.target)) {
    return null;
  }
  if (event.key === '/') return 'focus-search';
  if (event.key === '?') return 'show-help';
  return null;
}

@Injectable({ providedIn: 'root' })
export class KeyboardShortcuts {
  private readonly singleKey = signal(this.readPreference());
  readonly singleKeyEnabled = this.singleKey.asReadonly();

  setSingleKeyEnabled(enabled: boolean): void {
    this.singleKey.set(enabled);
    try {
      localStorage.setItem(SINGLE_KEY_STORAGE, String(enabled));
    } catch {
      // Préférence non mémorisée : sans conséquence.
    }
  }

  resolve(event: KeyboardEvent): ShortcutAction | null {
    return resolveShortcut(event, this.singleKey());
  }

  private readPreference(): boolean {
    try {
      return localStorage.getItem(SINGLE_KEY_STORAGE) !== 'false';
    } catch {
      return true;
    }
  }
}
