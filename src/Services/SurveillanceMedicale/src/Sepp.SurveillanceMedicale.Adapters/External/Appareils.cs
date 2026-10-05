using System.Globalization;

using Sepp.SurveillanceMedicale.Application;
using Sepp.SurveillanceMedicale.Domain.Examens;

namespace Sepp.SurveillanceMedicale.Adapters.External;

// SAN-21 : import direct des appareils. Les messages d'erreur ne reprennent jamais une valeur lue (contenu clinique) :
// seulement des numéros de ligne ou de segment.

/// <summary>
/// Résultats d'appareils au format HL7 v2 (message ORU^R01) : un segment <c>OBX</c> par mesure,
/// <c>OBX|n|NM|CODE^Libellé|…|valeur|unité|plage|…</c>. Le code d'observation (OBX-3, premier composant) est le code de
/// mesure du protocole (par ex. <c>AUDIO.PERTE_4000_OD</c>) ; les correspondances LOINC des appareils réels sont à
/// paramétrer lors de leur intégration (pilotes hors périmètre).
/// </summary>
public sealed class ImportHl7 : IImportAppareil
{
    public string Format => "HL7";

    public IReadOnlyList<Mesure> Lire(TypeActe typeActe, string contenu)
    {
        var segments = contenu.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || !segments[0].StartsWith("MSH|", StringComparison.Ordinal))
        {
            throw new FormatException("Message HL7 invalide : le segment MSH est absent.");
        }

        var mesures = new List<Mesure>();
        for (var i = 0; i < segments.Length; i++)
        {
            if (!segments[i].StartsWith("OBX|", StringComparison.Ordinal))
            {
                continue;
            }

            var champs = segments[i].Split('|');
            if (champs.Length < 6 || string.IsNullOrWhiteSpace(champs[3]))
            {
                throw new FormatException($"Segment OBX n° {i + 1} incomplet.");
            }

            var code = champs[3].Split('^')[0].Trim();
            var unite = champs.Length > 6 ? champs[6].Split('^')[0].Trim() : null;
            mesures.Add(champs[2] == "NM"
                ? new Mesure(code, Nombre(champs[5], $"segment OBX n° {i + 1}"), string.IsNullOrEmpty(unite) ? null : unite, null)
                : new Mesure(code, null, string.IsNullOrEmpty(unite) ? null : unite, champs[5]));
        }

        return mesures.Count > 0 ? mesures : throw new FormatException("Le message HL7 ne contient aucun résultat (segment OBX).");
    }

    internal static decimal Nombre(string valeur, string position) =>
        decimal.TryParse(valeur.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var nombre)
            ? nombre
            : throw new FormatException($"Valeur numérique illisible ({position}).");
}

/// <summary>Fichier exporté par un appareil : une mesure par ligne, <c>code;valeur;unité</c> (en-tête facultatif).</summary>
public sealed class ImportFichierCsv : IImportAppareil
{
    public string Format => "CSV";

    public IReadOnlyList<Mesure> Lire(TypeActe typeActe, string contenu)
    {
        var lignes = contenu.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var mesures = new List<Mesure>();
        for (var i = 0; i < lignes.Length; i++)
        {
            var colonnes = lignes[i].Split(';');
            if (i == 0 && colonnes[0].Equals("code", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (colonnes.Length < 2)
            {
                throw new FormatException($"Ligne {i + 1} : attendu « code;valeur;unité ».");
            }

            mesures.Add(new Mesure(colonnes[0].Trim(), ImportHl7.Nombre(colonnes[1].Trim(), $"ligne {i + 1}"), colonnes.Length > 2 ? colonnes[2].Trim() : null, null));
        }

        return mesures.Count > 0 ? mesures : throw new FormatException("Le fichier ne contient aucune mesure.");
    }
}

/// <summary>
/// Simulateur d'appareil (NF-14 : données fictives) pour le développement et les démonstrations : renvoie des valeurs
/// déterministes et normales pour chaque type d'acte ; le contenu peut forcer une valeur (<c>CODE=valeur</c>).
/// </summary>
public sealed class SimulateurAppareil : IImportAppareil
{
    private static readonly Dictionary<TypeActe, (string Code, decimal Valeur, string Unite)[]> Valeurs = new()
    {
        [TypeActe.Biometrie] = [("BIOM.IMC", 23.4m, "kg/m2"), ("BIOM.TA_SYS", 122m, "mmHg"), ("BIOM.TA_DIA", 78m, "mmHg")],
        [TypeActe.Vision] = [("VISION.ACUITE_OD", 10m, "/10"), ("VISION.ACUITE_OG", 10m, "/10")],
        [TypeActe.Audiometrie] = [("AUDIO.PERTE_4000_OD", 10m, "dB"), ("AUDIO.PERTE_4000_OG", 15m, "dB")],
        [TypeActe.Spirometrie] = [("SPIRO.VEMS_PCT", 98m, "%"), ("SPIRO.TIFFENEAU", 81m, "%")],
        [TypeActe.Ecg] = [("ECG.FREQUENCE", 68m, "bpm")],
        [TypeActe.Biologie] = [("BIO.GLYCEMIE", 92m, "mg/dL")],
    };

    public string Format => "SIMULATEUR";

    public IReadOnlyList<Mesure> Lire(TypeActe typeActe, string contenu)
    {
        var forcees = contenu.Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split('='))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim().ToUpperInvariant(), p => ImportHl7.Nombre(p[1].Trim(), "valeur forcée"), StringComparer.Ordinal);
        return [.. Valeurs[typeActe].Select(v => new Mesure(v.Code, forcees.GetValueOrDefault(v.Code, v.Valeur), v.Unite, null))];
    }
}
