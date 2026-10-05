import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Shell, ShellNavItem } from '@veilla/shared';
import { AffilieSelector } from './affilie/affilie-selector';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Shell, AffilieSelector],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <vl-shell appNameKey="app.name" [navItems]="navItems">
      <app-affilie-selector vlShellHeader />
      <router-outlet />
    </vl-shell>
  `,
})
export class App {
  protected readonly navItems: readonly ShellNavItem[] = [
    { path: '/', labelKey: 'nav.dashboard', exact: true },
    { path: '/affiliation', labelKey: 'nav.affiliate' },
    { path: '/travailleurs', labelKey: 'nav.workers' },
    { path: '/postes', labelKey: 'nav.positions' },
    { path: '/listes-nominatives', labelKey: 'nav.lists' },
    { path: '/propositions', labelKey: 'nav.proposals' },
    { path: '/demandes', labelKey: 'nav.requests' },
    { path: '/decisions', labelKey: 'nav.decisions' },
  ];
}
