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
import { combineLatest } from 'rxjs';
import { AffilieContext } from '../affilie/affilie-context';
import { EtatAffilie } from '../affilie/affilie-selector';
import { PropositionSoumise, TypeModification } from '../api/bff-employeur.models';
import { BffEmployeurService, ErreurApi, versErreurApi } from '../api/bff-employeur.service';
import { TYPES_MODIFICATION } from '../postes/proposition-poste';
import { Chargement, ErreurApiMessage } from '../ui/etats';
import { parAffilie } from '../ui/par-affilie';

/** Nombre de travailleurs proposés dans le formulaire (taille maximale d'une page du BFF). */
export const TRAVAILLEURS_FORMULAIRE = 100;

type LigneForm = FormGroup<{
  type: FormControl<TypeModification>;
  personneId: FormControl<string>;
  posteId: FormControl<string>;
}>;

/**
 * POR-03 : proposition d'ajustement d'une liste nominative (AFF-31) — ajout, modification ou retrait d'un
 * travailleur sur un poste — soumise à la validation du CPMT ; aucune modification directe de la liste.
 */
@Component({
  selector: 'app-proposition-liste',
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
      <h1>{{ t('listProposal.title') }}</h1>
      <p>
        <a routerLink="/listes-nominatives">{{ t('listProposal.back') }}</a>
      </p>
      <app-etat-affilie />
      @if (contexte.affilieId()) {
        @let etat = donnees.etat();
        @switch (etat.statut) {
          @case ('chargement') {
            <app-chargement />
          }
          @case ('erreur') {
            <app-erreur-api
              [erreur]="etat.erreur"
              [reessayable]="true"
              (reessayer)="donnees.recharger()"
            />
          }
          @case ('ok') {
            @let d = etat.valeur;
            <p>
              {{
                t('listProposal.intro', {
                  liste: t('enums.typeListe.' + d.liste.liste.type),
                  version: d.liste.liste.version,
                })
              }}
            </p>

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
                  <label for="liste-motif"
                    >{{ t('proposal.reason') }} <span aria-hidden="true">*</span></label
                  >
                  <textarea
                    id="liste-motif"
                    formControlName="motif"
                    rows="4"
                    maxlength="2000"
                    required
                    class="app-input"
                    [attr.aria-invalid]="motifInvalide()"
                    [attr.aria-describedby]="motifInvalide() ? 'liste-motif-erreur' : null"
                  ></textarea>
                  @if (motifInvalide()) {
                    <p id="liste-motif-erreur" class="app-erreur">{{ t('form.required') }}</p>
                  }
                </div>

                <fieldset formArrayName="lignes" class="app-fieldset">
                  <legend>{{ t('listProposal.lines') }}</legend>
                  @if (d.travailleurs.total > d.travailleurs.elements.length) {
                    <p class="app-aide">
                      {{ t('listProposal.workersLimit', { n: d.travailleurs.elements.length }) }}
                    </p>
                  }
                  @for (ligne of lignes.controls; track ligne; let i = $index) {
                    <div
                      class="app-ligne"
                      [formGroupName]="i"
                      role="group"
                      [attr.aria-label]="t('proposal.line', { n: i + 1 })"
                    >
                      <div class="app-champ">
                        <label [for]="'liste-type-' + i">{{ t('proposal.lineType') }}</label>
                        <select [id]="'liste-type-' + i" formControlName="type" class="app-input">
                          @for (type of types; track type) {
                            <option [value]="type">
                              {{ t('enums.typeModification.' + type) }}
                            </option>
                          }
                        </select>
                      </div>
                      <div class="app-champ">
                        <label [for]="'liste-personne-' + i"
                          >{{ t('listProposal.worker') }} <span aria-hidden="true">*</span></label
                        >
                        <select
                          [id]="'liste-personne-' + i"
                          formControlName="personneId"
                          required
                          class="app-input"
                          [attr.aria-invalid]="ligneInvalide(i, 'personneId')"
                          [attr.aria-describedby]="
                            ligneInvalide(i, 'personneId') ? 'liste-personne-erreur-' + i : null
                          "
                        >
                          <option value="">{{ t('form.choose') }}</option>
                          @for (w of d.travailleurs.elements; track w.id) {
                            <option [value]="w.id">{{ w.nom }} {{ w.prenom }}</option>
                          }
                        </select>
                        @if (ligneInvalide(i, 'personneId')) {
                          <p [id]="'liste-personne-erreur-' + i" class="app-erreur">
                            {{ t('form.required') }}
                          </p>
                        }
                      </div>
                      <div class="app-champ">
                        <label [for]="'liste-poste-' + i"
                          >{{ t('listProposal.position') }} <span aria-hidden="true">*</span></label
                        >
                        <select
                          [id]="'liste-poste-' + i"
                          formControlName="posteId"
                          required
                          class="app-input"
                          [attr.aria-invalid]="ligneInvalide(i, 'posteId')"
                          [attr.aria-describedby]="
                            ligneInvalide(i, 'posteId') ? 'liste-poste-erreur-' + i : null
                          "
                        >
                          <option value="">{{ t('form.choose') }}</option>
                          @for (p of d.postes; track p.id) {
                            <option [value]="p.id">{{ p.intitule }}</option>
                          }
                        </select>
                        @if (ligneInvalide(i, 'posteId')) {
                          <p [id]="'liste-poste-erreur-' + i" class="app-erreur">
                            {{ t('form.required') }}
                          </p>
                        }
                      </div>
                      @if (lignes.length > 1) {
                        <button
                          type="button"
                          class="vl-button vl-button--secondary"
                          (click)="lignes.removeAt(i)"
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
                    (click)="lignes.push(nouvelleLigne())"
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
      }
    </ng-container>
  `,
})
export class PropositionListe {
  readonly listeId = input.required<string>();

  protected readonly contexte = inject(AffilieContext);
  private readonly api = inject(BffEmployeurService);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly langue = inject(LanguageService).current;

  protected readonly types = TYPES_MODIFICATION;

  private readonly cle = computed(() => ({ listeId: this.listeId(), langue: this.langue() }));
  protected readonly donnees = parAffilie(
    (id, p: { listeId: string }) =>
      combineLatest({
        liste: this.api.listeNominative(id, p.listeId),
        travailleurs: this.api.travailleurs(id, '', 1, TRAVAILLEURS_FORMULAIRE),
        postes: this.api.postes(id),
      }),
    this.cle,
  );

  protected readonly formulaire = this.fb.group({
    motif: this.fb.control('', [Validators.required, Validators.maxLength(2000)]),
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

  protected motifInvalide(): boolean {
    const c = this.formulaire.controls.motif;
    return c.invalid && (c.touched || this.tente());
  }

  protected ligneInvalide(index: number, champ: 'personneId' | 'posteId'): boolean {
    const c = this.lignes.at(index).controls[champ];
    return c.invalid && (c.touched || this.tente());
  }

  protected nouvelleLigne(): LigneForm {
    return this.fb.group({
      type: this.fb.control<TypeModification>('Ajout'),
      personneId: this.fb.control('', Validators.required),
      posteId: this.fb.control('', Validators.required),
    });
  }

  protected soumettre(): void {
    this.tente.set(true);
    this.erreurEnvoi.set(null);
    this.formulaire.markAllAsTouched();
    const erreurs: string[] = [];
    if (this.formulaire.controls.motif.invalid) erreurs.push('proposal.reasonMissing');
    if (this.lignes.controls.some((l) => l.controls.personneId.invalid))
      erreurs.push('listProposal.workerMissing');
    if (this.lignes.controls.some((l) => l.controls.posteId.invalid))
      erreurs.push('listProposal.positionMissing');
    this.resume.set(erreurs);
    const affilieId = this.contexte.affilieId();
    if (erreurs.length > 0 || !affilieId) return;

    const valeur = this.formulaire.getRawValue();
    this.envoi.set(true);
    this.api
      .proposerListe(affilieId, this.listeId(), {
        motif: valeur.motif.trim(),
        lignes: valeur.lignes,
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
}
