using Sepp.Documents.Domain.Documents;
using Sepp.Testing.Contrats;

using Shouldly;

namespace Sepp.Documents.Integration.Tests;

/// <summary>Contrat OpenAPI de Documents (INT-01) : instantané docs/api/openapi/documents.json.</summary>
public sealed class ContratOpenApiTests
{
    [Fact]
    [Trait("Categorie", ContratOpenApi.Categorie)]
    public Task Le_document_OpenAPI_correspond_a_l_instantane_versionne() =>
        ContratOpenApi.VerifierAsync<Program>("documents", TestContext.Current.CancellationToken, "Documents");

    [Fact]
    [Trait("Categorie", ContratOpenApi.Categorie)]
    public void Le_type_de_destinataire_des_reponses_est_publie_en_minuscules_comme_le_service_le_renvoie()
    {
        // #436, #476 : l'instantané est égal au document servi (test ci-dessus) ; ses valeurs sont celles de TypesDestinataire.Code.
        var propriete = ContratConsommateur.Instantane("documents")["components"]!["schemas"]!["DocumentDto"]!["properties"]!["typeDestinataire"]!;

        propriete["enum"]!.AsArray().Select(v => v!.GetValue<string>())
            .ShouldBe(Enum.GetValues<TypeDestinataire>().Select(t => t.Code()), ignoreOrder: false);
        propriete["enum"]!.AsArray().ShouldAllBe(v => v!.GetValue<string>().All(c => !char.IsUpper(c)));
    }
}
