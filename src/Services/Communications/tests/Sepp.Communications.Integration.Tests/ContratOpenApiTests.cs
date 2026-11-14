using Sepp.Testing.Contrats;

namespace Sepp.Communications.Integration.Tests;

/// <summary>Contrat OpenAPI de Communications (INT-01) : instantané docs/api/openapi/communications.json.</summary>
public sealed class ContratOpenApiTests
{
    [Fact]
    [Trait("Categorie", ContratOpenApi.Categorie)]
    public Task Le_document_OpenAPI_correspond_a_l_instantane_versionne() =>
        ContratOpenApi.VerifierAsync<Program>("communications", TestContext.Current.CancellationToken, "Communications");
}
