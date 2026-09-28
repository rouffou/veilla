using Sepp.BuildingBlocks.Domain;

namespace Sepp.Audit.Domain.Journal;

/// <summary>
/// Sceau d'une purge légale : dernier maillon supprimé d'une zone. Écrit par la base elle-même lors de la purge
/// (déclencheur), en ajout seul ; la vérification de la chaîne reprend à partir de ce maillon (NF-04).
/// </summary>
public sealed class SceauPurge
{
    private SceauPurge()
    {
    }

    public SceauPurge(Zone zone, long numeroFinal, string empreinteFinale, long nombreEntrees, DateTimeOffset purgeLe)
    {
        Id = Guid.CreateVersion7();
        Zone = zone;
        NumeroFinal = numeroFinal;
        EmpreinteFinale = empreinteFinale;
        NombreEntrees = nombreEntrees;
        PurgeLe = purgeLe;
    }

    public Guid Id { get; private init; }

    public Zone Zone { get; private init; }

    public long NumeroFinal { get; private init; }

    public string EmpreinteFinale { get; private init; } = string.Empty;

    public long NombreEntrees { get; private init; }

    public DateTimeOffset PurgeLe { get; private init; }

    public MaillonChaine Maillon => new(NumeroFinal, EmpreinteFinale);
}

/// <summary>
/// Durée de conservation du journal (NF-04) : au moins 10 ans, paramétrable au-delà. Seules les entrées plus
/// anciennes sont purgées, par préfixe de chaîne ; aucune suppression unitaire n'est possible.
/// </summary>
public static class PolitiqueConservation
{
    public const int MinimumAnnees = 10;

    /// <summary>Instant avant lequel une entrée peut être purgée.</summary>
    public static DateTimeOffset LimitePurge(DateTimeOffset maintenant, int anneesConservation) =>
        anneesConservation < MinimumAnnees
            ? throw new DomainException($"La durée de conservation du journal d'audit est d'au moins {MinimumAnnees} ans (NF-04).")
            : maintenant.AddYears(-anneesConservation);
}
