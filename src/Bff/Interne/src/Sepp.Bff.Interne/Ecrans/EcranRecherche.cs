namespace Sepp.Bff.Interne.Ecrans;

/// <summary>Écran de recherche des travailleurs et des affiliés (lot A, épique #389).</summary>
internal static class EcranRecherche
{
    public const string Nom = "Recherche";

    public static RouteGroupBuilder MapRecherche(this RouteGroupBuilder ecrans)
    {
        ecrans.MapGet("/recherche", (string? q) => EcransInterneEndpoints.NonImplemente(Nom))
            .WithName("EcranRecherche")
            .WithSummary("Recherche de travailleurs et d'affiliés (lot A ; 501 tant qu'il n'est pas réalisé).")
            .ProducesProblem(StatusCodes.Status501NotImplemented);
        return ecrans;
    }
}
