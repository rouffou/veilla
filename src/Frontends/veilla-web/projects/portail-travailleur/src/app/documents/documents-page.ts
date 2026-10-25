import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { TranslocoDirective } from '@jsverse/transloco';
import { chargeable, Chargement, DateLocalePipe, ErreurApi, ErreurApiMessage, LanguageService, versErreurApi } from '@veilla/shared';
import { DocumentPersonnel, Fichier } from '../api/bff-travailleur.models';
import { BffTravailleurService } from '../api/bff-travailleur.service';
import { CopieDossier } from './copie-dossier';

/**
 * POR-13 : documents personnels publiés pour le travailleur (formulaire d'évaluation de santé, carnet de vaccination mis à
 * jour après chaque vaccination, copie du dossier de santé). Le téléchargement passe par le BFF, qui vérifie le
 * destinataire ; le service Documents journalise la lecture (NF-04). La demande de copie (SAN-43) se fait plus bas.
 */
@Component({
  selector: 'app-documents-page',
  imports: [TranslocoDirective, DateLocalePipe, Chargement, ErreurApiMessage, CopieDossier],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('documents.title') }}</h1>
      <p>{{ t('documents.intro') }}</p>
      @if (erreurTelechargement(); as e) {
        <vl-erreur-api [erreur]="e" />
      }
      @let etat = documents.etat();
      @switch (etat.statut) {
        @case ('chargement') {
          <vl-chargement />
        }
        @case ('erreur') {
          <vl-erreur-api [erreur]="etat.erreur" [reessayable]="true" (reessayer)="documents.recharger()" />
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
      <h2>{{ t('documents.vaccinationTitle') }}</h2>
      <p>{{ t('documents.vaccination') }}</p>
      <app-copie-dossier />
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
