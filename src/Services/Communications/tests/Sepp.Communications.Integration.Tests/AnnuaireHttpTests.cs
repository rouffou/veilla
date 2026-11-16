using System.Net;

using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Domain;
using Sepp.Communications.Adapters.Annuaire;
using Sepp.Communications.Domain.Messages;
using Sepp.Testing.Contrats;

using Shouldly;

namespace Sepp.Communications.Integration.Tests;

/// <summary>
/// Annuaire réel (<c>AnnuaireHttp</c>) face à des doubles HTTP de Personnes et d'Affiliés confrontés à leurs instantanés OpenAPI
/// (#476) : les réponses programmées sont conformes au contrat (propriétés, énumérations à la casse exacte) et les requêtes
/// émises par l'adaptateur correspondent à des opérations existantes. Le jeton du compte technique n'est pas rejoué ici.
/// </summary>
public sealed class AnnuaireHttpTests : IDisposable
{
    private const string Personnes = "personnes.test";
    private const string Affilies = "affilies.test";

    private readonly FournisseurHttpSimule _aval = new();

    public void Dispose() => _aval.Dispose();

    private AnnuaireHttp Annuaire()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(ClientsHttp.Personnes, c => c.BaseAddress = new Uri($"https://{Personnes}/")).ConfigurePrimaryHttpMessageHandler(() => _aval);
        services.AddHttpClient(ClientsHttp.Affilies, c => c.BaseAddress = new Uri($"https://{Affilies}/")).ConfigurePrimaryHttpMessageHandler(() => _aval);
        return new AnnuaireHttp(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(), new OptionsAnnuaire());
    }

    [Fact]
    public async Task Une_personne_est_lue_avec_sa_langue_ses_coordonnees_et_son_canal_prefere()
    {
        var id = Guid.CreateVersion7();
        _aval.RepondreSelonContrat(Personnes, "personnes", $"/api/v1/personnes/{id}", new Dictionary<string, object?>
        {
            ["prenom"] = "Alix",
            ["nom"] = "Exemple",
            ["email"] = "alix@exemple.test",
            ["telephone"] = "+3225550100",
            ["langue"] = "Nl",
            ["canalPrefere"] = "Sms",
            ["adresse"] = new { rue = "Rue de l'Exemple", numero = "1", boite = (string?)null, codePostal = "1000", localite = "Bruxelles", pays = "BE" },
        });

        var destinataire = await Annuaire().ObtenirAsync(TypeDestinataire.Personne, id, TestContext.Current.CancellationToken);

        destinataire.ShouldNotBeNull();
        destinataire.Nom.ShouldBe("Alix Exemple");
        destinataire.Email.ShouldBe("alix@exemple.test");
        destinataire.Telephone.ShouldBe("+3225550100");
        destinataire.Langue.ShouldBe(Language.Nl);
        destinataire.CanalPrefere.ShouldBe(Canal.Sms);
        destinataire.Adresse.ShouldNotBeNull().Localite.ShouldBe("Bruxelles");
        _aval.ExigerRequetesConformes();
    }

    [Fact]
    public async Task Une_personne_inconnue_donne_un_destinataire_absent()
    {
        var id = Guid.CreateVersion7();
        _aval.Probleme(Personnes, $"/api/v1/personnes/{id}", HttpStatusCode.NotFound, "personne.inconnue");

        (await Annuaire().ObtenirAsync(TypeDestinataire.Personne, id, TestContext.Current.CancellationToken)).ShouldBeNull();
        _aval.ExigerRequetesConformes();
    }

    [Fact]
    public async Task Un_affilie_est_lu_avec_sa_fiche_son_contact_et_l_adresse_de_son_unite()
    {
        var id = Guid.CreateVersion7();
        _aval.RepondreSelonContrat(Affilies, "affilies", $"/api/v1/affilies/{id}", new Dictionary<string, object?>
        {
            ["fiche.denomination"] = "Boulangerie Dupont",
            ["fiche.numeroBce"] = "0123.456.749",
            ["fiche.langue"] = "De",
            ["contacts"] = new[] { new { id = Guid.CreateVersion7(), nom = "Contact", role = "PersonneDeContact", fonction = (string?)null, email = "contact@exemple.test", telephone = "+3225550101", valideDu = "2026-01-01", valideJusquAu = (string?)null } },
            ["unitesEtablissement"] = new[]
            {
                new
                {
                    id = Guid.CreateVersion7(), numero = "2.000.000.001", nom = "Siège", langue = "Fr", valideDu = "2026-01-01", valideJusquAu = (string?)null, sites = Array.Empty<object>(),
                    adresse = new { rue = "Rue de l'Exemple", numero = "1", boite = (string?)null, codePostal = "1000", localite = "Bruxelles", codePays = "BE" },
                },
            },
        });

        var destinataire = await Annuaire().ObtenirAsync(TypeDestinataire.Affilie, id, TestContext.Current.CancellationToken);

        destinataire.ShouldNotBeNull();
        destinataire.Nom.ShouldBe("Boulangerie Dupont");
        destinataire.Langue.ShouldBe(Language.De);
        destinataire.EBoxEntreprise.ShouldBe("0123456749");
        destinataire.Email.ShouldBe("contact@exemple.test");
        destinataire.Adresse.ShouldNotBeNull().Pays.ShouldBe("BE");
        _aval.ExigerRequetesConformes();
    }
}
