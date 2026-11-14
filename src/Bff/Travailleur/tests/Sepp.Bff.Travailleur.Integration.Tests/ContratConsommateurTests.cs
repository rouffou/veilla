using Sepp.Testing.Contrats;

namespace Sepp.Bff.Travailleur.Integration.Tests;

/// <summary>
/// Contrat consommateur-fournisseur du BFF Travailleur (ARC-23) : chaque appel HTTP que le BFF émet vers un service
/// (<c>Aval/ApisAval.cs</c>, <c>Aval/DossierSanteAval.cs</c>) est déclaré ici, avec ses paramètres, les champs de son corps et les
/// champs de réponse qu'il lit, puis confronté à l'instantané OpenAPI du service (<c>docs/api/openapi/</c>). Une évolution d'un
/// service qui supprime ou renomme ce que le BFF utilise fait échouer ce test. Tout nouvel appel du BFF s'ajoute ici.
/// </summary>
public sealed class ContratConsommateurTests
{
    private const string Bff = "bff-travailleur";

    private static readonly string[] ChampsDocument =
        ["id", "codeModele", "langue", "typeDestinataire", "destinataireId", "exemplaire", "format", "taille", "date", "statut", "publieLe"];

    private static readonly string[] ChampsDemandeCopie =
        ["id", "personneId", "deposeeLe", "dateLimite", "statut", "enRetard", "traiteeLe", "motifRefus", "documentId", "publieeLe"];

    public static IReadOnlyList<AttenteConsommateur> Attentes { get; } =
    [
        // Planification (SAN-12, POR-11)
        new(Bff, "planification", "GET", "/api/v1/reservations/rendez-vous"),
        new(Bff, "planification", "GET", "/api/v1/reservations/creneaux") { Query = ["affilieId", "typeActe", "du", "au", "lieuId"] },
        new(Bff, "planification", "POST", "/api/v1/reservations") { CorpsRequete = ["creneauId", "personneId", "affilieId", "obligationIds"] },
        new(Bff, "planification", "POST", "/api/v1/reservations/rendez-vous/{id}/annulation") { CorpsRequete = ["raison", "commentaire"] },
        new(Bff, "planification", "POST", "/api/v1/reservations/rendez-vous/{id}/deplacement") { CorpsRequete = ["nouveauCreneauId"] },
        new(Bff, "planification", "POST", "/api/v1/reservations/rendez-vous/{id}/confirmation") { CorpsRequete = [] },

        // Surveillance médicale (SAN-22, SAN-43, POR-12, POR-13)
        new(Bff, "surveillance-medicale", "GET", "/api/v1/protocoles/questionnaires") { Query = ["langue"] },
        new(Bff, "surveillance-medicale", "POST", "/api/v1/questionnaires/pre-remplissage") { CorpsRequete = ["personneId", "modeleCode", "reponses"] },
        new(Bff, "surveillance-medicale", "POST", "/api/v1/demandes-copie") { CorpsRequete = [], ChampsReponse = ChampsDemandeCopie },
        new(Bff, "surveillance-medicale", "GET", "/api/v1/demandes-copie/miennes") { ChampsReponse = ChampsDemandeCopie },

        // Personnes, Affiliés, Obligations (POR-11, POR-12)
        new(Bff, "personnes", "GET", "/api/v1/mes-occupations") { ChampsReponse = ["occupationId", "affilieId", "typeTravailleur", "dateDebut", "dateFin"] },
        new(Bff, "affilies", "GET", "/api/v1/mes-employeurs") { ChampsReponse = ["affilieId", "denomination", "langue"] },
        new(Bff, "obligations", "POST", "/api/v1/mes-demandes") { CorpsRequete = ["affilieId", "type"] },
        new(Bff, "obligations", "GET", "/api/v1/mes-examens-a-planifier") { Query = ["langue"] },

        // Documents, instances standard et médicale (même contrat : décision #356). #436 : « typeDestinataire » est publié en minuscules.
        new(Bff, "documents", "GET", "/api/v1/documents") { Query = ["typeDestinataire", "destinataireId"], ChampsReponse = ChampsDocument },
        new(Bff, "documents", "GET", "/api/v1/documents/{id}") { ChampsReponse = ChampsDocument },
        new(Bff, "documents", "GET", "/api/v1/documents/{id}/contenu"),
    ];

    [Fact]
    [Trait("Categorie", "ContratConsommateur")]
    public void Les_appels_du_BFF_sont_satisfaits_par_les_contrats_des_services() =>
        ContratConsommateur.Verifier(Attentes).ExigerSansEcart();
}
