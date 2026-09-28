using System.Globalization;
using System.Text;

using Sepp.Personnes.Application.Imports;

namespace Sepp.Personnes.Adapters.Imports;

/// <summary>
/// Lecture d'un fichier CSV d'import (AFF-21) : séparateur « ; » (Excel en Belgique) ou « , » détecté sur l'en-tête,
/// champs entre guillemets (RFC 4180), en-têtes normalisés (minuscules, sans accents, espaces → « _ »).
/// Les fichiers Excel sont enregistrés au format « CSV UTF-8 » avant l'import : aucune bibliothèque tierce
/// n'analyse de fichier bureautique non fiable.
/// </summary>
public static class CsvLecteur
{
    public sealed record Fichier(IReadOnlyList<string> Colonnes, IReadOnlyList<LigneImport> Lignes);

    public static Fichier Lire(string contenu)
    {
        var enregistrements = Decouper(contenu.TrimStart('﻿')).Where(e => e.Champs.Any(c => c.Length > 0)).ToList();
        if (enregistrements.Count == 0)
        {
            return new Fichier([], []);
        }

        var colonnes = enregistrements[0].Champs.Select(Normaliser).ToList();
        var lignes = enregistrements.Skip(1)
            .Select(e => new LigneImport(
                e.Ligne,
                colonnes.Select((c, i) => (c, v: i < e.Champs.Count ? e.Champs[i] : string.Empty))
                    .Where(x => x.c.Length > 0)
                    .GroupBy(x => x.c, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First().v, StringComparer.Ordinal)))
            .ToList();
        return new Fichier(colonnes.Where(c => c.Length > 0).ToList(), lignes);
    }

    private static string Normaliser(string entete)
    {
        var decompose = entete.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decompose.Length);
        foreach (var c in decompose)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            sb.Append(c is ' ' or '-' ? '_' : c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed record Enregistrement(int Ligne, List<string> Champs);

    private static IEnumerable<Enregistrement> Decouper(string contenu)
    {
        var premiereLigne = contenu.Split('\n', 2)[0];
        var separateur = premiereLigne.Count(c => c == ';') >= premiereLigne.Count(c => c == ',') ? ';' : ',';

        var champs = new List<string>();
        var champ = new StringBuilder();
        var entreGuillemets = false;
        var ligne = 1;
        var debut = 1;
        for (var i = 0; i < contenu.Length; i++)
        {
            var c = contenu[i];
            if (entreGuillemets)
            {
                if (c == '"' && i + 1 < contenu.Length && contenu[i + 1] == '"')
                {
                    champ.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    entreGuillemets = false;
                }
                else
                {
                    if (c == '\n')
                    {
                        ligne++;
                    }

                    champ.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when champ.Length == 0:
                    entreGuillemets = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    champs.Add(champ.ToString());
                    champ.Clear();
                    yield return new Enregistrement(debut, champs);
                    champs = [];
                    ligne++;
                    debut = ligne;
                    break;
                default:
                    if (c == separateur)
                    {
                        champs.Add(champ.ToString());
                        champ.Clear();
                    }
                    else
                    {
                        champ.Append(c);
                    }

                    break;
            }
        }

        if (champ.Length > 0 || champs.Count > 0)
        {
            champs.Add(champ.ToString());
            yield return new Enregistrement(debut, champs);
        }
    }
}
