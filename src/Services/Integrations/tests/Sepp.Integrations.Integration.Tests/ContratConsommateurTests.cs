using Sepp.Testing.Contrats;

namespace Sepp.Integrations.Integration.Tests;

/// <summary>
/// Contrat consommateur-fournisseur du service Integrations (ARC-23) : API interne DIMONA, registre national et identification du service Personnes (AFF-20, AFF-22, SAN-60, <c>External/Personnes/PersonnesClient.cs</c>). Le NISS ne transite que dans les corps.
/// Les attentes sont confrontées à l'instantané OpenAPI du service appelé (<c>docs/api/openapi/</c>) ; une évolution du fournisseur
/// qui supprime ou renomme ce que Integrations utilise fait échouer ce test. Tout nouvel appel HTTP interne s'ajoute à cette liste.
/// </summary>
public sealed class ContratConsommateurTests
{
    private const string Consommateur = "integrations";

    public static IReadOnlyList<AttenteConsommateur> Attentes { get; } =
    [
        new(Consommateur, "personnes", "POST", "/api/v1/dimona/entrees") { CorpsRequete = ["referenceDimona", "niss", "identite", "affilieId", "affilieUtilisateurId", "typeTravailleur", "typeContrat", "dateDebut", "dateFin"] },
        new(Consommateur, "personnes", "POST", "/api/v1/dimona/{referenceDimona}/sortie") { CorpsRequete = ["dateFin"] },
        new(Consommateur, "personnes", "POST", "/api/v1/registre-national/mutations") { CorpsRequete = ["referenceMutation", "niss", "type", "dateEffet", "adresse", "nom", "prenom", "langue"] },
        new(Consommateur, "personnes", "POST", "/api/v1/identifications") { CorpsRequete = ["niss"], ChampsReponse = ["personneId"] },
    ];

    [Fact]
    [Trait("Categorie", "ContratConsommateur")]
    public void Les_appels_HTTP_internes_sont_satisfaits_par_les_contrats_des_fournisseurs() =>
        ContratConsommateur.Verifier(Attentes).ExigerSansEcart();
}
