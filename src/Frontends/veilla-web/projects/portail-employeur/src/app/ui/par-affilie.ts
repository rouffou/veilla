import { computed, inject, Signal, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { filter, Observable, switchMap } from 'rxjs';
import { charger, CHARGEMENT, Etat } from '../api/bff-employeur.service';
import { AffilieContext } from '../affilie/affilie-context';

export interface Chargeable<T> {
  readonly etat: Signal<Etat<T>>;
  /** Relance la requête (bouton « Réessayer », après une proposition…). */
  recharger(): void;
}

/**
 * Charge une ressource de l'affilié courant et la recharge quand l'affilié ou les paramètres changent
 * (requête précédente annulée). À appeler dans un contexte d'injection (constructeur, initialiseur de champ).
 */
export function parAffilie<T, P = void>(
  requete: (affilieId: string, parametres: P) => Observable<T>,
  parametres?: Signal<P>,
): Chargeable<T> {
  const contexte = inject(AffilieContext);
  contexte.charger();
  const relance = signal(0);
  const cle = computed(() => ({
    affilieId: contexte.affilieId(),
    parametres: parametres?.() as P,
    relance: relance(),
  }));
  const etat = toSignal(
    toObservable(cle).pipe(
      filter(
        (c): c is { affilieId: string; parametres: P; relance: number } => c.affilieId !== null,
      ),
      switchMap((c) => charger(requete(c.affilieId, c.parametres))),
    ),
    { initialValue: CHARGEMENT as Etat<T> },
  );
  return { etat, recharger: () => relance.update((n) => n + 1) };
}
