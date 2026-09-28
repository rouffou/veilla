import { ChangeDetectionStrategy, Component, DOCUMENT, inject, input } from '@angular/core';

/**
 * Lien d'évitement (WCAG 2.4.1) : premier élément focalisable, visible au focus.
 * Le clic est géré pour déplacer réellement le focus sans déclencher le routeur.
 */
@Component({
  selector: 'vl-skip-link',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<a class="vl-skip-link" [href]="'#' + targetId()" (click)="skip($event)"><ng-content /></a>`,
})
export class SkipLink {
  readonly targetId = input.required<string>();
  private readonly document = inject(DOCUMENT);

  protected skip(event: Event): void {
    const target = this.document.getElementById(this.targetId());
    if (!target) return;
    event.preventDefault();
    if (!target.hasAttribute('tabindex')) target.setAttribute('tabindex', '-1');
    target.focus();
    target.scrollIntoView?.();
  }
}
