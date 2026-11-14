using Sepp.Testing.Contrats;

namespace Sepp.Documents.Integration.Tests;

/// <summary>Contrat OpenAPI de Documents (INT-01) : instantané docs/api/openapi/documents.json.</summary>
public sealed class ContratOpenApiTests
{
    [Fact]
    [Trait("Categorie", ContratOpenApi.Categorie)]
    public Task Le_document_OpenAPI_correspond_a_l_instantane_versionne() =>
        ContratOpenApi.VerifierAsync<Program>("documents", TestContext.Current.CancellationToken, "Documents");
}
