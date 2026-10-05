using Sepp.BuildingBlocks.Domain;

namespace Sepp.Integrations.Domain.Flux;

/// <summary>
/// Position de lecture d'un flux entrant par lots (curseur opaque fourni par l'adaptateur de l'organisme) :
/// la prochaine récupération reprend après le dernier lot journalisé.
/// </summary>
public sealed class PositionFlux : AggregateRoot
{
    private PositionFlux()
    {
    }

    private PositionFlux(Guid id, TypeFlux flux) : base(id)
    {
        Flux = flux;
    }

    public TypeFlux Flux { get; private set; }

    public string? Position { get; private set; }

    public DateTimeOffset? MiseAJourLe { get; private set; }

    public static PositionFlux Initialiser(TypeFlux flux) =>
        Enum.IsDefined(flux) ? new PositionFlux(NewId(), flux) : throw new DomainException($"Flux inconnu : {flux}.");

    public bool Avancer(string? position, DateTimeOffset maintenant)
    {
        var nouvelle = Texte.Facultatif(position, "La position du flux", 200);
        if (nouvelle == Position)
        {
            return false;
        }

        Position = nouvelle;
        MiseAJourLe = maintenant;
        return true;
    }
}
