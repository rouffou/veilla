using Sepp.BuildingBlocks.Domain;
using Sepp.Integrations.Domain.Bce;
using Sepp.Integrations.Domain.Correspondances;

using Shouldly;

namespace Sepp.Integrations.Domain.Tests;

public sealed class CorrespondanceIdentifiantTests
{
    [Fact]
    public void Un_numero_bce_correspond_a_un_affilie_sous_sa_forme_canonique()
    {
        var affilie = Guid.CreateVersion7();

        var correspondance = CorrespondanceIdentifiant.Creer(TypeIdentifiantExterne.NumeroBce, "BE 0202.239.951", TypeObjetInterne.Affilie, affilie);

        correspondance.ValeurExterne.ShouldBe("0202239951");
        correspondance.IdentifiantInterne.ShouldBe(affilie);
    }

    [Fact]
    public void Les_types_externes_et_internes_doivent_concorder()
    {
        Should.Throw<DomainException>(() =>
            CorrespondanceIdentifiant.Creer(TypeIdentifiantExterne.NumeroBce, "0202239951", TypeObjetInterne.Occupation, Guid.CreateVersion7()));
        Should.Throw<DomainException>(() =>
            CorrespondanceIdentifiant.Creer(TypeIdentifiantExterne.ReferenceDimona, "DIM-1", TypeObjetInterne.Affilie, Guid.CreateVersion7()));
        Should.Throw<DomainException>(() =>
            CorrespondanceIdentifiant.Creer(TypeIdentifiantExterne.ReferenceDimona, "DIM-1", TypeObjetInterne.Occupation, Guid.Empty));
    }

    [Fact]
    public void Rattacher_au_meme_objet_est_sans_effet()
    {
        var occupation = Guid.CreateVersion7();
        var correspondance = CorrespondanceIdentifiant.Creer(TypeIdentifiantExterne.ReferenceDimona, " dim-1 ", TypeObjetInterne.Occupation, occupation);

        correspondance.ValeurExterne.ShouldBe("DIM-1");
        correspondance.Rattacher(occupation).ShouldBeFalse();
        correspondance.Rattacher(Guid.CreateVersion7()).ShouldBeTrue();
    }
}

public sealed class EntrepriseBceTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
    private static readonly string Unite1 = NumerosBce.AvecControle("20223995");
    private static readonly string Unite2 = NumerosBce.AvecControle("30223995");

    private static DonneesEntreprise Donnees(string denomination = "ACME", string rue = "Rue de la Loi", params string[] unites) => new(
        "0202.239.951", denomination, "SRL", "62.010", new DateOnly(2026, 10, 5),
        (unites.Length == 0 ? [Unite1] : unites).Select(u => new DonneesUniteEtablissement(u, "Siège", new AdresseBce(rue, "16", null, "1000", "Bruxelles"), null)).ToList());

    [Fact]
    public void Une_entreprise_recue_est_creee_avec_ses_unites()
    {
        var entreprise = EntrepriseBce.Creer(Donnees(), Maintenant);

        entreprise.NumeroBce.ShouldBe("0202239951");
        entreprise.UnitesEtablissement.ShouldHaveSingleItem().Numero.ShouldBe(Unite1);
        entreprise.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<EntrepriseBceActualisee>();
    }

    [Fact]
    public void Des_donnees_identiques_ne_changent_rien()
    {
        var entreprise = EntrepriseBce.Creer(Donnees(), Maintenant);
        entreprise.ClearDomainEvents();

        entreprise.Actualiser(Donnees(), Maintenant.AddDays(1)).ShouldBeFalse();

        entreprise.DomainEvents.ShouldBeEmpty();
        entreprise.ActualiseeLe.ShouldBe(Maintenant);
    }

    [Fact]
    public void Une_adresse_modifiee_une_unite_ajoutee_ou_disparue_est_un_changement()
    {
        var entreprise = EntrepriseBce.Creer(Donnees(), Maintenant);

        entreprise.Actualiser(Donnees(rue: "Avenue Louise"), Maintenant).ShouldBeTrue();
        entreprise.UnitesEtablissement.Single().Adresse.Rue.ShouldBe("Avenue Louise");

        entreprise.Actualiser(Donnees("ACME", "Avenue Louise", Unite1, Unite2), Maintenant).ShouldBeTrue();
        entreprise.UnitesEtablissement.Count.ShouldBe(2);

        entreprise.Actualiser(Donnees("ACME", "Avenue Louise", Unite2), Maintenant).ShouldBeTrue();
        entreprise.UnitesEtablissement.ShouldHaveSingleItem().Numero.ShouldBe(Unite2);
    }

    [Fact]
    public void Des_donnees_invalides_sont_refusees_sans_modifier_l_entreprise()
    {
        var entreprise = EntrepriseBce.Creer(Donnees(), Maintenant);

        Should.Throw<DomainException>(() => entreprise.Actualiser(Donnees("ACME", "Rue de la Loi", Unite1, Unite1), Maintenant));
        Should.Throw<DomainException>(() => entreprise.Actualiser(Donnees("ACME", "Rue", "0202239951"), Maintenant));
        Should.Throw<DomainException>(() => entreprise.Actualiser(Donnees() with { NumeroBce = "0403.170.701" }, Maintenant));

        entreprise.UnitesEtablissement.ShouldHaveSingleItem().Numero.ShouldBe(Unite1);
    }

    [Fact]
    public void Une_adresse_exige_un_code_pays_iso() =>
        Should.Throw<DomainException>(() => new AdresseBce("Rue", "1", null, "1000", "Bruxelles", "BEL"));
}
