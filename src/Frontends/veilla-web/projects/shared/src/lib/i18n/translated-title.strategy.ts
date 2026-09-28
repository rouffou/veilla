import { DestroyRef, inject, Injectable } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { BehaviorSubject, combineLatest, switchMap } from 'rxjs';

/**
 * Titre de page traduit (WCAG 2.4.2) : la propriété `title` des routes contient une clé
 * de traduction ; le titre est recalculé à chaque changement de langue.
 */
@Injectable()
export class TranslatedTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly transloco = inject(TranslocoService);
  private readonly pageKey$ = new BehaviorSubject<string | undefined>(undefined);

  constructor() {
    super();
    const subscription = combineLatest([this.pageKey$, this.transloco.langChanges$])
      .pipe(
        switchMap(([pageKey]) =>
          this.transloco.selectTranslate<string[]>(pageKey ? [pageKey, 'app.name'] : ['app.name']),
        ),
      )
      .subscribe((parts) => this.title.setTitle(parts.join(' – ')));
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.pageKey$.next(this.buildTitle(snapshot));
  }
}
