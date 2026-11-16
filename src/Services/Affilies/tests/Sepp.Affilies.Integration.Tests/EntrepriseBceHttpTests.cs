using System.Net;

using Microsoft.Extensions.DependencyInjection;

using Sepp.Affilies.Adapters.Externe;
using Sepp.Testing.Contrats;

using Shouldly;

namespace Sepp.Affilies.Integration.Tests;

/// <summary>
/// Lecture des données BCE chez Intégrations (<c>EntrepriseBceHttp</c>) face à un double HTTP confronté à l'instantané OpenAPI
/// d'Intégrations (#476) : la réponse programmée est conforme au contrat (propriétés, types), la requête émise correspond à
/// l'opération <c>GET /api/v1/bce/entreprises/{numeroBce}</c>. Le jeton du compte technique n'est pas rejoué ici.
/// </summary>
public sealed class EntrepriseBceHttpTests : IDisposable
{
    private const string Integrations = "integrations.test";

    private readonly FournisseurHttpSimule _aval = new();

    public void Dispose() => _aval.Dispose();

    [Fact]
    public async Task Les_donnees_bce_et_les_unites_d_etablissement_sont_lues_selon_le_contrat()
    {
        _aval.RepondreSelonContrat(Integrations, "integrations", "/api/v1/bce/entreprises/0123456749", new Dictionary<string, object?>
        {
            ["numeroBce"] = "0123456749",
            ["denomination"] = "Boulangerie Dupont",
            ["formeJuridique"] = "SRL",
            ["codeNace"] = "62010",
            ["dateExtraction"] = "2026-10-01",
            ["unitesEtablissement"] = new[]
            {
                new
                {
                    numero = "2.000.000.001", denomination = "Siège", dateDebut = "2020-01-01",
                    adresse = new { rue = "Rue de l'Exemple", numero = "1", boite = "B2", codePostal = "1000", localite = "Bruxelles", codePays = "BE" },
                },
            },
        });

        var entreprise = await Client().LireAsync("0123456749", TestContext.Current.CancellationToken);

        entreprise.ShouldNotBeNull();
        entreprise.Denomination.ShouldBe("Boulangerie Dupont");
        entreprise.CodeNace.ShouldBe("62010");
        entreprise.DateExtraction.ShouldBe(new DateOnly(2026, 10, 1));
        var unite = entreprise.Unites.ShouldHaveSingleItem();
        unite.Numero.ShouldBe("2.000.000.001");
        unite.Localite.ShouldBe("Bruxelles");
        unite.CodePays.ShouldBe("BE");
        unite.DateDebut.ShouldBe(new DateOnly(2020, 1, 1));
        _aval.ExigerRequetesConformes();
    }

    [Fact]
    public async Task Une_entreprise_sans_donnees_bce_donne_un_resultat_absent()
    {
        _aval.Probleme(Integrations, "/api/v1/bce/entreprises/0123456749", HttpStatusCode.NotFound, "bce.inconnue");

        (await Client().LireAsync("0123456749", TestContext.Current.CancellationToken)).ShouldBeNull();
        _aval.ExigerRequetesConformes();
    }

    private EntrepriseBceHttp Client()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(ClientsHttp.Integrations, c => c.BaseAddress = new Uri($"https://{Integrations}/")).ConfigurePrimaryHttpMessageHandler(() => _aval);
        return new EntrepriseBceHttp(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>());
    }
}
