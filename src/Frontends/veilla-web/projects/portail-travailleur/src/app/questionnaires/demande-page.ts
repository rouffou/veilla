import { ChangeDetectionStrategy, Component, computed, ElementRef, inject, input, signal, viewChild } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { TYPES_DEMANDE, TypeDemande } from '../api/bff-travailleur.models';
import { BffTravailleurService, ErreurApi, versErreurApi } from '../api/bff-travailleur.service';
import { affiliesConnus } from '../ui/affilies';
import { chargeable } from '../ui/chargeable';
import { Chargement, ErreurApiMessage } from '../ui/etats';

/**
 * POR-12 : demande de consultation spontanée ou de visite de pré-reprise, sans passer par l'employeur. Le motif n'est ni
 * demandé ni transmis (§5.1) : il se donne au médecin lors de la consultation. Le service Obligations fixe l'échéance.
 */
@Component({
  selector: 'app-demande-page',
  imports: [TranslocoDirective, Chargement, ErreurApiMessage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('request.title') }}</h1>
      <p>{{ t('request.intro') }}</p>
      <p class="app-aide">{{ t('request.noReason') }}</p>
      @if (envoyee()) {
        <div class="app-succes" role="status" tabindex="-1" #succes>
          <p>{{ t('request.success') }}</p>
        </div>
      }
      @if (erreurEnvoi(); as e) {
        <app-erreur-api [erreur]="e" />
      }
      @let etat = rendezVous.etat();
      @switch (etat.statut) {
        @case ('chargement') {
          <app-chargement />
        }
        @case ('erreur') {
          <app-erreur-api [erreur]="etat.erreur" [reessayable]="true" (reessayer)="rendezVous.recharger()" />
        }
        @case ('ok') {
          @if (affilies().length === 0) {
            <p>{{ t('request.noEmployer') }}</p>
          } @else {
            <form class="app-form" (submit)="$event.preventDefault(); envoyer()" novalidate>
              @if (affilies().length > 1) {
                <div class="app-champ">
                  <label for="demande-affilie">{{ t('appointments.employer') }}</label>
                  <select id="demande-affilie" class="app-input" (change)="affilieChoisi.set($any($event.target).value)">
                    @for (id of affilies(); track id; let i = $index) {
                      <option [value]="id" [selected]="id === affilieCourant()">
                        {{ t('appointments.employerN', { n: i + 1 }) }}
                      </option>
                    }
                  </select>
                </div>
              }
              <fieldset class="app-fieldset">
                <legend>{{ t('request.type') }}</legend>
                @for (type of types; track type) {
                  <label>
                    <input
                      type="radio"
                      name="demande-type"
                      [value]="type"
                      [checked]="type === typeChoisi()"
                      (change)="typeChoisi.set(type)"
                    />
                    {{ t('actes.' + type) }}
                  </label>
                }
              </fieldset>
              <button type="submit" class="vl-button" [disabled]="envoi()" [attr.aria-busy]="envoi()">
                {{ envoi() ? t('request.submitting') : t('request.submit') }}
              </button>
            </form>
          }
        }
      }
    </ng-container>
  `,
})
export class DemandePage {
  /** Employeur proposé par un lien : paramètre de requête lié à l'entrée. */
  readonly affilieId = input<string>();

  private readonly api = inject(BffTravailleurService);
  protected readonly types = TYPES_DEMANDE;
  protected readonly rendezVous = chargeable(() => this.api.rendezVous());
  protected readonly affilieChoisi = signal<string | null>(null);
  protected readonly typeChoisi = signal<TypeDemande>(TYPES_DEMANDE[0]);
  protected readonly envoi = signal(false);
  protected readonly envoyee = signal(false);
  protected readonly erreurEnvoi = signal<ErreurApi | null>(null);
  private readonly succes = viewChild<ElementRef<HTMLElement>>('succes');

  protected readonly affilies = computed(() => {
    const etat = this.rendezVous.etat();
    return affiliesConnus(etat.statut === 'ok' ? etat.valeur : [], this.affilieId());
  });
  protected readonly affilieCourant = computed(() => {
    const choisi = this.affilieChoisi();
    const liste = this.affilies();
    return choisi && liste.includes(choisi) ? choisi : (liste[0] ?? null);
  });

  protected envoyer(): void {
    const affilieId = this.affilieCourant();
    if (!affilieId) return;
    this.envoi.set(true);
    this.envoyee.set(false);
    this.erreurEnvoi.set(null);
    this.api.demander(affilieId, this.typeChoisi()).subscribe({
      next: () => {
        this.envoi.set(false);
        this.envoyee.set(true);
        queueMicrotask(() => this.succes()?.nativeElement.focus());
      },
      error: (error: unknown) => {
        this.envoi.set(false);
        this.erreurEnvoi.set(versErreurApi(error));
      },
    });
  }
}
