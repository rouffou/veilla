using System.Net;
using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using Sepp.Bff.Travailleur.Aval;
using Sepp.Bff.Travailleur.Securite;
using Sepp.BuildingBlocks.Web;

using Shouldly;

namespace Sepp.Bff.Travailleur.Tests;

public sealed class PerimetreTravailleurTests
{
    private static readonly Guid A = Guid.Parse("0192a5c8-0000-7000-8000-0000000000a1");
    private static readonly Guid B = Guid.Parse("0192a5c8-0000-7000-8000-0000000000a2");

    private static ClaimsPrincipal Utilisateur(params string[] valeurs) =>
        new(new ClaimsIdentity(valeurs.Select(v => new Claim(PerimetreTravailleur.ClaimPersonne, v)), "test"));

    [Fact]
    public void Le_claim_personne_id_donne_la_personne() =>
        PerimetreTravailleur.Personne(Utilisateur(A.ToString())).ShouldBe(A);

    [Fact]
    public void Le_meme_claim_repete_donne_la_meme_personne() =>
        PerimetreTravailleur.Personne(Utilisateur(A.ToString(), A.ToString())).ShouldBe(A);

    [Fact]
    public void Deux_personnes_differentes_dans_le_jeton_ne_donnent_aucun_perimetre() =>
        PerimetreTravailleur.Personne(Utilisateur(A.ToString(), B.ToString())).ShouldBeNull();

    [Theory]
    [InlineData("")]
    [InlineData("0123456749")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("[\"0192a5c8-0000-7000-8000-0000000000a1\"]")]
    public void Une_valeur_qui_n_est_pas_un_UUID_utilisable_ne_donne_aucun_perimetre(string valeur) =>
        PerimetreTravailleur.Personne(Utilisateur(valeur)).ShouldBeNull();

    [Fact]
    public void Sans_claim_aucune_personne_n_est_accessible() =>
        PerimetreTravailleur.Personne(Utilisateur()).ShouldBeNull();
}

public sealed class PropagationJetonHandlerTests
{
    [Fact]
    public async Task Le_jeton_la_correlation_et_la_langue_de_l_utilisateur_sont_recopies_sur_l_appel_aval()
    {
        var contexte = new DefaultHttpContext();
        contexte.Request.Headers.Authorization = "Bearer jeton-utilisateur";
        contexte.Request.Headers.AcceptLanguage = "nl-BE";
        contexte.Response.Headers[CorrelationIdMiddleware.Header] = "corr-123";
        var capture = new Capture();
        using var client = new HttpClient(new PropagationJetonHandler(new HttpContextAccessor { HttpContext = contexte }) { InnerHandler = capture });

        await client.GetAsync("http://planification.test/api/v1/reservations/rendez-vous", TestContext.Current.CancellationToken);

        capture.Requete!.Headers.Authorization!.ToString().ShouldBe("Bearer jeton-utilisateur");
        capture.Requete.Headers.GetValues(CorrelationIdMiddleware.Header).ShouldBe(["corr-123"]);
        capture.Requete.Headers.AcceptLanguage.ToString().ShouldBe("nl-BE");
    }

    [Fact]
    public async Task Hors_requete_HTTP_aucun_jeton_n_est_invente()
    {
        var capture = new Capture();
        using var client = new HttpClient(new PropagationJetonHandler(new HttpContextAccessor()) { InnerHandler = capture });

        await client.GetAsync("http://planification.test/", TestContext.Current.CancellationToken);

        capture.Requete!.Headers.Authorization.ShouldBeNull();
    }

    private sealed class Capture : HttpMessageHandler
    {
        public HttpRequestMessage? Requete { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requete = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
