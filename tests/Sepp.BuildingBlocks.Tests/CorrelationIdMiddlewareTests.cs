using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using Sepp.BuildingBlocks.Web;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

// ARC-47 : propagation de l'identifiant de corrélation ; CodeQL cs/log-forging : seul un GUID reformaté par la
// plateforme est écrit dans les journaux, jamais la chaîne reçue.
public class CorrelationIdMiddlewareTests
{
    [Theory]
    [InlineData("0192a5c8-0000-7000-8000-000000000001")]
    [InlineData("4bf92f3577b34da6a3ce929d0e0e4736")]
    public async Task Un_identifiant_valide_est_repris_tel_quel(string valeur)
    {
        var resultat = await ExecuterAsync(valeur);

        resultat.ShouldBe(valeur);
    }

    [Theory]
    [InlineData("abc\r\nINFO faux message")]
    [InlineData("abc\nxyz")]
    [InlineData("<script>")]
    [InlineData("a b")]
    [InlineData("é")]
    [InlineData("portail.employeur:42_a")]
    [InlineData("{0192a5c8-0000-7000-8000-000000000001}")]
    public async Task Un_identifiant_avec_des_caracteres_interdits_est_remplace(string valeur)
    {
        var resultat = await ExecuterAsync(valeur);

        resultat.ShouldNotBe(valeur);
        resultat.ShouldNotBeNullOrWhiteSpace();
        resultat.ShouldNotContain("\n");
    }

    [Fact]
    public async Task Un_guid_en_majuscules_est_repris_sous_sa_forme_normalisee()
    {
        var resultat = await ExecuterAsync("0192A5C8-0000-7000-8000-00000000000A");

        resultat.ShouldBe("0192a5c8-0000-7000-8000-00000000000a");
    }

    [Fact]
    public async Task Un_identifiant_trop_long_est_remplace()
    {
        var valeur = new string('a', 101);

        var resultat = await ExecuterAsync(valeur);

        resultat.ShouldNotBe(valeur);
    }

    private static async Task<string> ExecuterAsync(string valeur)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.Header] = valeur;
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Headers[CorrelationIdMiddleware.Header].ToString().ShouldBe(context.TraceIdentifier);
        return context.TraceIdentifier;
    }
}
