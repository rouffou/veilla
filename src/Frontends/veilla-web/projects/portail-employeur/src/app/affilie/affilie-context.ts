import { computed, inject, Injectable, signal } from '@angular/core';
import { AffilieResume } from '../api/bff-employeur.models';
import { BffEmployeurService, ErreurApi, versErreurApi } from '../api/bff-employeur.service';

const STORAGE_KEY = 'veilla.employeur.affilie';

/**
 * Affilié en cours de consultation. Un utilisateur (employeur, secrétariat social, SIPP) peut représenter
 * plusieurs affiliés (claim `affilie_id`, POR-01) : le choix est mémorisé localement, sans donnée sensible.
 */
@Injectable({ providedIn: 'root' })
export class AffilieContext {
  private readonly api = inject(BffEmployeurService);

  private readonly liste = signal<readonly AffilieResume[] | null>(null);
  private readonly selection = signal<string | null>(null);
  private readonly erreurChargement = signal<ErreurApi | null>(null);
  private chargementLance = false;

  readonly affilies = this.liste.asReadonly();
  readonly erreur = this.erreurChargement.asReadonly();
  readonly charge = computed(() => this.liste() !== null || this.erreurChargement() !== null);

  /** Identifiant de l'affilié courant ; `null` tant que la liste n'est pas chargée ou si elle est vide. */
  readonly affilieId = computed(() => {
    const affilies = this.liste();
    if (!affilies || affilies.length === 0) return null;
    const choisi = this.selection();
    return affilies.some((a) => a.id === choisi) ? choisi : affilies[0].id;
  });

  readonly affilie = computed(() => this.liste()?.find((a) => a.id === this.affilieId()) ?? null);

  /** Charge une seule fois la liste des affiliés de l'utilisateur. */
  charger(): void {
    if (this.chargementLance) return;
    this.chargementLance = true;
    this.erreurChargement.set(null);
    this.selection.set(lire());
    this.api.mesAffilies().subscribe({
      next: (resultat) => this.liste.set(resultat.affilies),
      error: (error: unknown) => {
        this.erreurChargement.set(versErreurApi(error));
        this.chargementLance = false;
      },
    });
  }

  choisir(affilieId: string): void {
    this.selection.set(affilieId);
    try {
      localStorage.setItem(STORAGE_KEY, affilieId);
    } catch {
      // Stockage indisponible : le choix vaut pour la session en cours.
    }
  }
}

function lire(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}
