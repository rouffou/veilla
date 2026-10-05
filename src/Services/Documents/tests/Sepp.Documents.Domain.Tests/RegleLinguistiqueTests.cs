using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Domain.Documents;
using Sepp.Documents.Domain.Langues;

using Shouldly;

namespace Sepp.Documents.Domain.Tests;

/// <summary>NF-41 : langue des documents selon le régime linguistique de l'affilié ou le choix du travailleur.</summary>
public class RegleLinguistiqueTests
{
    [Fact]
    public void Une_langue_demandee_explicitement_prevaut()
    {
        var choix = RegleLinguistique.Determiner(new ContexteLinguistique(Language.En, RegimeLinguistique.Francais, Language.Fr, Language.Nl), TypeDestinataire.Personne);

        choix.Langue.ShouldBe(Language.En);
    }

    [Theory]
    [InlineData(RegimeLinguistique.Francais, Language.Nl, Language.Fr)]
    [InlineData(RegimeLinguistique.Neerlandais, Language.Fr, Language.Nl)]
    [InlineData(RegimeLinguistique.Allemand, Language.Fr, Language.De)]
    public void En_region_unilingue_la_langue_de_la_region_s_impose_meme_au_travailleur(RegimeLinguistique regime, Language choixTravailleur, Language attendue)
    {
        var choix = RegleLinguistique.Determiner(new ContexteLinguistique(null, regime, null, choixTravailleur), TypeDestinataire.Personne);

        choix.Langue.ShouldBe(attendue);
        choix.Motif.ShouldContain("régime linguistique");
    }

    [Fact]
    public void A_Bruxelles_le_travailleur_choisit_entre_francais_et_neerlandais()
    {
        var pourTravailleur = RegleLinguistique.Determiner(
            new ContexteLinguistique(null, RegimeLinguistique.BruxellesCapitale, Language.Fr, Language.Nl), TypeDestinataire.Personne);
        var pourEmployeur = RegleLinguistique.Determiner(
            new ContexteLinguistique(null, RegimeLinguistique.BruxellesCapitale, Language.Fr, Language.Nl), TypeDestinataire.Affilie);

        pourTravailleur.Langue.ShouldBe(Language.Nl);
        pourEmployeur.Langue.ShouldBe(Language.Fr);
    }

    [Fact]
    public void A_Bruxelles_l_allemand_n_est_pas_retenu_et_le_francais_est_le_defaut()
    {
        var choix = RegleLinguistique.Determiner(
            new ContexteLinguistique(null, RegimeLinguistique.BruxellesCapitale, null, Language.De), TypeDestinataire.Personne);

        choix.Langue.ShouldBe(Language.Fr);
    }

    [Fact]
    public void Sans_regime_connu_le_choix_du_travailleur_puis_la_langue_de_l_affilie_puis_le_francais()
    {
        RegleLinguistique.Determiner(new ContexteLinguistique(null, null, Language.Nl, Language.De), TypeDestinataire.Dossier).Langue.ShouldBe(Language.De);
        RegleLinguistique.Determiner(new ContexteLinguistique(null, null, Language.Nl, Language.De), TypeDestinataire.Affilie).Langue.ShouldBe(Language.Nl);
        RegleLinguistique.Determiner(new ContexteLinguistique(null, null, null, null), TypeDestinataire.Affilie).Langue.ShouldBe(Language.Fr);
    }

    [Fact]
    public void L_anglais_n_est_jamais_retenu_sans_demande_explicite()
    {
        var choix = RegleLinguistique.Determiner(new ContexteLinguistique(null, null, Language.En, Language.En), TypeDestinataire.Personne);

        choix.Langue.ShouldBe(Language.Fr);
    }
}
