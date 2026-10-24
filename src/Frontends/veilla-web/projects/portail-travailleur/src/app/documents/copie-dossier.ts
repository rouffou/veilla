import { ChangeDetectionStrategy, Component, computed, ElementRef, inject, signal, viewChild } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageService } from '@veilla/shared';
import { BffTravailleurService, ErreurApi, versErreurApi } from '../api/bff-travailleur.service';
import { chargeable } from '../ui/chargeable';
import { DateLocalePipe } from '../ui/date-locale.pipe';
import { Chargement, ErreurApiMessage } from '../ui/etats';

/**
 * POR-13, SAN-43 : demande de copie du dossier de santé et suivi de la demande (§3.3 « copie sur demande »). Le portail ne
 * lit jamais le dossier : le médecin du travail valide la copie, le service Documents la publie, et elle se télécharge
 * dans la liste des documents.
 */
@Component({
  selector: 'app-copie-dossier',
  imports: [TranslocoDirective, DateLocalePipe, Chargement, ErreurApiMessage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <section aria-labelledby="copie-titre">
        <h2 id="copie-titre">{{ t('documents.copy.title') }}</h2>
        <p>{{ t('documents.copy.intro') }}</p>
        @if (envoyee()) {
          <div class="app-succes" role="status" tabindex="-1" #succes>
            <p>{{ t('documents.copy.success') }}</p>
          </div>
        }
        @if (erreurEnvoi(); as e) {
          @if (e.code === 'demande-copie.en-cours') {
            <p role="alert">{{ t('documents.copy.inProgress') }}</p>
          } @else {
            <app-erreur-api [erreur]="e" />
          }
        }
        @let etat = demandes.etat();
        @switch (etat.statut) {
          @case ('chargement') {
            <app-chargement />
          }
          @case ('erreur') {
            <app-erreur-api [erreur]="etat.erreur" [reessayable]="true" (reessayer)="demandes.recharger()" />
          }
          @case ('ok') {
            @if (enCours()) {
              <p>{{ t('documents.copy.inProgress') }}</p>
            } @else {
              <button type="button" class="vl-button" [disabled]="envoi()" [attr.aria-busy]="envoi()" (click)="demander()">
                {{ envoi() ? t('documents.copy.submitting') : t('documents.copy.submit') }}
              </button>
            }
            @if (etat.valeur.length === 0) {
              <p class="vl-empty">{{ t('documents.copy.empty') }}</p>
            } @else {
              <div class="app-table-wrapper">
                <table class="app-table">
                  <caption class="vl-visually-hidden">
                    {{ t('documents.copy.caption') }}
                  </caption>
                  <thead>
                    <tr>
                      <th scope="col">{{ t('documents.copy.requestedOn') }}</th>
                      <th scope="col">{{ t('documents.copy.deadline') }}</th>
                      <th scope="col">{{ t('documents.copy.status') }}</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (d of etat.valeur; track d.id) {
                      <tr>
                        <th scope="row">{{ d.deposeeLe | dateLocale: langue() }}</th>
                        <td>{{ d.dateLimite | dateLocale: langue() }}</td>
                        <td>
                          {{ t('documents.copy.statuses.' + d.statut) }}
                          @if (d.enRetard) {
                            — <strong>{{ t('documents.copy.late') }}</strong>
                          }
                          @if (d.motifRefus) {
                            <br />{{ t('documents.copy.refusals.' + d.motifRefus) }}
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
      </section>
    </ng-container>
  `,
})
export class CopieDossier {
  private readonly api = inject(BffTravailleurService);
  protected readonly langue = inject(LanguageService).current;
  protected readonly demandes = chargeable(() => this.api.demandesCopie());
  protected readonly envoi = signal(false);
  protected readonly envoyee = signal(false);
  protected readonly erreurEnvoi = signal<ErreurApi | null>(null);
  private readonly succes = viewChild<ElementRef<HTMLElement>>('succes');

  protected readonly enCours = computed(() => {
    const etat = this.demandes.etat();
    return etat.statut === 'ok' && etat.valeur.some((d) => d.statut === 'Deposee' || d.statut === 'Validee');
  });

  protected demander(): void {
    this.envoi.set(true);
    this.envoyee.set(false);
    this.erreurEnvoi.set(null);
    this.api.demanderCopie().subscribe({
      next: () => {
        this.envoi.set(false);
        this.envoyee.set(true);
        this.demandes.recharger();
        queueMicrotask(() => this.succes()?.nativeElement.focus());
      },
      error: (error: unknown) => {
        this.envoi.set(false);
        this.erreurEnvoi.set(versErreurApi(error));
      },
    });
  }
}
