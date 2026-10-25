import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { LanguageService, RUNTIME_CONFIG } from '@veilla/shared';
import { map, Observable } from 'rxjs';
import {
  Fichier,
  FicheAffilie,
  ListeNominative,
  ListeNominativeDetail,
  MesAffilies,
  Page,
  Poste,
  Proposition,
  PropositionListeCorps,
  PropositionPosteCorps,
  PropositionSoumise,
  Reprise,
  RepriseAnnoncee,
  RepriseCorps,
  Risque,
  TableauDeBord,
  Travailleur,
} from './bff-employeur.models';

/**
 * Client typé du BFF employeur (ARC-43). Le jeton est ajouté par `apiAuthInterceptor` ;
 * la langue active est transmise pour les libellés (risques) et l'export CSV.
 */
@Injectable({ providedIn: 'root' })
export class BffEmployeurService {
  private readonly http = inject(HttpClient);
  private readonly base = `${inject(RUNTIME_CONFIG).apiBaseUrl}/api/v1`;
  private readonly langues = inject(LanguageService);

  mesAffilies(): Observable<MesAffilies> {
    return this.http.get<MesAffilies>(`${this.base}/affilies`);
  }

  fiche(affilieId: string): Observable<FicheAffilie> {
    return this.http.get<FicheAffilie>(this.affilie(affilieId));
  }

  tableauDeBord(affilieId: string): Observable<TableauDeBord> {
    return this.http.get<TableauDeBord>(`${this.affilie(affilieId)}/tableau-de-bord`);
  }

  travailleurs(
    affilieId: string,
    recherche: string,
    page: number,
    taille: number,
  ): Observable<Page<Travailleur>> {
    let params = new HttpParams().set('page', page).set('taille', taille);
    if (recherche.trim()) {
      params = params.set('recherche', recherche.trim());
    }
    return this.http.get<Page<Travailleur>>(`${this.affilie(affilieId)}/travailleurs`, { params });
  }

  postes(affilieId: string): Observable<readonly Poste[]> {
    return this.http.get<readonly Poste[]>(`${this.affilie(affilieId)}/postes`, {
      params: this.langue(),
    });
  }

  risques(): Observable<readonly Risque[]> {
    return this.http.get<readonly Risque[]>(`${this.base}/risques`, { params: this.langue() });
  }

  listesNominatives(affilieId: string): Observable<readonly ListeNominative[]> {
    return this.http.get<readonly ListeNominative[]>(
      `${this.affilie(affilieId)}/listes-nominatives`,
    );
  }

  listeNominative(affilieId: string, listeId: string): Observable<ListeNominativeDetail> {
    return this.http.get<ListeNominativeDetail>(
      `${this.affilie(affilieId)}/listes-nominatives/${encodeURIComponent(listeId)}`,
    );
  }

  /** POR-06 : export CSV produit par le BFF ; le nom du fichier vient de Content-Disposition. */
  telechargerListeCsv(affilieId: string, listeId: string): Observable<Fichier> {
    return this.http
      .get(`${this.affilie(affilieId)}/listes-nominatives/${encodeURIComponent(listeId)}/csv`, {
        params: this.langue(),
        responseType: 'blob',
        observe: 'response',
      })
      .pipe(
        map((response) => ({
          nom: nomDeFichier(response.headers.get('Content-Disposition')) ?? 'liste-nominative.csv',
          contenu: response.body ?? new Blob([], { type: 'text/csv' }),
        })),
      );
  }

  propositions(affilieId: string): Observable<readonly Proposition[]> {
    return this.http.get<readonly Proposition[]>(`${this.affilie(affilieId)}/propositions`);
  }

  proposerPoste(
    affilieId: string,
    posteId: string,
    corps: PropositionPosteCorps,
  ): Observable<PropositionSoumise> {
    return this.http.post<PropositionSoumise>(
      `${this.affilie(affilieId)}/postes/${encodeURIComponent(posteId)}/propositions`,
      corps,
    );
  }

  proposerListe(
    affilieId: string,
    listeId: string,
    corps: PropositionListeCorps,
  ): Observable<PropositionSoumise> {
    return this.http.post<PropositionSoumise>(
      `${this.affilie(affilieId)}/listes-nominatives/${encodeURIComponent(listeId)}/propositions`,
      corps,
    );
  }

  /** POR-04 : suivi des reprises annoncées. */
  reprises(affilieId: string): Observable<readonly Reprise[]> {
    return this.http.get<readonly Reprise[]>(`${this.affilie(affilieId)}/reprises`);
  }

  /** POR-04 : annonce d'une reprise (relayée par le BFF au service Obligations, jamais rejouée automatiquement). */
  annoncerReprise(affilieId: string, corps: RepriseCorps): Observable<RepriseAnnoncee> {
    return this.http.post<RepriseAnnoncee>(`${this.affilie(affilieId)}/reprises`, corps);
  }

  private affilie(affilieId: string): string {
    return `${this.base}/affilies/${encodeURIComponent(affilieId)}`;
  }

  private langue(): HttpParams {
    return new HttpParams().set('langue', this.langues.current());
  }
}

/** Extrait le nom de fichier d'un en-tête Content-Disposition (filename* RFC 5987 prioritaire). */
export function nomDeFichier(disposition: string | null): string | null {
  if (!disposition) return null;
  const etendu = /filename\*\s*=\s*(?:UTF-8|utf-8)''([^;]+)/.exec(disposition);
  if (etendu) {
    try {
      return decodeURIComponent(etendu[1].trim());
    } catch {
      return etendu[1].trim();
    }
  }
  const simple = /filename\s*=\s*"?([^";]+)"?/.exec(disposition);
  return simple ? simple[1].trim() : null;
}
