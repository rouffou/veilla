import { ChangeDetectionStrategy, Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { Questionnaire, Reponse } from '../api/bff-travailleur.models';
import { BffTravailleurService, ErreurApi, versErreurApi } from '../api/bff-travailleur.service';
import { chargeable } from '../ui/chargeable';
import { Chargement, ErreurApiMessage } from '../ui/etats';

/**
 * POR-12 : questionnaire de santé à remplir avant l'examen (SAN-22). <b>Écriture seule</b> : le portail liste les modèles et
 * envoie les réponses ; il ne relit ni les réponses déjà enregistrées ni le dossier de santé (§3.3, copie sur demande).
 * Seules les réponses saisies sont envoyées, une fois, sans être conservées par le portail après l'envoi.
 */
@Component({
  selector: 'app-questionnaires-page',
  imports: [TranslocoDirective, Chargement, ErreurApiMessage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('questionnaires.title') }}</h1>
      <p>{{ t('questionnaires.intro') }}</p>
      <p class="app-aide">{{ t('questionnaires.writeOnly') }}</p>
      @if (accuse()) {
        <div class="app-succes" role="status" tabindex="-1" #succes>
          <p>{{ t('questionnaires.success') }}</p>
        </div>
      }
      @let etat = modeles.etat();
      @switch (etat.statut) {
        @case ('chargement') {
          <app-chargement />
        }
        @case ('erreur') {
          <app-erreur-api [erreur]="etat.erreur" [reessayable]="true" (reessayer)="modeles.recharger()" />
        }
        @case ('ok') {
          @if (etat.valeur.length === 0) {
            <p class="vl-empty">{{ t('questionnaires.empty') }}</p>
          } @else if (choisi(); as q) {
            <h2>{{ q.titre }}</h2>
            @if (resume().length > 0) {
              <div class="app-alerte" role="alert">
                <p>{{ t('form.summary') }}</p>
                <ul>
                  @for (e of resume(); track e) {
                    <li>{{ e }}</li>
                  }
                </ul>
              </div>
            }
            @if (erreurEnvoi(); as e) {
              <app-erreur-api [erreur]="e" />
            }
            <form class="app-form" (submit)="$event.preventDefault(); envoyer(q)" novalidate>
              @for (question of q.questions; track question.code) {
                @let champ = 'q-' + question.code;
                @if (question.typeReponse === 'OuiNon') {
                  <fieldset class="app-fieldset">
                    <legend>
                      {{ question.libelle }}
                      @if (question.obligatoire) {
                        <span aria-hidden="true">*</span>
                      }
                    </legend>
                    @for (option of ['OUI', 'NON']; track option) {
                      <label>
                        <input
                          type="radio"
                          [name]="champ"
                          [value]="option"
                          [required]="question.obligatoire"
                          [checked]="valeurs()[question.code] === option"
                          (change)="saisir(question.code, option)"
                        />
                        {{ t('questionnaires.' + (option === 'OUI' ? 'yes' : 'no')) }}
                      </label>
                    }
                  </fieldset>
                } @else {
                  <div class="app-champ">
                    <label [for]="champ">
                      {{ question.libelle }}
                      @if (question.obligatoire) {
                        <span aria-hidden="true">*</span>
                      }
                    </label>
                    @if (question.typeReponse === 'Choix') {
                      <select
                        [id]="champ"
                        class="app-input"
                        [required]="question.obligatoire"
                        (change)="saisir(question.code, $any($event.target).value)"
                      >
                        <option value="">{{ t('form.choose') }}</option>
                        @for (c of question.choix; track c) {
                          <option [value]="c" [selected]="valeurs()[question.code] === c">{{ c }}</option>
                        }
                      </select>
                    } @else {
                      <input
                        [id]="champ"
                        class="app-input"
                        [type]="question.typeReponse === 'Nombre' ? 'number' : 'text'"
                        [required]="question.obligatoire"
                        [value]="valeurs()[question.code] ?? ''"
                        (input)="saisir(question.code, $any($event.target).value)"
                      />
                    }
                  </div>
                }
              }
              <div class="app-actions">
                <button type="submit" class="vl-button" [disabled]="envoi()" [attr.aria-busy]="envoi()">
                  {{ envoi() ? t('questionnaires.submitting') : t('questionnaires.submit') }}
                </button>
                <button type="button" class="vl-button vl-button--secondary" (click)="annuler()">
                  {{ t('questionnaires.back') }}
                </button>
              </div>
            </form>
          } @else {
            <ul class="app-liste">
              @for (m of etat.valeur; track m.code) {
                <li>
                  {{ m.titre }}
                  <button type="button" class="vl-button vl-button--secondary" (click)="ouvrir(m)">
                    {{ t('questionnaires.fill') }}
                    <span class="vl-visually-hidden"> {{ m.titre }}</span>
                  </button>
                </li>
              }
            </ul>
          }
        }
      }
    </ng-container>
  `,
})
export class QuestionnairesPage {
  private readonly api = inject(BffTravailleurService);
  protected readonly modeles = chargeable(() => this.api.questionnaires());

  protected readonly choisi = signal<Questionnaire | null>(null);
  protected readonly valeurs = signal<Readonly<Record<string, string>>>({});
  protected readonly envoi = signal(false);
  protected readonly accuse = signal(false);
  protected readonly erreurEnvoi = signal<ErreurApi | null>(null);
  protected readonly resume = signal<readonly string[]>([]);
  private readonly succes = viewChild<ElementRef<HTMLElement>>('succes');

  protected ouvrir(modele: Questionnaire): void {
    this.accuse.set(false);
    this.valeurs.set({});
    this.resume.set([]);
    this.erreurEnvoi.set(null);
    this.choisi.set(modele);
  }

  protected annuler(): void {
    this.choisi.set(null);
    this.valeurs.set({});
  }

  protected saisir(code: string, valeur: string): void {
    this.valeurs.update((v) => ({ ...v, [code]: valeur }));
  }

  protected envoyer(modele: Questionnaire): void {
    this.erreurEnvoi.set(null);
    const manquantes = modele.questions
      .filter((q) => q.obligatoire && !(this.valeurs()[q.code] ?? '').trim())
      .map((q) => q.libelle);
    this.resume.set(manquantes);
    if (manquantes.length > 0) return;

    const reponses: Reponse[] = modele.questions
      .map((q) => ({ codeQuestion: q.code, valeur: (this.valeurs()[q.code] ?? '').trim() }))
      .filter((r) => r.valeur !== '');
    this.envoi.set(true);
    this.api.remplirQuestionnaire(modele.code, reponses).subscribe({
      next: () => {
        this.envoi.set(false);
        // Les réponses ne sont pas gardées par le portail une fois envoyées (donnée de santé).
        this.annuler();
        this.accuse.set(true);
        queueMicrotask(() => this.succes()?.nativeElement.focus());
      },
      error: (error: unknown) => {
        this.envoi.set(false);
        this.erreurEnvoi.set(versErreurApi(error));
      },
    });
  }
}
