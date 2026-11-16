using System.Diagnostics.Metrics;

using Microsoft.Extensions.Logging;

namespace Sepp.Personnes.Adapters.Persistence;

/// <summary>
/// Alerte de doublon de NISS (#514, P2-2) : deux personnes trouvées pour un même NISS, sous l'empreinte de la clé courante
/// et sous celle de la clé précédente de l'index aveugle. Ce cas ne se produit que pour un doublon créé hors de la
/// procédure de rotation. Il est journalisé en avertissement, avec les seuls identifiants techniques des deux lignes (ni
/// NISS ni empreinte), et compté par la métrique OpenTelemetry <see cref="NomCompteur"/>, sur laquelle porte l'alerte.
/// Les deux personnes sont à fusionner par le métier.
/// </summary>
public sealed partial class AlerteDoublonsNiss
{
    /// <summary>Meter du service (exporté par les valeurs par défaut des services : <c>Sepp.*</c>).</summary>
    public const string NomMeter = "Sepp.Personnes";

    /// <summary>Compteur des doublons de NISS détectés à la lecture.</summary>
    public const string NomCompteur = "sepp.personnes.niss.doublons";

    private readonly Counter<long> _compteur;
    private readonly ILogger<AlerteDoublonsNiss> _journal;

    public AlerteDoublonsNiss(IMeterFactory meters, ILogger<AlerteDoublonsNiss> journal)
    {
        ArgumentNullException.ThrowIfNull(meters);
        _journal = journal;
        _compteur = meters.Create(NomMeter).CreateCounter<long>(
            NomCompteur, unit: "{doublon}", description: "Deux personnes trouvées pour un même NISS (empreintes de deux clés de l'index aveugle).");
    }

    /// <summary>Signale un doublon : la personne retenue (empreinte de la clé courante) et l'autre ligne.</summary>
    public void Signaler(Guid retenue, Guid autre)
    {
        LogDoublon(_journal, retenue, autre);
        _compteur.Add(1);
    }

    [LoggerMessage(EventId = 5116, Level = LogLevel.Warning,
        Message = "Doublon de NISS : les personnes {PersonneRetenue} et {AutrePersonne} correspondent au même NISS (empreintes de deux clés de l'index aveugle). La première est retenue ; fusionner les deux personnes.")]
    private static partial void LogDoublon(ILogger journal, Guid personneRetenue, Guid autrePersonne);
}
