import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';
import { LanguageService } from '@veilla/shared';
import { AffilieContext } from '../affilie/affilie-context';
import { EtatAffilie } from '../affilie/affilie-selector';
import { Fichier, ListeNominative } from '../api/bff-employeur.models';
import { BffEmployeurService, ErreurApi, versErreurApi } from '../api/bff-employeur.service';
import { DateLocalePipe } from '../ui/date-locale.pipe';
import { Chargement, ErreurApiMessage } from '../ui/etats';
import { parAffilie } from '../ui/par-affilie';

/** Enregistre un fichier reçu du BFF via un lien temporaire (aucune ouverture de fenêtre). */
export function enregistrer(document: Document, fichier: Fichier): void {
  const url = URL.createObjectURL(fichier.contenu);
  const lien = document.createElement('a');
  lien.href = url;
  lien.download = fichier.nom;
  lien.rel = 'noopener';
  document.body.appendChild(lien);
  lien.click();
  lien.remove();
  setTimeout(() => URL.revokeObjectURL(url), 0);
}

/**
 * Listes nominatives de l'affilié et leurs versions (POR-06, AFF-30, AFF-31) : téléchargement CSV généré
 * par le BFF ; la dernière version de chaque type peut faire l'objet d'une proposition d'ajustement (POR-03).
 */
@Component({
  selector: 'app-listes-page',
  imports: [
    TranslocoDirective,
    RouterLink,
    EtatAffilie,
    Chargement,
    ErreurApiMessage,
    DateLocalePipe,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ng-container *transloco="let t">
      <h1>{{ t('lists.title') }}</h1>
      <p>{{ t('lists.intro') }}</p>
      <app-etat-affilie />
      @if (contexte.affilieId()) {
        <div aria-live="polite" class="app-aide">
          @if (telechargement(); as nom) {
            <p>{{ t('lists.downloaded', { fichier: nom }) }}</p>
          }
        </div>
        @if (erreurTelechargement(); as e) {
          <app-erreur-api [erreur]="e" />
        }
        @let etat = listes.etat();
        @switch (etat.statut) {
          @case ('chargement') {
            <app-chargement />
          }
          @case ('erreur') {
            <app-erreur-api
              [erreur]="etat.erreur"
              [reessayable]="true"
              (reessayer)="listes.recharger()"
            />
          }
          @case ('ok') {
            @if (etat.valeur.length === 0) {
              <p class="vl-empty">{{ t('lists.empty') }}</p>
            } @else {
              <div class="app-table-wrapper">
                <table class="app-table">
                  <caption class="vl-visually-hidden">
                    {{
                      t('lists.caption')
                    }}
                  </caption>
                  <thead>
                    <tr>
                      <th scope="col">{{ t('lists.type') }}</th>
                      <th scope="col">{{ t('lists.version') }}</th>
                      <th scope="col">{{ t('lists.referenceDate') }}</th>
                      <th scope="col">{{ t('lists.generatedOn') }}</th>
                      <th scope="col">{{ t('lists.lines') }}</th>
                      <th scope="col">
                        <span class="vl-visually-hidden">{{ t('positions.actions') }}</span>
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (l of etat.valeur; track l.id) {
                      <tr>
                        <th scope="row">{{ t('enums.typeListe.' + l.type) }}</th>
                        <td>
                          {{ l.version }}
                          @if (l.derniere) {
                            <span class="app-badge">{{ t('lists.latest') }}</span>
                          }
                        </td>
                        <td>{{ l.dateReference | dateLocale: langue() }}</td>
                        <td>{{ l.dateGeneration | dateLocale: langue() : true }}</td>
                        <td>{{ l.nombreLignes }}</td>
                        <td class="app-actions">
                          <button
                            type="button"
                            class="vl-button vl-button--secondary"
                            [disabled]="enCours() === l.id"
                            [attr.aria-busy]="enCours() === l.id"
                            (click)="telecharger(l)"
                          >
                            {{ t('lists.download')
                            }}<span class="vl-visually-hidden"> — {{ libelle(t, l) }}</span>
                          </button>
                          @if (l.derniere) {
                            <a
                              class="vl-button vl-button--secondary"
                              [routerLink]="['/listes-nominatives', l.id, 'proposition']"
                            >
                              {{ t('lists.propose')
                              }}<span class="vl-visually-hidden"> — {{ libelle(t, l) }}</span>
                            </a>
                          }
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
              <p class="app-aide">{{ t('lists.formatNote') }}</p>
            }
          }
        }
      }
    </ng-container>
  `,
})
export class ListesPage {
  protected readonly contexte = inject(AffilieContext);
  private readonly api = inject(BffEmployeurService);
  private readonly document = inject(DOCUMENT);
  protected readonly langue = inject(LanguageService).current;

  protected readonly listes = parAffilie((id) => this.api.listesNominatives(id));
  protected readonly enCours = signal<string | null>(null);
  protected readonly telechargement = signal<string | null>(null);
  protected readonly erreurTelechargement = signal<ErreurApi | null>(null);

  protected libelle(t: (cle: string) => string, l: ListeNominative): string {
    return `${t('enums.typeListe.' + l.type)} v${l.version}`;
  }

  protected telecharger(liste: ListeNominative): void {
    const affilieId = this.contexte.affilieId();
    if (!affilieId) return;
    this.enCours.set(liste.id);
    this.telechargement.set(null);
    this.erreurTelechargement.set(null);
    this.api.telechargerListeCsv(affilieId, liste.id).subscribe({
      next: (fichier) => {
        enregistrer(this.document, fichier);
        this.enCours.set(null);
        this.telechargement.set(fichier.nom);
      },
      error: (error: unknown) => {
        this.enCours.set(null);
        this.erreurTelechargement.set(versErreurApi(error));
      },
    });
  }
}
