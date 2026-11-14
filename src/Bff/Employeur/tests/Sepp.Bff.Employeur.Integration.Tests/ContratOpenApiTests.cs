using Sepp.Testing.Contrats;

namespace Sepp.Bff.Employeur.Integration.Tests;

/// <summary>Contrat OpenAPI de BFF Employeur (INT-01) : instantané docs/api/openapi/bff-employeur.json.</summary>
public sealed class ContratOpenApiTests
{
    [Fact]
    [Trait("Categorie", ContratOpenApi.Categorie)]
    public Task Le_document_OpenAPI_correspond_a_l_instantane_versionne() =>
        ContratOpenApi.VerifierAsync<Program>("bff-employeur", TestContext.Current.CancellationToken);
}
