namespace Sepp.Bff.Interne.Ecrans;

/// <summary>Tableau de bord par rôle et file de travail du CPMT (lot D).</summary>
internal static class EcranTableauDeBord
{
    public const string Nom = "TableauDeBord";

    public static RouteGroupBuilder MapTableauDeBord(this RouteGroupBuilder ecrans)
    {
        ecrans.MapGet("/tableau-de-bord", () => EcransInterneEndpoints.NonImplemente(Nom))
            .WithName("EcranTableauDeBord")
            .WithSummary("Tableau de bord (lot D ; 501 tant qu'il n'est pas réalisé).")
            .ProducesProblem(StatusCodes.Status501NotImplemented);
        return ecrans;
    }
}
