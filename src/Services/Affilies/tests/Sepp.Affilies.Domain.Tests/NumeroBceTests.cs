using Sepp.Affilies.Domain.Affilies;
using Sepp.BuildingBlocks.Domain;

using Shouldly;

namespace Sepp.Affilies.Domain.Tests;

public class NumeroBceTests
{
    [Theory]
    [InlineData("0202.239.951")]
    [InlineData("0202239951")]
    [InlineData("BE0202239951")]
    [InlineData("BE 0202.239.951")]
    [InlineData("202.239.951")]
    [InlineData(" 0202 239 951 ")]
    public void Les_formats_usuels_sont_acceptes_et_normalises(string saisie)
    {
        var numero = new NumeroBce(saisie);

        numero.Value.ShouldBe("0202239951");
        numero.Formate.ShouldBe("0202.239.951");
    }

    [Theory]
    [InlineData("0202.239.952")]
    [InlineData("0403.170.702")]
    public void Un_chiffre_de_controle_errone_est_refuse(string saisie) =>
        Should.Throw<DomainException>(() => new NumeroBce(saisie)).Message.ShouldContain("modulo 97");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("0202.239.95A")]
    [InlineData("02022399510")]
    public void Un_numero_mal_forme_est_refuse(string saisie) =>
        Should.Throw<DomainException>(() => new NumeroBce(saisie));

    [Fact]
    public void Un_numero_d_entreprise_commence_par_0_ou_1() =>
        Should.Throw<DomainException>(() => new NumeroBce("2123.456.791")).Message.ShouldContain("0 ou 1");

    [Fact]
    public void Un_numero_d_entreprise_commencant_par_1_est_valide() =>
        new NumeroBce("1234.567.894").Formate.ShouldBe("1234.567.894");

    [Fact]
    public void Une_unite_d_etablissement_commence_par_2_a_8()
    {
        new NumeroUniteEtablissement("2.123.456.791").Value.ShouldBe("2123456791");
        Should.Throw<DomainException>(() => new NumeroUniteEtablissement("0202.239.951"));
        Should.Throw<DomainException>(() => new NumeroUniteEtablissement("2123.456.792"));
    }

    [Fact]
    public void TryParse_ne_leve_pas_d_exception()
    {
        NumeroBce.TryParse("0403.170.701", out var valide).ShouldBeTrue();
        valide!.Value.ShouldBe("0403170701");
        NumeroBce.TryParse("n'importe quoi", out var invalide).ShouldBeFalse();
        invalide.ShouldBeNull();
    }

    [Fact]
    public void Une_adresse_belge_exige_un_code_postal_a_quatre_chiffres()
    {
        new Adresse("Rue de la Loi", "16", null, "1000", "Bruxelles").CodePays.ShouldBe("BE");
        Should.Throw<DomainException>(() => new Adresse("Rue de la Loi", "16", null, "10000", "Bruxelles"));
        new Adresse("Rue de Rivoli", "1", null, "75001", "Paris", "fr").CodePays.ShouldBe("FR");
    }
}
