namespace Sepp.Bff.Interne.Ecrans;

/// <summary>Agenda jour et semaine des ressources (lot B, fuseau Europe/Brussels).</summary>
internal static class EcranAgenda
{
    public const string Nom = "Agenda";

    public static RouteGroupBuilder MapAgenda(this RouteGroupBuilder ecrans)
    {
        ecrans.MapGet("/agenda", () => EcransInterneEndpoints.NonImplemente(Nom))
            .WithName("EcranAgenda")
            .WithSummary("Agenda (lot B ; 501 tant qu'il n'est pas réalisé).")
            .ProducesProblem(StatusCodes.Status501NotImplemented);
        return ecrans;
    }
}
