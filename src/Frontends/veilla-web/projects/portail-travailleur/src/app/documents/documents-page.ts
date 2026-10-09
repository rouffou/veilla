import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageService } from '@veilla/shared';
import { DocumentPersonnel, Fichier } from '../api/bff-travailleur.models';
import { BffTravailleurService, ErreurApi, versErreurApi } from '../api/bff-travailleur.service';
import { chargeable } from '../ui/chargeable';
import { DateLocalePipe } from '../ui/date-locale.pipe';
import { Chargement, ErreurApiMessage } from '../ui/etats';

/**
 * POR-13 : documents personnels publiés pour le travailleur (formulaire d'évaluation de santé, exemplaire du travailleur).
 * Le téléchargement passe par le BFF, qui vérifie le destinataire ; le service Documents journalise la lecture (NF-04).
 * Le carnet de vaccination et la demande de copie du dossier (SAN-43) ne sont pas encore offerts : voir l'encart.
 */
@Component({
  selector: 'app-documents-page',
  imports: [TranslocoDirective, DateLocalePipe, Chargement, ErreurApiMessage],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('documents.title') }}</h1>
      <p>{{ t('documents.intro') }}</p>
      @if (erreurTelechargement(); as e) {
        <app-erreur-api [erreur]="e" />
      }
      @let etat = documents.etat();
      @switch (etat.statut) {
        @case ('chargement') {
          <app-chargement />
        }
        @case ('erreur') {
          <app-erreur-api [erreur]="etat.erreur" [reessayable]="true" (reessayer)="documents.recharger()" />
        }
        @case ('ok') {
          @if (etat.valeur.length === 0) {
            <p class="vl-empty">{{ t('documents.empty') }}</p>
          } @else {
            <div class="app-table-wrapper">
              <table class="app-table">
                <caption class="vl-visually-hidden">
                  {{ t('documents.caption') }}
                </caption>
                <thead>
                  <tr>
                    <th scope="col">{{ t('documents.name') }}</th>
                    <th scope="col">{{ t('documents.date') }}</th>
                    <th scope="col">{{ t('documents.language') }}</th>
                    <th scope="col">{{ t('documents.actions') }}</th>
                  </tr>
                </thead>
                <tbody>
                  @for (d of etat.valeur; track d.id) {
                    <tr>
                      <th scope="row">{{ t('documents.categories.' + d.categorie) }}</th>
                      <td>{{ d.date | dateLocale: langue() }}</td>
                      <td [attr.lang]="d.langue.toLowerCase()">{{ d.langue.toUpperCase() }}</td>
                      <td>
                        <button
                          type="button"
                          class="vl-button vl-button--secondary"
                          [disabled]="enCours() === d.id"
                          (click)="telecharger(d)"
                        >
                          {{ t('documents.download') }}
                          <span class="vl-visually-hidden">
                            {{ t('documents.categories.' + d.categorie) }}
                            {{ d.date | dateLocale: langue() }}</span
                          >
                        </button>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        }
      }
      <h2>{{ t('documents.notYetTitle') }}</h2>
      <p>{{ t('documents.notYet') }}</p>
    </ng-container>
  `,
})
export class DocumentsPage {
  private readonly api = inject(BffTravailleurService);
  protected readonly langue = inject(LanguageService).current;
  protected readonly documents = chargeable(() => this.api.documents());
  protected readonly enCours = signal<string | null>(null);
  protected readonly erreurTelechargement = signal<ErreurApi | null>(null);

  protected telecharger(document: DocumentPersonnel): void {
    this.enCours.set(document.id);
    this.erreurTelechargement.set(null);
    this.api.telechargerDocument(document.id).subscribe({
      next: (fichier) => {
        this.enCours.set(null);
        enregistrer(fichier);
      },
      error: (error: unknown) => {
        this.enCours.set(null);
        this.erreurTelechargement.set(versErreurApi(error));
      },
    });
  }
}

/** Propose le fichier au navigateur (lien temporaire libéré aussitôt). */
function enregistrer(fichier: Fichier): void {
  const url = URL.createObjectURL(fichier.contenu);
  const lien = document.createElement('a');
  lien.href = url;
  lien.download = fichier.nom;
  lien.click();
  URL.revokeObjectURL(url);
}
