using Sepp.Testing.Contrats;

namespace Sepp.Affilies.Integration.Tests;

/// <summary>
/// Contrat consommateur-fournisseur du service Affilies (ARC-23) : lecture des données d'entreprise de la BCE chez Intégrations avec le compte technique (<c>Externe/IntegrationsHttp.cs</c>). Le jeton OIDC (client credentials) vient du fournisseur d'identité, hors contrat OpenAPI interne.
/// Les attentes sont confrontées à l'instantané OpenAPI du service appelé (<c>docs/api/openapi/</c>) ; une évolution du fournisseur
/// qui supprime ou renomme ce que Affilies utilise fait échouer ce test. Tout nouvel appel HTTP interne s'ajoute à cette liste.
/// </summary>
public sealed class ContratConsommateurTests
{
    private const string Consommateur = "affilies";

    public static IReadOnlyList<AttenteConsommateur> Attentes { get; } =
    [
        new(Consommateur, "integrations", "GET", "/api/v1/bce/entreprises/{numeroBce}") { ChampsReponse = ["numeroBce", "denomination", "formeJuridique", "codeNace", "dateExtraction", "unitesEtablissement"] },
    ];

    [Fact]
    [Trait("Categorie", "ContratConsommateur")]
    public void Les_appels_HTTP_internes_sont_satisfaits_par_les_contrats_des_fournisseurs() =>
        ContratConsommateur.Verifier(Attentes).ExigerSansEcart();
}
