using Sepp.Testing.Contrats;

namespace Sepp.Documents.Integration.Tests;

/// <summary>
/// Contrat consommateur-fournisseur du service Documents (ARC-23) : source linguistique (NF-41, <c>Externe/Adaptateurs.cs</c>) lue chez Affiliés et Personnes, et contenu du dossier de santé (SAN-43, SAN-50, <c>Externe/SourceDossierSanteHttp.cs</c>) lu chez Surveillance médicale, avec le compte technique. Le jeton OIDC vient du fournisseur d'identité, hors contrat OpenAPI interne.
/// Les attentes sont confrontées à l'instantané OpenAPI du service appelé (<c>docs/api/openapi/</c>) ; une évolution du fournisseur
/// qui supprime ou renomme ce que Documents utilise fait échouer ce test. Tout nouvel appel HTTP interne s'ajoute à cette liste.
/// </summary>
public sealed class ContratConsommateurTests
{
    private const string Consommateur = "documents";

    public static IReadOnlyList<AttenteConsommateur> Attentes { get; } =
    [
        new(Consommateur, "affilies", "GET", "/api/v1/affilies/{id}") { ChampsReponse = ["fiche"] },
        new(Consommateur, "personnes", "GET", "/api/v1/personnes/{id}") { ChampsReponse = ["langue"] },
        new(Consommateur, "surveillance-medicale", "GET", "/api/v1/demandes-copie/{id}/contenu"),
        new(Consommateur, "surveillance-medicale", "GET", "/api/v1/vaccinations/{id}/carnet"),
    ];

    [Fact]
    [Trait("Categorie", "ContratConsommateur")]
    public void Les_appels_HTTP_internes_sont_satisfaits_par_les_contrats_des_fournisseurs() =>
        ContratConsommateur.Verifier(Attentes).ExigerSansEcart();
}
