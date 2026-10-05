using Sepp.BuildingBlocks.Domain;
using Sepp.PostesRisques.Domain.Listes;
using Sepp.PostesRisques.Domain.Projections;
using Sepp.PostesRisques.Domain.Risques;
using Sepp.PostesRisques.Domain.Surcharges;

using Shouldly;

namespace Sepp.PostesRisques.Domain.Tests;

public class ListesEtSurchargesTests
{
    private static readonly Guid Affilie = Guid.CreateVersion7();
    private static readonly Guid Poste = Guid.CreateVersion7();
    private static readonly Guid Travailleur = Guid.CreateVersion7();
    private static readonly DateTimeOffset Maintenant = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);

    private static ListeNominative Liste(params LigneCalculee[] lignes) =>
        ListeNominative.Generer(Affilie, TypeListeNominative.PosteSecurite, 1, new DateOnly(2026, 9, 28), Maintenant, "gestionnaire", 5, lignes);

    [Fact]
    public void Une_liste_est_conservee_au_moins_la_duree_legale_et_dedoublonne_ses_lignes()
    {
        var ligne = new LigneCalculee(Travailleur, Poste, ["EX.SECU.B", "EX.SECU.A"], new DateOnly(2026, 3, 1), OrigineLigne.Calcul);

        var liste = Liste(ligne, ligne);

        liste.ConserverJusquAu.ShouldBe(new DateOnly(2031, 9, 28));
        var unique = liste.Lignes.ShouldHaveSingleItem();
        unique.CodesRisques.ShouldBe(["EX.SECU.A", "EX.SECU.B"]);
        unique.DateDerniereEvaluation.ShouldBe(new DateOnly(2026, 3, 1));
    }

    [Fact]
    public void Les_categories_de_risque_determinent_le_type_de_liste()
    {
        ListeNominative.CategoriesDe(TypeListeNominative.PosteSecurite).ShouldContain(CategorieRisque.Conduite);
        ListeNominative.CategoriesDe(TypeListeNominative.ActiviteRisqueDefini).ShouldContain(CategorieRisque.Chimique);
        ListeNominative.CategoriesDe(TypeListeNominative.PosteVigilance).ShouldNotContain(CategorieRisque.PosteSecurite);
        foreach (var type in Enum.GetValues<TypeListeNominative>())
        {
            ListeNominative.CategoriesDe(type).ShouldNotBeEmpty();
        }
    }

    [Fact]
    public void Une_proposition_de_liste_produit_les_lignes_de_la_nouvelle_version()
    {
        var autre = Guid.CreateVersion7();
        var liste = Liste(new LigneCalculee(Travailleur, Poste, ["EX.SECU.A"], null, OrigineLigne.Calcul));
        var proposition = PropositionListeNominative.Soumettre(liste, OrigineProposition.PortailEmployeur, "employeur", Maintenant, "Mise à jour",
            [new DemandeLigneListe(TypeModification.Retrait, Travailleur, Poste), new DemandeLigneListe(TypeModification.Ajout, autre, Poste)]);

        var lignes = proposition.AppliquerA(liste, l => new LigneCalculee(l.PersonneId, l.PosteId, ["EX.SECU.A"], null, OrigineLigne.AjustementValide));
        var nouvelle = ListeNominative.Generer(Affilie, liste.Type, 2, liste.DateReference, Maintenant, "cpmt", 5, lignes, proposition.Id);
        proposition.Valider("cpmt", Maintenant, nouvelle);

        nouvelle.Lignes.ShouldHaveSingleItem().PersonneId.ShouldBe(autre);
        nouvelle.Lignes[0].Origine.ShouldBe(OrigineLigne.AjustementValide);
        proposition.ListeResultanteId.ShouldBe(nouvelle.Id);
        liste.Lignes.ShouldHaveSingleItem().PersonneId.ShouldBe(Travailleur);
    }

    [Fact]
    public void Retirer_un_travailleur_absent_de_la_liste_est_refuse() =>
        Should.Throw<DomainException>(() => PropositionListeNominative.Soumettre(Liste(), OrigineProposition.PortailEmployeur, "employeur", Maintenant,
            "motif", [new DemandeLigneListe(TypeModification.Retrait, Travailleur, Poste)]));

    [Fact]
    public void Une_surcharge_se_cloture_sans_etre_supprimee()
    {
        var surcharge = SurchargeFrequence.Definir(Affilie, CibleSurcharge.Personne, Travailleur, RisqueTests.Bruit(), 12, "Suivi rapproché",
            "cpmt-1", new DateOnly(2026, 10, 1), null);

        surcharge.Cloturer(new DateOnly(2027, 10, 1));

        surcharge.Validite.ValidTo.ShouldBe(new DateOnly(2027, 10, 1));
        surcharge.RisqueCode.ShouldBe("EX.PHYS.BRUIT");
        Should.Throw<DomainException>(() => surcharge.Cloturer(new DateOnly(2028, 1, 1)));
    }

    [Fact]
    public void Une_surcharge_exige_un_motif() =>
        Should.Throw<DomainException>(() => SurchargeFrequence.Definir(Affilie, CibleSurcharge.Poste, Poste, RisqueTests.Bruit(), 12, " ",
            "cpmt-1", new DateOnly(2026, 10, 1), null));

    [Fact]
    public void Une_projection_d_affectation_ignore_un_evenement_plus_ancien()
    {
        var affectation = new AffectationPoste(Guid.CreateVersion7(), Travailleur, Poste, new DateOnly(2026, 1, 1), null, Maintenant);

        affectation.Appliquer(Travailleur, Poste, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), Maintenant.AddHours(-1)).ShouldBeFalse();
        affectation.EstActiveAu(new DateOnly(2026, 9, 1)).ShouldBeTrue();

        affectation.Appliquer(Travailleur, Poste, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), Maintenant.AddHours(1)).ShouldBeTrue();
        affectation.EstActiveAu(new DateOnly(2026, 6, 30)).ShouldBeTrue();
        affectation.EstActiveAu(new DateOnly(2026, 7, 1)).ShouldBeFalse();
    }
}
