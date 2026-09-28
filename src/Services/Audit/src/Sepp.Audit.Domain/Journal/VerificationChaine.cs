namespace Sepp.Audit.Domain.Journal;

public enum TypeAnomalie
{
    /// <summary>Un numéro manque : une ou plusieurs lignes ont été supprimées.</summary>
    EntreeManquante,

    /// <summary>L'empreinte précédente ne correspond pas à la ligne précédente : ligne supprimée, insérée ou réécrite.</summary>
    ChainageRompu,

    /// <summary>Le contenu de la ligne ne correspond plus à son empreinte : ligne modifiée.</summary>
    EmpreinteInvalide,
}

public sealed record AnomalieChaine(long Numero, TypeAnomalie Type, string Message);

/// <summary>
/// Vérification de l'intégrité d'une chaîne d'audit (NF-04) : les entrées d'une zone sont présentées dans
/// l'ordre des numéros, à partir du maillon de départ (origine, ou sceau de la dernière purge légale).
/// La vérification s'arrête à la première anomalie.
/// </summary>
public sealed class VerificationChaine(Zone zone, MaillonChaine depart)
{
    public Zone Zone { get; } = zone;

    public MaillonChaine Depart { get; } = depart;

    public MaillonChaine Dernier { get; private set; } = depart;

    public long EntreesVerifiees { get; private set; }

    public AnomalieChaine? Anomalie { get; private set; }

    public bool EstIntegre => Anomalie is null;

    /// <returns><c>false</c> dès qu'une anomalie est constatée (la vérification est alors terminée).</returns>
    public bool Verifier(EntreeAudit entree)
    {
        if (Anomalie is not null)
        {
            return false;
        }

        if (entree.Zone != Zone)
        {
            throw new InvalidOperationException($"L'entrée {entree.Id} n'appartient pas à la zone {Zone}.");
        }

        var attendu = Dernier.Numero + 1;
        if (entree.Numero != attendu)
        {
            Anomalie = new AnomalieChaine(attendu, TypeAnomalie.EntreeManquante,
                $"Entrée n° {attendu} absente (entrée suivante trouvée : n° {entree.Numero}).");
        }
        else if (!string.Equals(entree.EmpreintePrecedente, Dernier.Empreinte, StringComparison.Ordinal))
        {
            Anomalie = new AnomalieChaine(entree.Numero, TypeAnomalie.ChainageRompu,
                $"L'entrée n° {entree.Numero} ne se rattache pas à l'entrée n° {Dernier.Numero}.");
        }
        else if (!entree.EstIntegre())
        {
            Anomalie = new AnomalieChaine(entree.Numero, TypeAnomalie.EmpreinteInvalide,
                $"Le contenu de l'entrée n° {entree.Numero} ne correspond plus à son empreinte.");
        }
        else
        {
            Dernier = entree.Maillon;
            EntreesVerifiees++;
            return true;
        }

        return false;
    }
}
