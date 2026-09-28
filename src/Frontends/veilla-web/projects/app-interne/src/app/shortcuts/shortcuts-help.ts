import { ChangeDetectionStrategy, Component, ElementRef, inject, viewChild } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { KeyboardShortcuts } from './keyboard-shortcuts';

/** Aide des raccourcis clavier (boîte de dialogue modale native, fermeture par Échap). */
@Component({
  selector: 'app-shortcuts-help',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <button
        type="button"
        class="vl-button vl-button--inverse"
        aria-haspopup="dialog"
        aria-keyshortcuts="Shift+?"
        (click)="open()"
      >
        {{ t('shortcuts.open') }}
      </button>
      <dialog #dialog class="app-dialog" aria-labelledby="app-shortcuts-title">
        <h2 id="app-shortcuts-title">{{ t('shortcuts.title') }}</h2>
        <dl class="app-shortcuts">
          <dt><kbd>Ctrl</kbd> + <kbd>K</kbd></dt>
          <dd>{{ t('shortcuts.focusSearch') }}</dd>
          <dt><kbd>/</kbd></dt>
          <dd>{{ t('shortcuts.focusSearch') }} ({{ t('shortcuts.singleKey') }})</dd>
          <dt><kbd>?</kbd></dt>
          <dd>{{ t('shortcuts.help') }} ({{ t('shortcuts.singleKey') }})</dd>
          <dt><kbd>{{ t('shortcuts.escapeKey') }}</kbd></dt>
          <dd>{{ t('shortcuts.close') }}</dd>
        </dl>
        <p>
          <label>
            <input
              type="checkbox"
              [checked]="shortcuts.singleKeyEnabled()"
              (change)="toggle($event)"
            />
            {{ t('shortcuts.enableSingleKey') }}
          </label>
        </p>
        <button type="button" class="vl-button" (click)="close()">{{ t('shortcuts.close') }}</button>
      </dialog>
    </ng-container>
  `,
})
export class ShortcutsHelp {
  protected readonly shortcuts = inject(KeyboardShortcuts);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  open(): void {
    const dialog = this.dialog().nativeElement;
    if (dialog.open) return;
    if (typeof dialog.showModal === 'function') {
      dialog.showModal();
    } else {
      dialog.setAttribute('open', '');
    }
  }

  close(): void {
    const dialog = this.dialog().nativeElement;
    if (typeof dialog.close === 'function') {
      dialog.close();
    } else {
      dialog.removeAttribute('open');
    }
  }

  protected toggle(event: Event): void {
    this.shortcuts.setSingleKeyEnabled((event.target as HTMLInputElement).checked);
  }
}
