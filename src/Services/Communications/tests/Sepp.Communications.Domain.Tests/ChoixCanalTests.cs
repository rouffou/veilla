using Sepp.Communications.Domain.Messages;

using Shouldly;

namespace Sepp.Communications.Domain.Tests;

/// <summary>DOC-03 : choix du canal selon le souhait de l'émetteur, la préférence et la joignabilité du destinataire.</summary>
public class ChoixCanalTests
{
    private static readonly Joignabilite Tout = new(Portail: true, Email: true, Sms: true, Courrier: true, EBoxEntreprise: true, EBoxCitoyen: true);

    [Fact]
    public void Le_canal_souhaite_est_retenu_s_il_est_joignable()
    {
        var choix = ChoixCanal.Determiner(TypeDestinataire.Personne, Tout, Canal.Sms, Canal.Email, recommandeRequis: false);

        choix.ShouldHaveSingleItem().ShouldBe(new CanalChoisi(Canal.Sms, false));
    }

    [Fact]
    public void A_defaut_la_preference_du_destinataire_est_retenue()
    {
        var sansSms = Tout with { Sms = false };

        var choix = ChoixCanal.Determiner(TypeDestinataire.Personne, sansSms, Canal.Sms, Canal.Courrier, recommandeRequis: false);

        choix.ShouldHaveSingleItem().Canal.ShouldBe(Canal.Courrier);
    }

    [Fact]
    public void Sans_souhait_ni_preference_la_cascade_du_travailleur_commence_par_l_email()
    {
        ChoixCanal.Determiner(TypeDestinataire.Personne, Tout, null, null, false).Single().Canal.ShouldBe(Canal.Email);
        ChoixCanal.Determiner(TypeDestinataire.Personne, Tout with { Email = false }, null, null, false).Single().Canal.ShouldBe(Canal.Sms);
        ChoixCanal.Determiner(TypeDestinataire.Personne, Tout with { Email = false, Sms = false }, null, null, false).Single().Canal.ShouldBe(Canal.Courrier);
        ChoixCanal.Determiner(TypeDestinataire.Personne, new Joignabilite(true, false, false, false, false, false), null, null, false).Single().Canal.ShouldBe(Canal.Portail);
    }

    [Fact]
    public void L_affilie_est_joint_par_eBox_entreprise_en_priorite()
    {
        ChoixCanal.Determiner(TypeDestinataire.Affilie, Tout, null, null, false).Single().Canal.ShouldBe(Canal.EBoxEntreprise);
        ChoixCanal.Determiner(TypeDestinataire.Affilie, Tout with { EBoxEntreprise = false }, null, null, false).Single().Canal.ShouldBe(Canal.Email);
    }

    [Fact]
    public void Un_destinataire_interne_est_alerte_par_email()
    {
        ChoixCanal.Determiner(TypeDestinataire.Interne, Tout, null, null, false).Single().Canal.ShouldBe(Canal.Email);
    }

    [Fact]
    public void Un_recommande_requis_s_ajoute_a_l_envoi_simple_par_recommande_electronique()
    {
        var choix = ChoixCanal.Determiner(TypeDestinataire.Personne, Tout, Canal.Sms, null, recommandeRequis: true);

        choix.ShouldBe([new CanalChoisi(Canal.Sms, false), new CanalChoisi(Canal.RecommandeElectronique, true)]);
        ChoixCanal.RecommandeAssure(choix).ShouldBeTrue();
    }

    [Fact]
    public void Sans_recommande_electronique_possible_le_courrier_recommande_prend_le_relais()
    {
        var sansCourrielNiEBox = new Joignabilite(true, false, true, true, false, false);

        var choix = ChoixCanal.Determiner(TypeDestinataire.Personne, sansCourrielNiEBox, null, null, recommandeRequis: true);

        choix.ShouldBe([new CanalChoisi(Canal.Sms, false), new CanalChoisi(Canal.Courrier, true)]);
    }

    [Fact]
    public void Un_recommande_impossible_est_signale()
    {
        var injoignable = new Joignabilite(true, false, true, false, false, false);

        var choix = ChoixCanal.Determiner(TypeDestinataire.Personne, injoignable, null, null, recommandeRequis: true);

        ChoixCanal.RecommandeAssure(choix).ShouldBeFalse();
    }

    [Fact]
    public void Un_destinataire_injoignable_ne_donne_aucun_canal() =>
        ChoixCanal.Determiner(TypeDestinataire.Personne, new Joignabilite(false, false, false, false, false, false), Canal.Email, null, false).ShouldBeEmpty();

    [Theory]
    [InlineData("Courrier", Canal.Courrier)]
    [InlineData("email", Canal.Email)]
    [InlineData("Sms", Canal.Sms)]
    [InlineData("Portail", Canal.Portail)]
    [InlineData("eBox-Citoyen", Canal.EBoxCitoyen)]
    public void Les_canaux_des_evenements_sont_interpretes(string valeur, Canal attendu) =>
        Canaux.Depuis(valeur).ShouldBe(attendu);

    [Fact]
    public void Un_canal_inconnu_n_est_pas_interprete() => Canaux.Depuis("pigeon").ShouldBeNull();
}
