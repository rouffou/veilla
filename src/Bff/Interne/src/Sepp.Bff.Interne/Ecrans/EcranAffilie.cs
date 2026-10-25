namespace Sepp.Bff.Interne.Ecrans;

/// <summary>Fiche d'un affilié pour l'application interne (lot A).</summary>
internal static class EcranAffilie
{
    public const string Nom = "Affilie";

    public static RouteGroupBuilder MapAffilie(this RouteGroupBuilder ecrans)
    {
        ecrans.MapGet("/affilies/{affilieId:guid}", (Guid affilieId) => EcransInterneEndpoints.NonImplemente(Nom))
            .WithName("EcranAffilie")
            .WithSummary("Fiche affilié (lot A ; 501 tant qu'il n'est pas réalisé).")
            .ProducesProblem(StatusCodes.Status501NotImplemented);
        return ecrans;
    }
}
