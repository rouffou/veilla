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
import {
  Creneau,
  LONGUEUR_MAX_COMMENTAIRE,
  RAISONS_ANNULATION,
  RaisonAnnulation,
  RendezVous,
  TYPES_ACTE,
} from '../api/bff-travailleur.models';
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
 * POR-11 : rendez-vous du travailleur. Consultation, réservation en ligne dans les créneaux ouverts (SAN-12), confirmation
 * de présence, déplacement atomique (un seul appel au service) et annulation avec raison codée et précision courte.
 * Les règles (créneaux autorisés, obligations couvertes, délais, nombre de déplacements) sont celles du service
 * Planification : le portail affiche son refus.
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
                          @if (r.presenceConfirmeeLe) {
                            <span class="app-badge">{{ t('appointments.presenceConfirmed') }}</span>
                          } @else {
                            <button
                              type="button"
                              class="vl-button vl-button--secondary"
                              data-action="confirmer"
                              [disabled]="enCours()"
                              (click)="confirmer(r)"
                            >
                              {{ t('appointments.confirm') }}
                              <span class="vl-visually-hidden">
                                {{ r.debut | dateLocale: langue() : true }}</span
                              >
                            </button>
                          }
                          <button
                            type="button"
                            class="vl-button vl-button--secondary"
                            data-action="deplacer"
                            [disabled]="enCours()"
                            (click)="ouvrirDeplacement(r)"
                          >
                            {{ t('appointments.move') }}
                            <span class="vl-visually-hidden">
                              {{ r.debut | dateLocale: langue() : true }}</span
                            >
                          </button>
                          <button
                            type="button"
                            class="vl-button vl-button--secondary"
                            data-action="annuler"
                            [disabled]="enCours()"
                            (click)="ouvrirAnnulation(r)"
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

          @if (annulation(); as a) {
            <section class="app-form" aria-labelledby="rdv-annulation-titre">
              <h2 id="rdv-annulation-titre">{{ t('appointments.cancelTitle') }}</h2>
              <p>
                <time [attr.datetime]="a.debut">{{ a.debut | dateLocale: langue() : true }}</time>
              </p>
              <div class="app-champ">
                <label for="rdv-raison">{{ t('appointments.reason') }}</label>
                <select id="rdv-raison" class="app-input" required (change)="raison.set(valeur($event))">
                  <option value="">{{ t('appointments.reasonChoose') }}</option>
                  @for (code of raisons; track code) {
                    <option [value]="code">{{ t('appointments.reasons.' + code) }}</option>
                  }
                </select>
              </div>
              <div class="app-champ">
                <label for="rdv-commentaire">{{ t('appointments.comment') }}</label>
                <textarea
                  id="rdv-commentaire"
                  class="app-input"
                  rows="3"
                  [attr.maxlength]="longueurMax"
                  aria-describedby="rdv-commentaire-aide"
                  (input)="commentaire.set(texte($event))"
                ></textarea>
                <p id="rdv-commentaire-aide" class="vl-hint">{{ t('appointments.commentHelp') }}</p>
              </div>
              <button
                type="button"
                class="vl-button"
                data-action="confirmer-annulation"
                [disabled]="enCours() || !raison()"
                (click)="annuler(a)"
              >
                {{ t('appointments.confirmCancel') }}
              </button>
              <button type="button" class="vl-button vl-button--secondary" (click)="fermer()">
                {{ t('appointments.close') }}
              </button>
            </section>
          }

          @if (deplacement(); as d) {
            <section aria-labelledby="rdv-deplacement-titre">
              <h2 id="rdv-deplacement-titre">{{ t('appointments.moveTitle') }}</h2>
              <p>{{ t('appointments.moveIntro') }}</p>
              @if (creneauxDeplacement(); as c) {
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
                      <ul class="app-liste">
                        @for (s of c.valeur; track s.id) {
                          <li>
                            <time [attr.datetime]="s.debut">{{ s.debut | dateLocale: langue() : true }}</time>
                            <button
                              type="button"
                              class="vl-button vl-button--secondary"
                              data-action="deplacer-ici"
                              [disabled]="enCours()"
                              (click)="deplacer(d, s)"
                            >
                              {{ t('appointments.moveHere') }}
                              <span class="vl-visually-hidden"> {{ s.debut | dateLocale: langue() : true }}</span>
                            </button>
                          </li>
                        }
                      </ul>
                    }
                  }
                }
              }
              <button type="button" class="vl-button vl-button--secondary" (click)="fermer()">
                {{ t('appointments.close') }}
              </button>
            </section>
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
  protected readonly employeurs = chargeable(() => this.api.employeurs());
  protected readonly examensAPlanifier = chargeable(() => this.api.examensAPlanifier());

  protected readonly affilieChoisi = signal<string | null>(null);
  protected readonly typeChoisi = signal<string | null>(null);
  protected readonly enCours = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly erreurAction = signal<ErreurApi | null>(null);
  protected readonly raisons = RAISONS_ANNULATION;
  protected readonly longueurMax = LONGUEUR_MAX_COMMENTAIRE;
  protected readonly annulation = signal<RendezVous | null>(null);
  protected readonly deplacement = signal<RendezVous | null>(null);
  protected readonly raison = signal('');
  protected readonly commentaire = signal('');
  protected readonly creneauxDeplacement = toSignal(
    toObservable(this.deplacement).pipe(
      switchMap((r) => {
        if (!r) return of<Etat<readonly Creneau[]> | null>(null);
        const du = new Date();
        const au = new Date(du.getTime() + JOURS_RECHERCHE * 24 * 3600 * 1000);
        return charger(this.api.creneaux(r.affilieId, r.typeActe, du, au));
      }),
    ),
    { initialValue: null },
  );
  private readonly recherche = signal<{ affilieId: string; typeActe: string } | null>(null);
  private readonly succes = viewChild<ElementRef<HTMLElement>>('succes');

  protected readonly affilies = computed(() => {
    const rdv = this.rendezVous.etat();
    const employeurs = this.employeurs.etat();
    return affiliesConnus(
      employeurs.statut === 'ok' ? employeurs.valeur : [],
      rdv.statut === 'ok' ? rdv.valeur : [],
      this.affilieId(),
      this.examens(),
    );
  });
  /** Examens à planifier de la personne connectée : permettent une première réservation sans lien de convocation. */
  private readonly examens = computed(() => {
    const etat = this.examensAPlanifier.etat();
    return etat.statut === 'ok' ? etat.valeur : [];
  });
  protected readonly affilieCourant = computed(() => {
    const choisi = this.affilieChoisi();
    const liste = this.affilies();
    if (choisi && liste.includes(choisi)) return choisi;
    const propose = this.affilieId() ?? this.examens()[0]?.affilieId;
    return propose && liste.includes(propose) ? propose : (liste[0] ?? null);
  });
  protected readonly typeCourant = computed(() => {
    const premier = this.examens().find((e) => (TYPES_ACTE as readonly string[]).includes(e.type));
    return this.typeChoisi() ?? this.typeActe() ?? premier?.type ?? TYPES_ACTE[1];
  });

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
    this.agir(this.api.reserver(creneau.id, affilieId), 'appointments.booked');
  }

  protected texte(evenement: Event): string {
    return (evenement.target as HTMLTextAreaElement).value;
  }

  protected ouvrirAnnulation(rendezVous: RendezVous): void {
    this.fermer();
    this.annulation.set(rendezVous);
  }

  protected ouvrirDeplacement(rendezVous: RendezVous): void {
    this.fermer();
    this.deplacement.set(rendezVous);
  }

  protected fermer(): void {
    this.annulation.set(null);
    this.deplacement.set(null);
    this.raison.set('');
    this.commentaire.set('');
  }

  /** Annulation avec la raison codée choisie et la précision éventuelle (sans donnée de santé : voir l'aide du formulaire). */
  protected annuler(rendezVous: RendezVous): void {
    const raison = this.raison();
    if (!raison) return;
    this.agir(
      this.api.annuler(rendezVous.id, raison as RaisonAnnulation, this.commentaire()),
      'appointments.cancelled',
    );
  }

  /** Déplacement atomique : un seul appel annule l'ancien créneau et réserve le nouveau (409 si le créneau est pris). */
  protected deplacer(rendezVous: RendezVous, creneau: Creneau): void {
    this.agir(this.api.deplacer(rendezVous.id, creneau.id), 'appointments.moved');
  }

  protected confirmer(rendezVous: RendezVous): void {
    this.agir(this.api.confirmer(rendezVous.id), 'appointments.confirmed');
  }

  private agir(appel: Observable<unknown>, cle: string): void {
    this.enCours.set(true);
    this.erreurAction.set(null);
    this.message.set(null);
    appel.subscribe({
      next: () => {
        this.enCours.set(false);
        this.message.set(cle);
        this.fermer();
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
