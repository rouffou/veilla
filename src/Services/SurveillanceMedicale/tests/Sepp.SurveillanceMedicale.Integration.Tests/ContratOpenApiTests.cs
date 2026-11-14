using Sepp.Testing.Contrats;

namespace Sepp.SurveillanceMedicale.Integration.Tests;

/// <summary>Contrat OpenAPI de SurveillanceMedicale (INT-01) : instantané docs/api/openapi/surveillance-medicale.json.</summary>
public sealed class ContratOpenApiTests
{
    [Fact]
    [Trait("Categorie", ContratOpenApi.Categorie)]
    public Task Le_document_OpenAPI_correspond_a_l_instantane_versionne() =>
        ContratOpenApi.VerifierAsync<Program>("surveillance-medicale", TestContext.Current.CancellationToken, "SurveillanceMedicale");
}
