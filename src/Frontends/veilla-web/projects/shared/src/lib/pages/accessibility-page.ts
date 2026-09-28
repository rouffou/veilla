import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';

/** Déclaration d'accessibilité (EN 301 549 / WCAG 2.1 AA, NF-50). Contenu à compléter après audit. */
@Component({
  selector: 'vl-accessibility-page',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('a11y.title') }}</h1>
      <p>{{ t('a11y.target') }}</p>
      <p>{{ t('a11y.status') }}</p>
      <h2>{{ t('a11y.keyboardTitle') }}</h2>
      <p>{{ t('a11y.keyboard') }}</p>
    </ng-container>
  `,
})
export class AccessibilityPage {}
