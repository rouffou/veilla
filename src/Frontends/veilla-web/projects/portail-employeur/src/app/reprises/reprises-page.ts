import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageService } from '@veilla/shared';
import { combineLatest } from 'rxjs';
import { AffilieContext } from '../affilie/affilie-context';
import { EtatAffilie } from '../affilie/affilie-selector';
import { RepriseAnnoncee, STATUTS_REPRISE, Travailleur } from '../api/bff-employeur.models';
import { BffEmployeurService, ErreurApi, versErreurApi } from '../api/bff-employeur.service';
import { aujourdhui } from '../postes/proposition-poste';
import { TRAVAILLEURS_FORMULAIRE } from '../listes/proposition-liste';
import { DateLocalePipe } from '../ui/date-locale.pipe';
import { Chargement, ErreurApiMessage } from '../ui/etats';
import { parAffilie } from '../ui/par-affilie';

/**
 * POR-04 : annonce d'une reprise du travail (ARC-33) et suivi du processus d'examen de reprise.
 * L'employeur ne voit que le statut du processus et la date limite : aucune donnée médicale, aucun
 * identifiant d'examen ni de décision (ce que voit l'employeur reste à valider, plan saga §6).
 */
@Component({
  selector: 'app-reprises-page',
  imports: [
    TranslocoDirective,
    ReactiveFormsModule,
    EtatAffilie,
    Chargement,
    ErreurApiMessage,
    DateLocalePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('resumptions.title') }}</h1>
      <p>{{ t('resumptions.intro') }}</p>
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
            <h2>{{ t('resumptions.announceTitle') }}</h2>
            @if (annoncee(); as a) {
              <div class="app-succes" role="status" tabindex="-1" #succes>
                <p>{{ a.cree ? t('resumptions.success') : t('resumptions.alreadyKnown') }}</p>
                <p>{{ t('enums.statutReprise.' + statut(a.statut)) }}</p>
              </div>
            }
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
              @if (d.travailleurs.total > d.travailleurs.elements.length) {
                <p class="app-aide">
                  {{ t('resumptions.workersLimit', { n: d.travailleurs.elements.length }) }}
                </p>
              }
              <div class="app-champ">
                <label for="reprise-travailleur"
                  >{{ t('resumptions.worker') }} <span aria-hidden="true">*</span></label
                >
                <select
                  id="reprise-travailleur"
                  formControlName="personneId"
                  required
                  class="app-input"
                  [attr.aria-invalid]="invalide('personneId')"
                  [attr.aria-describedby]="
                    invalide('personneId') ? 'reprise-travailleur-erreur' : null
                  "
                >
                  <option value="">{{ t('form.choose') }}</option>
                  @for (w of d.travailleurs.elements; track w.id) {
                    <option [value]="w.id">{{ w.nom }} {{ w.prenom }}</option>
                  }
                </select>
                @if (invalide('personneId')) {
                  <p id="reprise-travailleur-erreur" class="app-erreur">{{ t('form.required') }}</p>
                }
              </div>
              <div class="app-champ">
                <label for="reprise-date"
                  >{{ t('resumptions.resumptionDate') }} <span aria-hidden="true">*</span></label
                >
                <input
                  id="reprise-date"
                  type="date"
                  formControlName="dateReprise"
                  required
                  class="app-input"
                  [attr.aria-invalid]="invalide('dateReprise')"
                  [attr.aria-describedby]="invalide('dateReprise') ? 'reprise-date-erreur' : null"
                />
                @if (invalide('dateReprise')) {
                  <p id="reprise-date-erreur" class="app-erreur">{{ t('form.required') }}</p>
                }
              </div>
              <div class="app-champ">
                <label for="reprise-absence"
                  >{{ t('resumptions.absenceStart') }} <span aria-hidden="true">*</span></label
                >
                <input
                  id="reprise-absence"
                  type="date"
                  formControlName="debutAbsence"
                  required
                  class="app-input"
                  [attr.aria-invalid]="invalide('debutAbsence')"
                  [attr.aria-describedby]="
                    invalide('debutAbsence') ? 'reprise-absence-erreur' : null
                  "
                />
                @if (invalide('debutAbsence')) {
                  <p id="reprise-absence-erreur" class="app-erreur">{{ t('form.required') }}</p>
                }
              </div>
              <button
                type="submit"
                class="vl-button"
                [disabled]="envoi()"
                [attr.aria-busy]="envoi()"
              >
                {{ envoi() ? t('resumptions.submitting') : t('resumptions.submit') }}
              </button>
            </form>

            <h2>{{ t('resumptions.followTitle') }}</h2>
            @if (d.reprises.length === 0) {
              <p class="vl-empty">{{ t('resumptions.empty') }}</p>
            } @else {
              <div class="app-table-wrapper">
                <table class="app-table">
                  <caption class="vl-visually-hidden">
                    {{
                      t('resumptions.caption')
                    }}
                  </caption>
                  <thead>
                    <tr>
                      <th scope="col">{{ t('resumptions.worker') }}</th>
                      <th scope="col">{{ t('resumptions.resumptionDate') }}</th>
                      <th scope="col">{{ t('resumptions.absenceStart') }}</th>
                      <th scope="col">{{ t('resumptions.status') }}</th>
                      <th scope="col">{{ t('resumptions.deadline') }}</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (r of d.reprises; track r.id) {
                      <tr>
                        <th scope="row">{{ nom(d.travailleurs.elements, r.personneId) }}</th>
                        <td>{{ r.dateReprise | dateLocale: langue() }}</td>
                        <td>{{ r.debutAbsence | dateLocale: langue() }}</td>
                        <td>
                          <span class="app-badge">{{
                            t('enums.statutReprise.' + statut(r.statut))
                          }}</span>
                        </td>
                        <td>
                          {{ r.dateLimite | dateLocale: langue() }}
                          @if (r.enRetard || r.horsDelai) {
                            <span class="app-badge app-badge--refus">{{
                              t('resumptions.overdue')
                            }}</span>
                          }
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          }
        }
      }
    </ng-container>
  `,
})
export class ReprisesPage {
  protected readonly contexte = inject(AffilieContext);
  private readonly api = inject(BffEmployeurService);
  private readonly fb = inject(NonNullableFormBuilder);
  protected readonly langue = inject(LanguageService).current;

  protected readonly donnees = parAffilie((id) =>
    combineLatest({
      reprises: this.api.reprises(id),
      travailleurs: this.api.travailleurs(id, '', 1, TRAVAILLEURS_FORMULAIRE),
    }),
  );

  protected readonly formulaire = this.fb.group({
    personneId: this.fb.control('', Validators.required),
    dateReprise: this.fb.control(aujourdhui(), Validators.required),
    debutAbsence: this.fb.control('', Validators.required),
  });

  protected readonly envoi = signal(false);
  protected readonly tente = signal(false);
  protected readonly annoncee = signal<RepriseAnnoncee | null>(null);
  protected readonly erreurEnvoi = signal<ErreurApi | null>(null);
  protected readonly resume = signal<readonly string[]>([]);
  private readonly succes = viewChild<ElementRef<HTMLElement>>('succes');

  protected invalide(champ: 'personneId' | 'dateReprise' | 'debutAbsence'): boolean {
    const c = this.formulaire.controls[champ];
    return c.invalid && (c.touched || this.tente());
  }

  /** Statut inconnu du portail (évolution du service) : libellé générique plutôt qu'une clé brute. */
  protected statut(valeur: string): string {
    return (STATUTS_REPRISE as readonly string[]).includes(valeur) ? valeur : 'Inconnu';
  }

  protected nom(travailleurs: readonly Travailleur[], personneId: string): string {
    const t = travailleurs.find((w) => w.id === personneId);
    return t ? `${t.nom} ${t.prenom}` : '—';
  }

  protected soumettre(): void {
    this.tente.set(true);
    this.erreurEnvoi.set(null);
    this.annoncee.set(null);
    this.formulaire.markAllAsTouched();
    const c = this.formulaire.controls;
    const erreurs: string[] = [];
    if (c.personneId.invalid) erreurs.push('resumptions.workerMissing');
    if (c.dateReprise.invalid) erreurs.push('resumptions.resumptionDateMissing');
    if (c.debutAbsence.invalid) erreurs.push('resumptions.absenceStartMissing');
    this.resume.set(erreurs);
    const affilieId = this.contexte.affilieId();
    if (erreurs.length > 0 || !affilieId) return;

    this.envoi.set(true);
    this.api.annoncerReprise(affilieId, this.formulaire.getRawValue()).subscribe({
      next: (resultat) => {
        this.envoi.set(false);
        this.annoncee.set(resultat);
        this.tente.set(false);
        this.formulaire.reset({
          personneId: '',
          dateReprise: aujourdhui(),
          debutAbsence: '',
        });
        this.donnees.recharger();
        queueMicrotask(() => this.succes()?.nativeElement.focus());
      },
      error: (error: unknown) => {
        this.envoi.set(false);
        this.erreurEnvoi.set(versErreurApi(error));
      },
    });
  }
}
