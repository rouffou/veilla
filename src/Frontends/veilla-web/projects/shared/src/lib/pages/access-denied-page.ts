import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

/** Page affichée quand l'utilisateur n'a pas le rôle exigé par une route (voir `roleGuard`). */
@Component({
  selector: 'vl-access-denied-page',
  imports: [TranslocoDirective, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('accessDenied.title') }}</h1>
      <p role="alert">{{ t('accessDenied.text') }}</p>
      <p>
        <a routerLink="/">{{ t('accessDenied.home') }}</a>
      </p>
    </ng-container>
  `,
})
export class AccessDeniedPage {}
