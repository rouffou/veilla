using Sepp.Bff.Interne.Securite;

namespace Sepp.Bff.Interne.Ecrans;

/// <summary>
/// Écrans composites de l'application interne sous <c>/api/v1/ecrans</c> (ADR 0013), réservés aux profils internes. Chaque
/// écran vit dans son propre fichier, possédé par le lot qui le réalise (épique #389) ; tant qu'il n'est pas écrit, il
/// répond 501 <c>ecran.non-implemente</c>. Les écrans composent les services avec <see cref="Annuaire"/> et
/// <see cref="SectionTolerante"/> ; les autres appels de l'application passent par le relais (<c>/{service}/api/v1/…</c>).
/// </summary>
public static class EcransInterneEndpoints
{
    public const string Prefixe = "/api/v1/ecrans";

    public static IEndpointRouteBuilder MapEcransInterne(this IEndpointRouteBuilder app)
    {
        var ecrans = app.MapGroup(Prefixe).RequireAuthorization(PerimetreInterne.Politique).WithTags("Application interne — écrans");

        ecrans.MapRecherche();
        ecrans.MapDossierTravailleur();
        ecrans.MapAffilie();
        ecrans.MapAgenda();
        ecrans.MapConsultation();
        ecrans.MapTableauDeBord();
        return app;
    }

    /// <summary>Réponse d'un écran pas encore réalisé.</summary>
    internal static IResult NonImplemente(string ecran) =>
        Results.Problem(
            statusCode: StatusCodes.Status501NotImplemented,
            title: "ecran.non-implemente",
            detail: "Cet écran n'est pas encore disponible.",
            extensions: new Dictionary<string, object?> { ["code"] = "ecran.non-implemente", ["ecran"] = ecran });
}
