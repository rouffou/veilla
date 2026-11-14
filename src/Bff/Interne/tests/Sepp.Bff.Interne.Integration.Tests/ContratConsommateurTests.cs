using Sepp.Testing.Contrats;

namespace Sepp.Bff.Interne.Integration.Tests;

/// <summary>
/// Contrat consommateur-fournisseur du BFF Interne (ARC-23) : appels des clients typés (<c>Aval/ApisAval.cs</c>) déclarés puis
/// confrontés à l'instantané OpenAPI du service. Le relais générique (YARP, <c>Relais/RelaisServices.cs</c>) ne consomme aucune route
/// précise : il transmet les appels des écrans internes tels quels, son contrat est celui des services eux-mêmes.
/// </summary>
public sealed class ContratConsommateurTests
{
    private const string Bff = "bff-interne";

    public static IReadOnlyList<AttenteConsommateur> Attentes { get; } =
    [
        new(Bff, "personnes", "GET", "/api/v1/personnes/{id}") { ChampsReponse = ["id", "nom", "prenom"] },
        new(Bff, "affilies", "GET", "/api/v1/affilies/{id}") { ChampsReponse = ["id", "fiche"] },
    ];

    [Fact]
    [Trait("Categorie", "ContratConsommateur")]
    public void Les_appels_du_BFF_sont_satisfaits_par_les_contrats_des_services() =>
        ContratConsommateur.Verifier(Attentes).ExigerSansEcart();
}
