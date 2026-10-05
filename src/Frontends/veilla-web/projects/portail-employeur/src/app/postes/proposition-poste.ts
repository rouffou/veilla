import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import {
  FormArray,
  FormControl,
  FormGroup,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageService } from '@veilla/shared';
import { AffilieContext } from '../affilie/affilie-context';
import { EtatAffilie } from '../affilie/affilie-selector';
import {
  NiveauExposition,
  PropositionSoumise,
  Risque,
  TypeModification,
} from '../api/bff-employeur.models';
import {
  BffEmployeurService,
  charger,
  CHARGEMENT,
  ErreurApi,
  Etat,
  versErreurApi,
} from '../api/bff-employeur.service';
import { switchMap } from 'rxjs';
import { Chargement, ErreurApiMessage } from '../ui/etats';
import { parAffilie } from '../ui/par-affilie';

export const TYPES_MODIFICATION: readonly TypeModification[] = ['Ajout', 'Modification', 'Retrait'];
export const NIVEAUX: readonly NiveauExposition[] = ['Faible', 'Moyen', 'Eleve'];

type LigneForm = FormGroup<{
  type: FormControl<TypeModification>;
  risqueCode: FormControl<string>;
  niveauExposition: FormControl<NiveauExposition | ''>;
}>;

/** Niveau d'exposition obligatoire sauf pour un retrait. */
function niveauRequis(groupe: LigneForm): { niveauRequis: true } | null {
  const { type, niveauExposition } = groupe.getRawValue();
  return type !== 'Retrait' && !niveauExposition ? { niveauRequis: true } : null;
}

/**
 * POR-03 : proposition de modification du profil de risques d'un poste (AFF-14, AFF-31). Elle n'a aucun effet
 * avant la validation du CPMT ; le portail affiche l'accusé de soumission puis l'état dans « Propositions ».
 * Erreurs liées aux champs (aria-invalid, aria-describedby) et résumé annoncé à la soumission (WCAG 3.3.1, 3.3.3).
 */
@Component({
  selector: 'app-proposition-poste',
  imports: [
    TranslocoDirective,
    ReactiveFormsModule,
    RouterLink,
    EtatAffilie,
    Chargement,
    ErreurApiMessage,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('proposal.title') }}</h1>
      <p>
        <a routerLink="/postes">{{ t('proposal.back') }}</a>
      </p>
      <app-etat-affilie />
      @if (contexte.affilieId()) {
        @let etatPostes = postes.etat();
        @if (etatPostes.statut === 'chargement') {
          <app-chargement />
        } @else if (etatPostes.statut === 'erreur') {
          <app-erreur-api
            [erreur]="etatPostes.erreur"
            [reessayable]="true"
            (reessayer)="postes.recharger()"
          />
        } @else if (!poste()) {
          <p class="vl-empty" role="alert">{{ t('errors.introuvable') }}</p>
        } @else {
          <p>{{ t('proposal.intro', { poste: poste()!.intitule }) }}</p>

          @if (soumise(); as s) {
            <div class="app-succes" role="status" tabindex="-1" #succes>
              <p>{{ t('proposal.success') }}</p>
              <p>
                {{ t('proposal.reference', { id: s.id }) }} —
                {{ t('enums.statutProposition.' + s.statut) }}
              </p>
              <p>
                <a routerLink="/propositions">{{ t('proposal.follow') }}</a>
              </p>
            </div>
          } @else {
            @if (resume().length > 0) {
              <div class="app-alerte" role="alert">
                <p>{{ t('form.summary') }}</p>
                <ul>
                  @for (e of resume(); track e) {
                    <li>{{ t(e) }}</li>
                  }
                </ul>
              </div>
            }
            @if (erreurEnvoi(); as e) {
              <app-erreur-api [erreur]="e" />
            }

            <form [formGroup]="formulaire" (ngSubmit)="soumettre()" novalidate class="app-form">
              <div class="app-champ">
                <label for="proposition-motif"
                  >{{ t('proposal.reason') }} <span aria-hidden="true">*</span></label
                >
                <textarea
                  id="proposition-motif"
                  formControlName="motif"
                  rows="4"
                  maxlength="2000"
                  required
                  class="app-input"
                  [attr.aria-invalid]="invalide('motif')"
                  [attr.aria-describedby]="
                    invalide('motif')
                      ? 'proposition-motif-aide proposition-motif-erreur'
                      : 'proposition-motif-aide'
                  "
                ></textarea>
                <p id="proposition-motif-aide" class="app-aide">{{ t('proposal.reasonHint') }}</p>
                @if (invalide('motif')) {
                  <p id="proposition-motif-erreur" class="app-erreur">{{ t('form.required') }}</p>
                }
              </div>

              <div class="app-champ">
                <label for="proposition-date"
                  >{{ t('proposal.effectiveDate') }} <span aria-hidden="true">*</span></label
                >
                <input
                  id="proposition-date"
                  type="date"
                  formControlName="valideDu"
                  required
                  class="app-input"
                  [attr.aria-invalid]="invalide('valideDu')"
                  [attr.aria-describedby]="invalide('valideDu') ? 'proposition-date-erreur' : null"
                />
                @if (invalide('valideDu')) {
                  <p id="proposition-date-erreur" class="app-erreur">{{ t('form.required') }}</p>
                }
              </div>

              <fieldset formArrayName="lignes" class="app-fieldset">
                <legend>{{ t('proposal.lines') }}</legend>
                @let etatRisques = risques.etat();
                @if (etatRisques.statut === 'erreur') {
                  <app-erreur-api [erreur]="etatRisques.erreur" />
                }
                @for (ligne of lignes.controls; track ligne; let i = $index) {
                  <div
                    class="app-ligne"
                    [formGroupName]="i"
                    role="group"
                    [attr.aria-label]="t('proposal.line', { n: i + 1 })"
                  >
                    <div class="app-champ">
                      <label [for]="'ligne-type-' + i">{{ t('proposal.lineType') }}</label>
                      <select [id]="'ligne-type-' + i" formControlName="type" class="app-input">
                        @for (type of types; track type) {
                          <option [value]="type">{{ t('enums.typeModification.' + type) }}</option>
                        }
                      </select>
                    </div>
                    <div class="app-champ">
                      <label [for]="'ligne-risque-' + i"
                        >{{ t('proposal.risk') }} <span aria-hidden="true">*</span></label
                      >
                      <select
                        [id]="'ligne-risque-' + i"
                        formControlName="risqueCode"
                        required
                        class="app-input"
                        [attr.aria-invalid]="ligneInvalide(i, 'risqueCode')"
                        [attr.aria-describedby]="
                          ligneInvalide(i, 'risqueCode') ? 'ligne-risque-erreur-' + i : null
                        "
                      >
                        <option value="">{{ t('form.choose') }}</option>
                        @if (etatRisques.statut === 'ok') {
                          @for (r of etatRisques.valeur; track r.code) {
                            <option [value]="r.code">{{ r.libelle }} ({{ r.code }})</option>
                          }
                        }
                      </select>
                      @if (ligneInvalide(i, 'risqueCode')) {
                        <p [id]="'ligne-risque-erreur-' + i" class="app-erreur">
                          {{ t('form.required') }}
                        </p>
                      }
                    </div>
                    <div class="app-champ">
                      <label [for]="'ligne-niveau-' + i">{{ t('proposal.exposureLevel') }}</label>
                      <select
                        [id]="'ligne-niveau-' + i"
                        formControlName="niveauExposition"
                        class="app-input"
                        [attr.aria-invalid]="niveauManquant(i)"
                        [attr.aria-describedby]="
                          niveauManquant(i) ? 'ligne-niveau-erreur-' + i : null
                        "
                      >
                        <option value="">{{ t('form.choose') }}</option>
                        @for (n of niveaux; track n) {
                          <option [value]="n">{{ t('enums.niveauExposition.' + n) }}</option>
                        }
                      </select>
                      @if (niveauManquant(i)) {
                        <p [id]="'ligne-niveau-erreur-' + i" class="app-erreur">
                          {{ t('proposal.levelRequired') }}
                        </p>
                      }
                    </div>
                    @if (lignes.length > 1) {
                      <button
                        type="button"
                        class="vl-button vl-button--secondary"
                        (click)="retirerLigne(i)"
                      >
                        {{ t('proposal.removeLine')
                        }}<span class="vl-visually-hidden"> {{ i + 1 }}</span>
                      </button>
                    }
                  </div>
                }
                <button
                  type="button"
                  class="vl-button vl-button--secondary"
                  (click)="ajouterLigne()"
                >
                  {{ t('proposal.addLine') }}
                </button>
              </fieldset>

              <button
                type="submit"
                class="vl-button"
                [disabled]="envoi()"
                [attr.aria-busy]="envoi()"
              >
                {{ envoi() ? t('proposal.submitting') : t('proposal.submit') }}
              </button>
            </form>
          }
        }
      }
    </ng-container>
  `,
})
export class PropositionPoste {
  readonly posteId = input.required<string>();

  protected readonly contexte = inject(AffilieContext);
  private readonly api = inject(BffEmployeurService);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly langue = inject(LanguageService).current;

  protected readonly types = TYPES_MODIFICATION;
  protected readonly niveaux = NIVEAUX;

  protected readonly postes = parAffilie((id) => this.api.postes(id), this.langue);
  protected readonly risques = {
    etat: toSignal(toObservable(this.langue).pipe(switchMap(() => charger(this.api.risques()))), {
      initialValue: CHARGEMENT as Etat<readonly Risque[]>,
    }),
  };
  protected readonly poste = computed(() => {
    const etat = this.postes.etat();
    return etat.statut === 'ok' ? (etat.valeur.find((p) => p.id === this.posteId()) ?? null) : null;
  });

  protected readonly formulaire = this.fb.group({
    motif: this.fb.control('', [Validators.required, Validators.maxLength(2000)]),
    valideDu: this.fb.control(aujourdhui(), Validators.required),
    lignes: this.fb.array<LigneForm>([this.nouvelleLigne()]),
  });

  protected readonly envoi = signal(false);
  protected readonly tente = signal(false);
  protected readonly soumise = signal<PropositionSoumise | null>(null);
  protected readonly erreurEnvoi = signal<ErreurApi | null>(null);
  protected readonly resume = signal<readonly string[]>([]);
  private readonly succes = viewChild<ElementRef<HTMLElement>>('succes');

  protected get lignes(): FormArray<LigneForm> {
    return this.formulaire.controls.lignes;
  }

  protected ajouterLigne(): void {
    this.lignes.push(this.nouvelleLigne());
  }

  protected retirerLigne(index: number): void {
    this.lignes.removeAt(index);
  }

  protected invalide(champ: 'motif' | 'valideDu'): boolean {
    const control = this.formulaire.controls[champ];
    return control.invalid && (control.touched || this.tente());
  }

  protected ligneInvalide(index: number, champ: 'risqueCode'): boolean {
    const control = this.lignes.at(index).controls[champ];
    return control.invalid && (control.touched || this.tente());
  }

  protected niveauManquant(index: number): boolean {
    return this.lignes.at(index).hasError('niveauRequis') && this.tente();
  }

  protected soumettre(): void {
    this.tente.set(true);
    this.erreurEnvoi.set(null);
    this.formulaire.markAllAsTouched();
    const erreurs: string[] = [];
    if (this.formulaire.controls.motif.invalid) erreurs.push('proposal.reasonMissing');
    if (this.formulaire.controls.valideDu.invalid) erreurs.push('proposal.dateMissing');
    if (this.lignes.controls.some((l) => l.controls.risqueCode.invalid))
      erreurs.push('proposal.riskMissing');
    if (this.lignes.controls.some((l) => l.hasError('niveauRequis')))
      erreurs.push('proposal.levelRequired');
    this.resume.set(erreurs);
    const affilieId = this.contexte.affilieId();
    if (erreurs.length > 0 || !affilieId) return;

    const valeur = this.formulaire.getRawValue();
    this.envoi.set(true);
    this.api
      .proposerPoste(affilieId, this.posteId(), {
        motif: valeur.motif.trim(),
        valideDu: valeur.valideDu,
        lignes: valeur.lignes.map((l) => ({
          type: l.type,
          risqueCode: l.risqueCode,
          niveauExposition: l.type === 'Retrait' || !l.niveauExposition ? null : l.niveauExposition,
        })),
      })
      .subscribe({
        next: (resultat) => {
          this.envoi.set(false);
          this.soumise.set(resultat);
          queueMicrotask(() => this.succes()?.nativeElement.focus());
        },
        error: (error: unknown) => {
          this.envoi.set(false);
          this.erreurEnvoi.set(versErreurApi(error));
        },
      });
  }

  private nouvelleLigne(): LigneForm {
    return this.fb.group(
      {
        type: this.fb.control<TypeModification>('Ajout'),
        risqueCode: this.fb.control('', Validators.required),
        niveauExposition: this.fb.control<NiveauExposition | ''>(''),
      },
      { validators: (g) => niveauRequis(g as LigneForm) },
    );
  }
}

export function aujourdhui(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}
