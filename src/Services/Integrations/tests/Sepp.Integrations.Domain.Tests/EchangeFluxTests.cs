using Sepp.BuildingBlocks.Domain;
using Sepp.Integrations.Domain;
using Sepp.Integrations.Domain.Flux;

using Shouldly;

namespace Sepp.Integrations.Domain.Tests;

public sealed class EchangeFluxTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    private static EchangeFlux Recu() =>
        EchangeFlux.Recevoir(TypeFlux.Dimona, SensFlux.Entrant, "dimona.entree", "entree:DIM-1", "DIM-1", 1, "{\"niss\":\"85073000123\"}", Maintenant);

    [Fact]
    public void Un_echange_recu_est_a_traiter_avec_sa_charge_utile()
    {
        var echange = Recu();

        echange.Statut.ShouldBe(StatutEchange.Recu);
        echange.EstATraiter.ShouldBeTrue();
        echange.Tentatives.ShouldBe(0);
        echange.NombreEnregistrements.ShouldBe(1);
    }

    [Fact]
    public void Une_tentative_reussie_marque_l_echange_traite_et_efface_l_erreur_precedente()
    {
        var echange = Recu();
        echange.DebuterTentative(Maintenant);
        echange.MarquerEnErreur("personnes.indisponible", "Service indisponible.", Maintenant);
        echange.EstATraiter.ShouldBeTrue();

        echange.DebuterTentative(Maintenant.AddMinutes(5));
        echange.MarquerTraite(1, Maintenant.AddMinutes(5));

        echange.Statut.ShouldBe(StatutEchange.Traite);
        echange.Tentatives.ShouldBe(2);
        echange.CodeErreur.ShouldBeNull();
        echange.MessageErreur.ShouldBeNull();
        echange.EstATraiter.ShouldBeFalse();
        Should.Throw<DomainException>(() => echange.DebuterTentative(Maintenant));
    }

    [Theory]
    [InlineData("Refus pour 85073000123.", "Refus pour ***********.")]
    [InlineData("Refus pour 85.07.30-001.23 !", "Refus pour *********** !")]
    [InlineData("Employeur BCE 0202.239.951 inconnu", "Employeur BCE 0202.239.951 inconnu")]
    public void Le_message_d_erreur_ne_conserve_aucun_niss(string message, string attendu)
    {
        var echange = Recu();
        echange.DebuterTentative(Maintenant);

        echange.Rejeter("personnes.rejet", message, Maintenant);

        echange.Statut.ShouldBe(StatutEchange.Rejete);
        echange.MessageErreur.ShouldBe(attendu);
    }

    [Fact]
    public void Un_message_d_erreur_trop_long_est_tronque()
    {
        var echange = Recu();
        echange.DebuterTentative(Maintenant);

        echange.MarquerEnErreur("x", new string('a', 2000), Maintenant);

        echange.MessageErreur!.Length.ShouldBe(EchangeFlux.LongueurMaximaleMessage);
    }

    [Fact]
    public void Une_charge_utile_purgee_ne_peut_plus_etre_rejouee()
    {
        var echange = Recu();
        echange.DebuterTentative(Maintenant);
        echange.Rejeter("integrations.employeur-inconnu", "Employeur inconnu.", Maintenant);

        echange.PurgerChargeUtile(Maintenant.AddDays(31)).ShouldBeTrue();
        echange.PurgerChargeUtile(Maintenant.AddDays(32)).ShouldBeFalse();

        echange.ChargeUtile.ShouldBeNull();
        echange.ChargeUtilePurgeeLe.ShouldBe(Maintenant.AddDays(31));
        echange.EstATraiter.ShouldBeFalse();
        Should.Throw<DomainException>(() => echange.DebuterTentative(Maintenant));
    }

    [Fact]
    public void Un_echange_exige_une_cle_et_une_charge_utile()
    {
        Should.Throw<DomainException>(() => EchangeFlux.Recevoir(TypeFlux.Bce, SensFlux.Entrant, "bce.entreprise", " ", null, 1, "{}", Maintenant));
        Should.Throw<DomainException>(() => EchangeFlux.Recevoir(TypeFlux.Bce, SensFlux.Entrant, "bce.entreprise", "cle", null, 1, "", Maintenant));
        Should.Throw<DomainException>(() => EchangeFlux.Recevoir(TypeFlux.Bce, SensFlux.Entrant, "bce.entreprise", "cle", null, -1, "{}", Maintenant));
    }

    [Fact]
    public void La_position_d_un_flux_n_avance_que_si_elle_change()
    {
        var position = PositionFlux.Initialiser(TypeFlux.Dimona);

        position.Avancer("lot-1", Maintenant).ShouldBeTrue();
        position.Avancer("lot-1", Maintenant.AddHours(1)).ShouldBeFalse();

        position.Position.ShouldBe("lot-1");
        position.MiseAJourLe.ShouldBe(Maintenant);
    }
}

public sealed class NumerosBceTests
{
    [Theory]
    [InlineData("0202.239.951", "0202239951")]
    [InlineData("BE 0202 239 951", "0202239951")]
    [InlineData("202239951", "0202239951")]
    public void Un_numero_d_entreprise_est_normalise(string saisie, string attendu) =>
        NumerosBce.Entreprise(saisie).ShouldBe(attendu);

    [Theory]
    [InlineData("0202.239.952")]
    [InlineData("2202.239.951")]
    [InlineData("12345")]
    [InlineData("")]
    public void Un_numero_d_entreprise_invalide_est_refuse(string saisie) =>
        Should.Throw<DomainException>(() => NumerosBce.Entreprise(saisie));

    [Fact]
    public void Le_controle_modulo_97_est_calcule()
    {
        var numero = NumerosBce.AvecControle("20223995");

        NumerosBce.UniteEtablissement(numero).ShouldBe(numero);
        Should.Throw<DomainException>(() => NumerosBce.Entreprise(numero));
    }

    [Fact]
    public void Le_masquage_ne_touche_que_les_suites_au_format_niss()
    {
        DonneesPersonnelles.Masquer("NISS 85073000123 et BCE 0202239951").ShouldBe("NISS *********** et BCE 0202239951");
        DonneesPersonnelles.Masquer("référence 123456789012").ShouldBe("référence 123456789012");
    }
}
