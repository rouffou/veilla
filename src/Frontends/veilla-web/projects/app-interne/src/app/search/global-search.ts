import { ChangeDetectionStrategy, Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

/** Champ de recherche globale de l'en-tête (NF-51). */
@Component({
  selector: 'app-global-search',
  imports: [TranslocoDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="app-search" role="search" (submit)="submit($event)" *transloco="let t">
      <label class="vl-visually-hidden" for="app-global-search">{{ t('search.label') }}</label>
      <input
        #input
        id="app-global-search"
        class="app-search__input"
        type="search"
        name="q"
        autocomplete="off"
        aria-keyshortcuts="Control+K /"
        [value]="query()"
        [attr.placeholder]="t('search.placeholder')"
        (input)="query.set(input.value)"
      />
      <button type="submit" class="vl-button vl-button--inverse">{{ t('search.submit') }}</button>
    </form>
  `,
})
export class GlobalSearch {
  private readonly router = inject(Router);
  private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('input');
  protected readonly query = signal('');

  focus(): void {
    const element = this.input().nativeElement;
    element.focus();
    element.select();
  }

  protected submit(event: Event): void {
    event.preventDefault();
    const q = this.query().trim();
    void this.router.navigate(['/recherche'], { queryParams: q ? { q } : {} });
  }
}
