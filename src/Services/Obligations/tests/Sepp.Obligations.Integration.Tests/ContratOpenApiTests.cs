using Sepp.Testing.Contrats;

namespace Sepp.Obligations.Integration.Tests;

/// <summary>Contrat OpenAPI de Obligations (INT-01) : instantané docs/api/openapi/obligations.json.</summary>
public sealed class ContratOpenApiTests
{
    [Fact]
    [Trait("Categorie", ContratOpenApi.Categorie)]
    public Task Le_document_OpenAPI_correspond_a_l_instantane_versionne() =>
        ContratOpenApi.VerifierAsync<Program>("obligations", TestContext.Current.CancellationToken, "Obligations");
}
