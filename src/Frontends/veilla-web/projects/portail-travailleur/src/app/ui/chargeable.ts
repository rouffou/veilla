import { signal, Signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Observable, switchMap } from 'rxjs';
import { charger, CHARGEMENT, Etat } from '../api/bff-travailleur.service';

export interface Chargeable<T> {
  readonly etat: Signal<Etat<T>>;
  /** Relance la requête (bouton « Réessayer », après une écriture…). */
  recharger(): void;
}

/**
 * Charge une ressource du BFF et la recharge à la demande (requête précédente annulée).
 * À appeler dans un contexte d'injection (initialiseur de champ).
 */
export function chargeable<T>(requete: () => Observable<T>): Chargeable<T> {
  const relance = signal(0);
  const etat = toSignal(
    toObservable(relance).pipe(switchMap(() => charger(requete()))),
    { initialValue: CHARGEMENT as Etat<T> },
  );
  return { etat, recharger: () => relance.update((n) => n + 1) };
}
