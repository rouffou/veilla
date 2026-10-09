import { Pipe, PipeTransform } from '@angular/core';

const LOCALES: Record<string, string> = { fr: 'fr-BE', nl: 'nl-BE', de: 'de-BE', en: 'en-GB' };

/**
 * Date dans la langue de l'interface (Intl, sans données de locale Angular à embarquer).
 * Une date ISO sans heure (« 2026-09-01 ») est lue comme date civile, sans décalage de fuseau.
 */
@Pipe({ name: 'dateLocale' })
export class DateLocalePipe implements PipeTransform {
  transform(valeur: string | null | undefined, langue: string, avecHeure = false): string {
    if (!valeur) return '—';
    const civile = /^(\d{4})-(\d{2})-(\d{2})$/.exec(valeur);
    const date = civile
      ? new Date(Number(civile[1]), Number(civile[2]) - 1, Number(civile[3]))
      : new Date(valeur);
    if (Number.isNaN(date.getTime())) return valeur;
    return new Intl.DateTimeFormat(
      LOCALES[langue] ?? 'fr-BE',
      avecHeure && !civile ? { dateStyle: 'medium', timeStyle: 'short' } : { dateStyle: 'medium' },
    ).format(date);
  }
}
