using Sepp.Testing.Contrats;

namespace Sepp.Communications.Integration.Tests;

/// <summary>
/// Contrat consommateur-fournisseur du service Communications (ARC-23) : annuaire des destinataires (coordonnées, langue, canal préféré) lu chez Personnes et Affiliés avec le compte technique (<c>Annuaire/Annuaires.cs</c>). Le jeton OIDC vient du fournisseur d'identité, hors contrat OpenAPI interne.
/// Les attentes sont confrontées à l'instantané OpenAPI du service appelé (<c>docs/api/openapi/</c>) ; une évolution du fournisseur
/// qui supprime ou renomme ce que Communications utilise fait échouer ce test. Tout nouvel appel HTTP interne s'ajoute à cette liste.
/// </summary>
public sealed class ContratConsommateurTests
{
    private const string Consommateur = "communications";

    public static IReadOnlyList<AttenteConsommateur> Attentes { get; } =
    [
        new(Consommateur, "personnes", "GET", "/api/v1/personnes/{id}") { ChampsReponse = ["prenom", "nom", "email", "telephone", "adresse", "canalPrefere", "langue"] },
        new(Consommateur, "affilies", "GET", "/api/v1/affilies/{id}") { ChampsReponse = ["fiche", "contacts", "unitesEtablissement"] },
    ];

    [Fact]
    [Trait("Categorie", "ContratConsommateur")]
    public void Les_appels_HTTP_internes_sont_satisfaits_par_les_contrats_des_fournisseurs() =>
        ContratConsommateur.Verifier(Attentes).ExigerSansEcart();
}
