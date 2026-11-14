using Sepp.Testing.Contrats;

namespace Sepp.Affilies.Integration.Tests;

/// <summary>Contrat OpenAPI de Affilies (INT-01) : instantané docs/api/openapi/affilies.json.</summary>
public sealed class ContratOpenApiTests
{
    [Fact]
    [Trait("Categorie", ContratOpenApi.Categorie)]
    public Task Le_document_OpenAPI_correspond_a_l_instantane_versionne() =>
        ContratOpenApi.VerifierAsync<Program>("affilies", TestContext.Current.CancellationToken, "Affilies");
}
