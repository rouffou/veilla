using Sepp.Testing.Contrats;

namespace Sepp.Integrations.Integration.Tests;

/// <summary>Contrat OpenAPI de Integrations (INT-01) : instantané docs/api/openapi/integrations.json.</summary>
public sealed class ContratOpenApiTests
{
    [Fact]
    [Trait("Categorie", ContratOpenApi.Categorie)]
    public Task Le_document_OpenAPI_correspond_a_l_instantane_versionne() =>
        ContratOpenApi.VerifierAsync<Program>("integrations", TestContext.Current.CancellationToken, "Integrations");
}
