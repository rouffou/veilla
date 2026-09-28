import { ChangeDetectionStrategy, Component, inject, viewChild } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Shell, ShellNavItem } from '@veilla/shared';
import { GlobalSearch } from './search/global-search';
import { KeyboardShortcuts } from './shortcuts/keyboard-shortcuts';
import { ShortcutsHelp } from './shortcuts/shortcuts-help';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Shell, GlobalSearch, ShortcutsHelp],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown)': 'onKeydown($event)' },
  template: `
    <vl-shell appNameKey="app.name" [navItems]="navItems">
      <div vlShellHeader class="app-header-tools">
        <app-global-search />
        <app-shortcuts-help />
      </div>
      <router-outlet />
    </vl-shell>
  `,
})
export class App {
  protected readonly navItems: readonly ShellNavItem[] = [
    { path: '/', labelKey: 'nav.dashboard', exact: true },
    { path: '/recherche', labelKey: 'nav.search' },
  ];

  private readonly shortcuts = inject(KeyboardShortcuts);
  private readonly search = viewChild.required(GlobalSearch);
  private readonly help = viewChild.required(ShortcutsHelp);

  protected onKeydown(event: KeyboardEvent): void {
    switch (this.shortcuts.resolve(event)) {
      case 'focus-search':
        event.preventDefault();
        this.search().focus();
        break;
      case 'show-help':
        event.preventDefault();
        this.help().open();
        break;
      default:
        break;
    }
  }
}
