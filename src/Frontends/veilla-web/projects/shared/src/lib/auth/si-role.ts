import { Directive, effect, inject, input, TemplateRef, ViewContainerRef } from '@angular/core';
import { AuthService } from './auth.service';

/**
 * Directive structurelle : affiche le gabarit si l'utilisateur possède au moins un des rôles donnés.
 * `*vlSiRole="'cpmt'"` ou `*vlSiRole="['cpmt', 'infirmier']"`. Sans rôle, le gabarit est affiché.
 */
@Directive({ selector: '[vlSiRole]' })
export class SiRole {
  readonly vlSiRole = input.required<string | readonly string[]>();

  private readonly auth = inject(AuthService);
  private readonly template = inject(TemplateRef<unknown>);
  private readonly container = inject(ViewContainerRef);
  private affiche = false;

  constructor() {
    effect(() => {
      const exiges = this.vlSiRole();
      const autorise = this.auth.hasAnyRole(...(typeof exiges === 'string' ? [exiges] : exiges));
      if (autorise && !this.affiche) {
        this.container.createEmbeddedView(this.template);
      } else if (!autorise && this.affiche) {
        this.container.clear();
      }
      this.affiche = autorise;
    });
  }
}
