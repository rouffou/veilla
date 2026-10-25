namespace Sepp.Bff.Interne.Ecrans;

/// <summary>Dossier d'un travailleur : identité, occupations, affectations, obligations, rendez-vous, décisions résumées, documents et envois (lot A).</summary>
internal static class EcranDossierTravailleur
{
    public const string Nom = "DossierTravailleur";

    public static RouteGroupBuilder MapDossierTravailleur(this RouteGroupBuilder ecrans)
    {
        ecrans.MapGet("/travailleurs/{personneId:guid}", (Guid personneId) => EcransInterneEndpoints.NonImplemente(Nom))
            .WithName("EcranDossierTravailleur")
            .WithSummary("Dossier travailleur (lot A ; 501 tant qu'il n'est pas réalisé).")
            .ProducesProblem(StatusCodes.Status501NotImplemented);
        return ecrans;
    }
}
