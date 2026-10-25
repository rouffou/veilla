using System.Net;
using System.Text.Json;

using Sepp.Bff.Interne.Aval;
using Sepp.Bff.Interne.Ecrans;

using Shouldly;

namespace Sepp.Bff.Interne.Tests;

public sealed class SectionToleranteTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Une_section_chargee_est_disponible_avec_sa_valeur()
    {
        var section = await SectionTolerante.ChargerAsync(() => Task.FromResult(new[] { "Examen périodique" }));

        section.Disponible.ShouldBeTrue();
        section.Raison.ShouldBeNull();
        section.Valeur.ShouldBe(["Examen périodique"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "acces-refuse")]
    [InlineData(HttpStatusCode.Unauthorized, "acces-refuse")]
    [InlineData(HttpStatusCode.InternalServerError, "service-indisponible")]
    [InlineData(HttpStatusCode.BadGateway, "service-indisponible")]
    public async Task Un_refus_ou_une_panne_du_service_rend_la_section_absente_avec_sa_raison(HttpStatusCode status, string raison)
    {
        var section = await SectionTolerante.ChargerAsync<string>(() => throw new ErreurAvalException("surveillance-medicale", status, "x", "détail sensible"));

        section.Disponible.ShouldBeFalse();
        section.Raison.ShouldBe(raison);
        section.Valeur.ShouldBeNull();
    }

    [Fact]
    public async Task Un_service_injoignable_rend_la_section_indisponible()
    {
        var section = await SectionTolerante.ChargerAsync<string>(() => throw new ServiceIndisponibleException("planification"));

        section.Raison.ShouldBe(SectionTolerante<string>.ServiceIndisponible);
    }

    [Fact]
    public async Task Une_erreur_fonctionnelle_de_la_demande_remonte()
    {
        await Should.ThrowAsync<ErreurAvalException>(() =>
            SectionTolerante.ChargerAsync<string>(() => throw new ErreurAvalException("personnes", HttpStatusCode.NotFound, "introuvable", null)));
    }

    [Fact]
    public async Task La_forme_json_est_disponible_raison_valeur_sans_detail_de_l_erreur()
    {
        var section = await SectionTolerante.ChargerAsync<string>(() => throw new ErreurAvalException("audit", HttpStatusCode.Forbidden, "x", "détail sensible"));

        var json = JsonSerializer.Serialize(section, Web);

        json.ShouldBe("""{"disponible":false,"raison":"acces-refuse","valeur":null}""");
    }
}
