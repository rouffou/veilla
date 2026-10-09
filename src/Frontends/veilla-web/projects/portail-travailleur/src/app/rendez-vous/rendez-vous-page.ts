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
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageService } from '@veilla/shared';
import { Observable, of, switchMap } from 'rxjs';
import { Creneau, RendezVous, TYPES_ACTE } from '../api/bff-travailleur.models';
import {
  BffTravailleurService,
  charger,
  Etat,
  ErreurApi,
  versErreurApi,
} from '../api/bff-travailleur.service';
import { affiliesConnus } from '../ui/affilies';
import { chargeable } from '../ui/chargeable';
import { DateLocalePipe } from '../ui/date-locale.pipe';
import { Chargement, ErreurApiMessage } from '../ui/etats';

/** Période de réservation proposée : 60 jours (le service Planification accepte au plus 93 jours). */
const JOURS_RECHERCHE = 60;

/**
 * POR-11 : rendez-vous du travailleur. Consultation, réservation en ligne dans les créneaux ouverts (SAN-12) et annulation.
 * Les règles (créneaux autorisés, obligations couvertes, délai de 24 h) sont celles du service Planification : le portail
 * affiche son refus. Déplacer = annuler puis réserver un autre créneau (le service n'a pas de route de déplacement).
 */
@Component({
  selector: 'app-rendez-vous-page',
  imports: [TranslocoDirective, DateLocalePipe, Chargement, ErreurApiMessage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('appointments.title') }}</h1>
      <p>{{ t('appointments.intro') }}</p>
      @if (message(); as m) {
        <div class="app-succes" role="status" tabindex="-1" #succes>
          <p>{{ t(m) }}</p>
          @if (peutDeplacer()) {
            <p>{{ t('appointments.moveHint') }}</p>
          }
        </div>
      }
      @if (erreurAction(); as e) {
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
          <h2>{{ t('appointments.mine') }}</h2>
          @if (etat.valeur.length === 0) {
            <p class="vl-empty">{{ t('appointments.empty') }}</p>
          } @else {
            <div class="app-table-wrapper">
              <table class="app-table">
                <caption class="vl-visually-hidden">
                  {{ t('appointments.caption') }}
                </caption>
                <thead>
                  <tr>
                    <th scope="col">{{ t('appointments.date') }}</th>
                    <th scope="col">{{ t('appointments.type') }}</th>
                    <th scope="col">{{ t('appointments.status') }}</th>
                    <th scope="col">{{ t('appointments.actions') }}</th>
                  </tr>
                </thead>
                <tbody>
                  @for (r of etat.valeur; track r.id) {
                    <tr>
                      <th scope="row">
                        <time [attr.datetime]="r.debut">{{ r.debut | dateLocale: langue() : true }}</time>
                      </th>
                      <td>{{ t('actes.' + (r.typeActe)) }}</td>
                      <td>
                        <span class="app-badge">{{ t('appointments.statuses.' + statut(r.statut)) }}</span>
                      </td>
                      <td>
                        @if (r.statut === 'Planifie') {
                          <button
                            type="button"
                            class="vl-button vl-button--secondary"
                            [disabled]="enCours()"
                            (click)="annuler(r)"
                          >
                            {{ t('appointments.cancel') }}
                            <span class="vl-visually-hidden">
                              {{ r.debut | dateLocale: langue() : true }}</span
                            >
                          </button>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }

          <h2>{{ t('appointments.bookTitle') }}</h2>
          @if (affilies().length === 0) {
            <p>{{ t('appointments.noEmployer') }}</p>
          } @else {
            <form class="app-form" (submit)="$event.preventDefault(); chercher()" novalidate>
              @if (affilies().length > 1) {
                <div class="app-champ">
                  <label for="rdv-affilie">{{ t('appointments.employer') }}</label>
                  <select id="rdv-affilie" class="app-input" (change)="affilieChoisi.set(valeur($event))">
                    @for (id of affilies(); track id; let i = $index) {
                      <option [value]="id" [selected]="id === affilieCourant()">
                        {{ t('appointments.employerN', { n: i + 1 }) }}
                      </option>
                    }
                  </select>
                </div>
              }
              <div class="app-champ">
                <label for="rdv-type">{{ t('appointments.type') }}</label>
                <select id="rdv-type" class="app-input" (change)="typeChoisi.set(valeur($event))">
                  @for (type of types; track type) {
                    <option [value]="type" [selected]="type === typeCourant()">{{ t('actes.' + type) }}</option>
                  }
                </select>
              </div>
              <button type="submit" class="vl-button">{{ t('appointments.search') }}</button>
            </form>

            @if (creneaux(); as c) {
              @switch (c.statut) {
                @case ('chargement') {
                  <app-chargement />
                }
                @case ('erreur') {
                  <app-erreur-api [erreur]="c.erreur" />
                }
                @case ('ok') {
                  @if (c.valeur.length === 0) {
                    <p class="vl-empty" role="status">{{ t('appointments.noSlots') }}</p>
                  } @else {
                    <h3>{{ t('appointments.slots') }}</h3>
                    <ul class="app-liste">
                      @for (s of c.valeur; track s.id) {
                        <li>
                          <time [attr.datetime]="s.debut">{{ s.debut | dateLocale: langue() : true }}</time>
                          <button
                            type="button"
                            class="vl-button vl-button--secondary"
                            [disabled]="enCours()"
                            (click)="reserver(s)"
                          >
                            {{ t('appointments.book') }}
                            <span class="vl-visually-hidden"> {{ s.debut | dateLocale: langue() : true }}</span>
                          </button>
                        </li>
                      }
                    </ul>
                  }
                }
              }
            }
          }
        }
      }
    </ng-container>
  `,
})
export class RendezVousPage {
  /** Employeur et type d'acte proposés par un lien (convocation) : paramètres de requête liés aux entrées. */
  readonly affilieId = input<string>();
  readonly typeActe = input<string>();

  private readonly api = inject(BffTravailleurService);
  protected readonly langue = inject(LanguageService).current;
  protected readonly types = TYPES_ACTE;
  protected readonly rendezVous = chargeable(() => this.api.rendezVous());

  protected readonly affilieChoisi = signal<string | null>(null);
  protected readonly typeChoisi = signal<string | null>(null);
  protected readonly enCours = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly erreurAction = signal<ErreurApi | null>(null);
  protected readonly peutDeplacer = signal(false);
  private readonly recherche = signal<{ affilieId: string; typeActe: string } | null>(null);
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
  protected readonly typeCourant = computed(() => this.typeChoisi() ?? this.typeActe() ?? TYPES_ACTE[1]);

  protected readonly creneaux = toSignal(
    toObservable(this.recherche).pipe(
      switchMap((r) => {
        if (!r) return of<Etat<readonly Creneau[]> | null>(null);
        const du = new Date();
        const au = new Date(du.getTime() + JOURS_RECHERCHE * 24 * 3600 * 1000);
        return charger(this.api.creneaux(r.affilieId, r.typeActe, du, au));
      }),
    ),
    { initialValue: null },
  );

  protected valeur(evenement: Event): string {
    return (evenement.target as HTMLSelectElement).value;
  }

  /** Statut inconnu du portail (évolution du service) : libellé générique plutôt qu'une clé brute. */
  protected statut(valeur: string): string {
    return ['Planifie', 'Arrive', 'EnSalle', 'Termine', 'Absent', 'Annule'].includes(valeur) ? valeur : 'Inconnu';
  }

  protected chercher(): void {
    const affilieId = this.affilieCourant();
    if (!affilieId) return;
    this.recherche.set({ affilieId, typeActe: this.typeCourant() });
  }

  protected reserver(creneau: Creneau): void {
    const affilieId = this.recherche()?.affilieId;
    if (!affilieId) return;
    this.agir(this.api.reserver(creneau.id, affilieId), 'appointments.booked', false);
  }

  protected annuler(rendezVous: RendezVous): void {
    this.agir(this.api.annuler(rendezVous.id), 'appointments.cancelled', true);
  }

  private agir(appel: Observable<unknown>, cle: string, deplacer: boolean): void {
    this.enCours.set(true);
    this.erreurAction.set(null);
    this.message.set(null);
    appel.subscribe({
      next: () => {
        this.enCours.set(false);
        this.message.set(cle);
        this.peutDeplacer.set(deplacer);
        this.recherche.set(null);
        this.rendezVous.recharger();
        queueMicrotask(() => this.succes()?.nativeElement.focus());
      },
      error: (error: unknown) => {
        this.enCours.set(false);
        this.erreurAction.set(versErreurApi(error));
      },
    });
  }
}
