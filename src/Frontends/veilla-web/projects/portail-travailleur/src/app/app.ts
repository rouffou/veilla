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
    { path: '/rendez-vous', labelKey: 'nav.appointments' },
    { path: '/questionnaires', labelKey: 'nav.questionnaires' },
    { path: '/demande', labelKey: 'nav.request' },
    { path: '/documents', labelKey: 'nav.documents' },
  ];
}
