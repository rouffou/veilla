using Microsoft.Extensions.Configuration;

using Shouldly;

using DI = Sepp.Documents.Adapters.DependencyInjection;

namespace Sepp.Documents.Architecture.Tests;

/// <summary>ADR 0009 : <c>Documents:SourceLinguistique</c> vaut <c>Http</c> (défaut) ou <c>Projection</c> ; toute autre valeur empêche le démarrage.</summary>
public sealed class SourceLinguistiqueConfigurationTests
{
    private static IConfiguration Configuration(string? valeur) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Documents:SourceLinguistique"] = valeur }).Build();

    [Theory]
    [InlineData(null, DI.SourceLinguistiqueHttp)]
    [InlineData("", DI.SourceLinguistiqueHttp)]
    [InlineData("Http", DI.SourceLinguistiqueHttp)]
    [InlineData("projection", DI.SourceLinguistiqueProjection)]
    [InlineData("Projection", DI.SourceLinguistiqueProjection)]
    public void Les_valeurs_admises_sont_Http_par_defaut_et_Projection(string? valeur, string attendu) =>
        DI.ModeSourceLinguistique(Configuration(valeur)).ShouldBe(attendu);

    [Fact]
    public void Une_valeur_inconnue_empeche_le_demarrage() =>
        Should.Throw<InvalidOperationException>(() => DI.ModeSourceLinguistique(Configuration("Evenements")))
            .Message.ShouldContain("Documents:SourceLinguistique");
}
