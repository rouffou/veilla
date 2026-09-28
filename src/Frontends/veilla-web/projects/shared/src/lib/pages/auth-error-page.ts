import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { AuthService } from '../auth/auth.service';

/** Affichée lorsque le fournisseur d'identité est injoignable ou refuse la connexion. */
@Component({
  selector: 'vl-auth-error-page',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('auth.errorTitle') }}</h1>
      <p role="alert">{{ t('auth.errorText') }}</p>
      <button type="button" class="vl-button" (click)="retry()">{{ t('auth.retry') }}</button>
    </ng-container>
  `,
})
export class AuthErrorPage {
  private readonly auth = inject(AuthService);

  protected retry(): void {
    this.auth.login('/');
  }
}
