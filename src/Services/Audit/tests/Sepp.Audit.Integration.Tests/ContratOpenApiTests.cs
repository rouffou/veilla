using Sepp.Testing.Contrats;

namespace Sepp.Audit.Integration.Tests;

/// <summary>Contrat OpenAPI de Audit (INT-01) : instantané docs/api/openapi/audit.json.</summary>
public sealed class ContratOpenApiTests
{
    [Fact]
    [Trait("Categorie", ContratOpenApi.Categorie)]
    public Task Le_document_OpenAPI_correspond_a_l_instantane_versionne() =>
        ContratOpenApi.VerifierAsync<Program>("audit", TestContext.Current.CancellationToken, "Audit");
}
