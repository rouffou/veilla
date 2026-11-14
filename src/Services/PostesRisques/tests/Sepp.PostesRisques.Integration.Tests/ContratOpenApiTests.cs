using Sepp.Testing.Contrats;

namespace Sepp.PostesRisques.Integration.Tests;

/// <summary>Contrat OpenAPI de PostesRisques (INT-01) : instantané docs/api/openapi/postes-risques.json.</summary>
public sealed class ContratOpenApiTests
{
    [Fact]
    [Trait("Categorie", ContratOpenApi.Categorie)]
    public Task Le_document_OpenAPI_correspond_a_l_instantane_versionne() =>
        ContratOpenApi.VerifierAsync<Program>("postes-risques", TestContext.Current.CancellationToken, "PostesRisques");
}
