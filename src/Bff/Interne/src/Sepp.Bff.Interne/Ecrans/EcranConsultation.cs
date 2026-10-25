namespace Sepp.Bff.Interne.Ecrans;

/// <summary>Vue de consultation d'un travailleur par le CPMT ou l'infirmier (lot C, zone médicale : motif d'accès exigé par le service).</summary>
internal static class EcranConsultation
{
    public const string Nom = "Consultation";

    public static RouteGroupBuilder MapConsultation(this RouteGroupBuilder ecrans)
    {
        ecrans.MapGet("/consultation/{personneId:guid}", (Guid personneId) => EcransInterneEndpoints.NonImplemente(Nom))
            .WithName("EcranConsultation")
            .WithSummary("Consultation (lot C ; 501 tant qu'il n'est pas réalisé).")
            .ProducesProblem(StatusCodes.Status501NotImplemented);
        return ecrans;
    }
}
