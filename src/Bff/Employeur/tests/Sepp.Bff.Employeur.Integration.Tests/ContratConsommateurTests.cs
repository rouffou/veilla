using Sepp.Testing.Contrats;

namespace Sepp.Bff.Employeur.Integration.Tests;

/// <summary>
/// Contrat consommateur-fournisseur du BFF Employeur (ARC-23) : chaque appel HTTP émis par <c>Aval/ApisAval.cs</c> est déclaré ici
/// puis confronté à l'instantané OpenAPI du service (<c>docs/api/openapi/</c>). Tout nouvel appel du BFF s'ajoute à cette liste.
/// </summary>
public sealed class ContratConsommateurTests
{
    private const string Bff = "bff-employeur";

    public static IReadOnlyList<AttenteConsommateur> Attentes { get; } =
    [
        // Obligations : reprise du travail (ARC-33, POR-04)
        new(Bff, "obligations", "POST", "/api/v1/reprises") { CorpsRequete = ["personneId", "affilieId", "dateReprise", "debutAbsence"] },
        new(Bff, "obligations", "GET", "/api/v1/reprises") { Query = ["affilieId"] },
        new(Bff, "obligations", "GET", "/api/v1/reprises/{id}"),

        // Affiliés, Personnes (AFF-01 à AFF-03, AFF-20 à AFF-23)
        new(Bff, "affilies", "GET", "/api/v1/affilies/{id}") { ChampsReponse = ["fiche", "unitesEtablissement", "contacts"] },
        new(Bff, "personnes", "GET", "/api/v1/affilies/{id}/travailleurs") { Query = ["date"] },
        new(Bff, "personnes", "GET", "/api/v1/personnes/{id}") { ChampsReponse = ["id", "nom", "prenom", "dateNaissance"] },

        // Postes et risques (AFF-10 à AFF-14, AFF-30, AFF-31)
        new(Bff, "postes-risques", "GET", "/api/v1/postes") { Query = ["affilieId"] },
        new(Bff, "postes-risques", "GET", "/api/v1/postes/{id}"),
        new(Bff, "postes-risques", "GET", "/api/v1/risques") { Query = ["langue"] },
        new(Bff, "postes-risques", "GET", "/api/v1/listes-nominatives") { Query = ["affilieId"] },
        new(Bff, "postes-risques", "GET", "/api/v1/listes-nominatives/{id}"),
        new(Bff, "postes-risques", "GET", "/api/v1/propositions-poste-risque") { Query = ["affilieId"] },
        new(Bff, "postes-risques", "GET", "/api/v1/propositions-liste-nominative") { Query = ["affilieId"] },
        new(Bff, "postes-risques", "POST", "/api/v1/postes/{id}/propositions") { CorpsRequete = ["motif", "valideDu", "lignes", "avisCppt"] },
        new(Bff, "postes-risques", "POST", "/api/v1/listes-nominatives/{id}/propositions") { CorpsRequete = ["motif", "lignes"] },
    ];

    [Fact]
    [Trait("Categorie", "ContratConsommateur")]
    public void Les_appels_du_BFF_sont_satisfaits_par_les_contrats_des_services() =>
        ContratConsommateur.Verifier(Attentes).ExigerSansEcart();
}
