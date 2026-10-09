using Microsoft.Extensions.Time.Testing;

namespace Sepp.Sagas.Tests.Plateforme;

/// <summary>
/// Horloge unique des cinq hôtes. Elle délègue à un <see cref="FakeTimeProvider"/> neuf à chaque scénario : le scénario
/// choisit sa date de départ (indépendante des autres) puis fait avancer le temps, ce qui déclenche les minuteries de
/// reprise (ADR 0008) sans attente réelle.
/// </summary>
public sealed class HorlogeSaga : TimeProvider
{
    private FakeTimeProvider _courante = new(new DateTimeOffset(2027, 1, 4, 9, 0, 0, TimeSpan.Zero));

    /// <summary>Faux temps du scénario courant.</summary>
    public FakeTimeProvider Faux => _courante;

    public override TimeZoneInfo LocalTimeZone => _courante.LocalTimeZone;

    public FakeTimeProvider Demarrer(DateTimeOffset debut) => _courante = new FakeTimeProvider(debut);

    public override DateTimeOffset GetUtcNow() => _courante.GetUtcNow();

    public override long GetTimestamp() => _courante.GetTimestamp();

    public override long TimestampFrequency => _courante.TimestampFrequency;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        _courante.CreateTimer(callback, state, dueTime, period);
}
