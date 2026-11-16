using System.Net;

using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Adapters.Externe;
using Sepp.Documents.Domain.Langues;
using Sepp.Testing.Contrats;

using Shouldly;

namespace Sepp.Documents.Integration.Tests;

/// <summary>
/// Adaptateurs HTTP de service à service de Documents face à des doubles confrontés aux instantanés OpenAPI de leurs fournisseurs
/// (#476) : source linguistique (NF-41 : Affiliés, Personnes) et dossier de santé (SAN-43, SAN-50 : Surveillance médicale).
/// Les réponses programmées sont conformes au contrat (propriétés, énumérations à la casse exacte : c'est la classe du bug #436) et
/// les requêtes émises correspondent à des opérations existantes. Le jeton du compte technique n'est pas rejoué ici.
/// </summary>
public sealed class AdaptateursHttpTests : IDisposable
{
    private const string Affilies = "affilies.test";
    private const string Personnes = "personnes.test";
    private const string SurveillanceMedicale = "surveillance-medicale.test";

    private readonly FournisseurHttpSimule _aval = new();

    public void Dispose() => _aval.Dispose();

    [Fact]
    public async Task Le_regime_et_la_langue_de_l_affilie_et_la_langue_du_travailleur_sont_lus_selon_le_contrat()
    {
        var affilie = Guid.CreateVersion7();
        var personne = Guid.CreateVersion7();
        _aval.RepondreSelonContrat(Affilies, "affilies", $"/api/v1/affilies/{affilie}", new Dictionary<string, object?>
        {
            ["fiche.regimeLinguistique"] = "Neerlandais",
            ["fiche.langue"] = "Nl",
        });
        _aval.RepondreSelonContrat(Personnes, "personnes", $"/api/v1/personnes/{personne}", new Dictionary<string, object?> { ["langue"] = "De" });

        var infos = await Source().ObtenirAsync(affilie, personne, TestContext.Current.CancellationToken);

        infos.Regime.ShouldBe(RegimeLinguistique.Neerlandais);
        infos.LangueAffilie.ShouldBe(Language.Nl);
        infos.LangueTravailleur.ShouldBe(Language.De);
        _aval.ExigerRequetesConformes();
    }

    [Fact]
    public async Task Un_affilie_et_une_personne_introuvables_laissent_la_langue_indeterminee()
    {
        var affilie = Guid.CreateVersion7();
        var personne = Guid.CreateVersion7();
        _aval.Probleme(Affilies, $"/api/v1/affilies/{affilie}", HttpStatusCode.NotFound, "affilie.inconnu");
        _aval.Probleme(Personnes, $"/api/v1/personnes/{personne}", HttpStatusCode.NotFound, "personne.inconnue");

        var infos = await Source().ObtenirAsync(affilie, personne, TestContext.Current.CancellationToken);

        infos.Regime.ShouldBeNull();
        infos.LangueAffilie.ShouldBeNull();
        infos.LangueTravailleur.ShouldBeNull();
        _aval.ExigerRequetesConformes();
    }

    [Fact]
    public async Task La_copie_du_dossier_validee_est_lue_selon_le_contrat()
    {
        var demande = Guid.CreateVersion7();
        _aval.RepondreSelonContrat(SurveillanceMedicale, "surveillance-medicale", $"/api/v1/demandes-copie/{demande}/contenu");

        var copie = await DossierSante().ObtenirCopieAsync(demande, TestContext.Current.CancellationToken);

        copie.DateOuverture.ShouldBe(new DateOnly(2026, 1, 15));
        copie.Expositions.ShouldHaveSingleItem();
        copie.Vaccinations.ShouldHaveSingleItem();
        _aval.ExigerRequetesConformes();
    }

    [Fact]
    public async Task Une_copie_non_transmise_fait_echouer_la_lecture_sans_reprendre_le_detail()
    {
        var demande = Guid.CreateVersion7();
        _aval.Probleme(SurveillanceMedicale, $"/api/v1/demandes-copie/{demande}/contenu", HttpStatusCode.Conflict, "demande-copie.non-validee");

        var echec = await Should.ThrowAsync<InvalidOperationException>(() => DossierSante().ObtenirCopieAsync(demande, TestContext.Current.CancellationToken));

        echec.Message.ShouldContain("409");
        echec.Message.ShouldNotContain("non-validee");
        _aval.ExigerRequetesConformes();
    }

    [Fact]
    public async Task Le_carnet_de_vaccination_est_lu_selon_le_contrat_et_une_vaccination_inconnue_donne_un_resultat_absent()
    {
        var vaccination = Guid.CreateVersion7();
        var inconnue = Guid.CreateVersion7();
        _aval.RepondreSelonContrat(SurveillanceMedicale, "surveillance-medicale", $"/api/v1/vaccinations/{vaccination}/carnet");
        _aval.Probleme(SurveillanceMedicale, $"/api/v1/vaccinations/{inconnue}/carnet", HttpStatusCode.NotFound, "vaccination.inconnue");

        var carnet = await DossierSante().ObtenirCarnetAsync(vaccination, TestContext.Current.CancellationToken);
        var absent = await DossierSante().ObtenirCarnetAsync(inconnue, TestContext.Current.CancellationToken);

        carnet.ShouldNotBeNull().Doses.ShouldHaveSingleItem();
        absent.ShouldBeNull();
        _aval.ExigerRequetesConformes();
    }

    private SourceLinguistiqueHttp Source()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(ClientsHttp.Affilies, c => c.BaseAddress = new Uri($"https://{Affilies}/")).ConfigurePrimaryHttpMessageHandler(() => _aval);
        services.AddHttpClient(ClientsHttp.Personnes, c => c.BaseAddress = new Uri($"https://{Personnes}/")).ConfigurePrimaryHttpMessageHandler(() => _aval);
        return new SourceLinguistiqueHttp(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>());
    }

    private SourceDossierSanteHttp DossierSante()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(ClientsHttp.SurveillanceMedicale, c => c.BaseAddress = new Uri($"https://{SurveillanceMedicale}/")).ConfigurePrimaryHttpMessageHandler(() => _aval);
        return new SourceDossierSanteHttp(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>());
    }
}
