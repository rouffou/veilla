/*
 * Utilitaires des tests du portail travailleur : BFF simulé par HttpTestingController (aucun appel réseau).
 */
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { EnvironmentProviders, Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Routes } from '@angular/router';
import { provideVeillaTesting, TEST_RUNTIME_CONFIG } from '@veilla/shared/testing';
import { RendezVous } from '../api/bff-travailleur.models';

export const BFF = `${TEST_RUNTIME_CONFIG.apiBaseUrl}/api/v1`;

export const AFFILIE = '0192a5c8-0000-7000-8000-000000000001';

export function rendezVous(id: string, debut: string, statut = 'Planifie'): RendezVous {
  return {
    id,
    affilieId: AFFILIE,
    lieuId: 'lieu-1',
    debut,
    fin: debut,
    typeActe: 'EVALUATION_PERIODIQUE',
    statut,
    motifAnnulation: null,
  };
}

export function provideTravailleurTesting(routes: Routes = []): (Provider | EnvironmentProviders)[] {
  return [...provideVeillaTesting({ routes }), provideHttpClient(), provideHttpClientTesting()];
}

export function bff(): HttpTestingController {
  return TestBed.inject(HttpTestingController);
}

export async function stable(fixture: { whenStable(): Promise<unknown> }): Promise<void> {
  await fixture.whenStable();
  await fixture.whenStable();
}
