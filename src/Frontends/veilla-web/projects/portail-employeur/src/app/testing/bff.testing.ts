/*
 * Utilitaires des tests du portail employeur : BFF simulé par HttpTestingController (aucun appel réseau).
 */
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { EnvironmentProviders, Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Routes } from '@angular/router';
import { provideVeillaTesting, TEST_RUNTIME_CONFIG } from '@veilla/shared/testing';
import { AffilieResume } from '../api/bff-employeur.models';

export const BFF = `${TEST_RUNTIME_CONFIG.apiBaseUrl}/api/v1`;

export const AFFILIE: AffilieResume = {
  id: '0192a5c8-0000-7000-8000-000000000001',
  numeroBce: '0123.456.749',
  denomination: 'Boulangerie Dupont',
  statut: 'Actif',
};

export function providePortailTesting(routes: Routes = []): (Provider | EnvironmentProviders)[] {
  return [...provideVeillaTesting({ routes }), provideHttpClient(), provideHttpClientTesting()];
}

export function bff(): HttpTestingController {
  return TestBed.inject(HttpTestingController);
}

/** Répond à la liste des affiliés de l'utilisateur (premier appel de chaque écran). */
export function repondreAffilies(affilies: readonly AffilieResume[] = [AFFILIE]): void {
  bff().expectOne(`${BFF}/affilies`).flush({ affilies, introuvables: [] });
}
