import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Shell, ShellNavItem } from '@veilla/shared';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Shell],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <vl-shell appNameKey="app.name" [navItems]="navItems">
      <router-outlet />
    </vl-shell>
  `,
})
export class App {
  protected readonly navItems: readonly ShellNavItem[] = [
    { path: '/', labelKey: 'nav.dashboard', exact: true },
    { path: '/travailleurs', labelKey: 'nav.workers' },
    { path: '/demandes', labelKey: 'nav.requests' },
    { path: '/decisions', labelKey: 'nav.decisions' },
    { path: '/documents', labelKey: 'nav.documents' },
  ];
}
