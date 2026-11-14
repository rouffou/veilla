using Sepp.Testing.Contrats;

namespace Sepp.Planification.Integration.Tests;

/// <summary>Contrat OpenAPI de Planification (INT-01) : instantané docs/api/openapi/planification.json.</summary>
public sealed class ContratOpenApiTests
{
    [Fact]
    [Trait("Categorie", ContratOpenApi.Categorie)]
    public Task Le_document_OpenAPI_correspond_a_l_instantane_versionne() =>
        ContratOpenApi.VerifierAsync<Program>("planification", TestContext.Current.CancellationToken, "Planification");
}
