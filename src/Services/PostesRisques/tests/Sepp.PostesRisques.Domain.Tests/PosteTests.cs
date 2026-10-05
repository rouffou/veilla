using Sepp.BuildingBlocks.Domain;
using Sepp.PostesRisques.Domain.Postes;
using Sepp.PostesRisques.Domain.Risques;

using Shouldly;

namespace Sepp.PostesRisques.Domain.Tests;

public class PosteTests
{
    private static readonly Guid Affilie = Guid.CreateVersion7();
    private static readonly DateTimeOffset Maintenant = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Effet = new(2026, 10, 1);
    private readonly Risque _bruit = RisqueTests.Bruit();

    private static Poste Cariste() => Poste.Creer(Affilie, " Cariste ", null, "ex.cariste");

    private PropositionPosteRisque Proposition(Poste poste, TypeModification type, NiveauExposition? niveau, DateOnly effet) =>
        PropositionPosteRisque.Soumettre(poste, OrigineProposition.PortailEmployeur, "employeur-1", Maintenant, "Nouvelle machine", effet,
            [new DemandeLienRisque(type, _bruit.Id, _bruit.Code, niveau)]);

    private Poste PosteExpose()
    {
        var poste = Cariste();
        var proposition = Proposition(poste, TypeModification.Ajout, NiveauExposition.Moyen, Effet);
        proposition.Valider("cpmt-1", Maintenant, new DateOnly(2026, 9, 15), Guid.CreateVersion7());
        poste.AppliquerProposition(proposition);
        return poste;
    }

    [Fact]
    public void Un_poste_est_cree_actif_avec_un_metier_type_normalise()
    {
        var poste = Cariste();

        poste.Intitule.ShouldBe("Cariste");
        poste.MetierTypeCode.ShouldBe("EX.CARISTE");
        poste.Statut.ShouldBe(StatutPoste.Actif);
        poste.Risques.ShouldBeEmpty();
    }

    [Fact]
    public void L_intitule_est_obligatoire() =>
        Should.Throw<DomainException>(() => Poste.Creer(Affilie, " ", null, null));

    [Fact]
    public void Une_proposition_soumise_ne_modifie_pas_le_profil()
    {
        var poste = Cariste();
        var proposition = Proposition(poste, TypeModification.Ajout, NiveauExposition.Eleve, Effet);

        proposition.Statut.ShouldBe(StatutProposition.Soumise);
        Should.Throw<DomainException>(() => poste.AppliquerProposition(proposition));
        poste.Risques.ShouldBeEmpty();
    }

    [Fact]
    public void La_validation_exige_l_avis_du_comite_ppt()
    {
        var poste = Cariste();
        var proposition = Proposition(poste, TypeModification.Ajout, NiveauExposition.Eleve, Effet);

        Should.Throw<DomainException>(() => proposition.Valider("cpmt-1", Maintenant));
        proposition.Statut.ShouldBe(StatutProposition.Soumise);
    }

    [Fact]
    public void Une_proposition_validee_ajoute_le_lien_avec_le_cpmt_et_l_avis()
    {
        var document = Guid.CreateVersion7();
        var poste = Cariste();
        var proposition = Proposition(poste, TypeModification.Ajout, NiveauExposition.Eleve, Effet);
        proposition.JoindreAvisCppt(new DateOnly(2026, 9, 15), document);

        proposition.Valider("cpmt-1", Maintenant);
        poste.AppliquerProposition(proposition);

        var lien = poste.RisquesAu(Effet).ShouldHaveSingleItem();
        lien.RisqueCode.ShouldBe("EX.PHYS.BRUIT");
        lien.ValideParCpmtId.ShouldBe("cpmt-1");
        lien.DateAvisCppt.ShouldBe(new DateOnly(2026, 9, 15));
        lien.DocumentAvisCpptId.ShouldBe(document);
        lien.PropositionId.ShouldBe(proposition.Id);
        poste.RisquesAu(Effet.AddDays(-1)).ShouldBeEmpty();
    }

    [Fact]
    public void Un_changement_de_niveau_cloture_le_lien_et_en_cree_un_nouveau()
    {
        var poste = PosteExpose();
        var modification = Proposition(poste, TypeModification.Modification, NiveauExposition.Eleve, new DateOnly(2027, 1, 1));
        modification.Valider("cpmt-2", Maintenant, new DateOnly(2026, 9, 20), Guid.CreateVersion7());

        poste.AppliquerProposition(modification);

        poste.Risques.Count.ShouldBe(2);
        poste.RisquesAu(new DateOnly(2026, 12, 31)).Single().NiveauExposition.ShouldBe(NiveauExposition.Moyen);
        poste.RisquesAu(new DateOnly(2027, 1, 1)).Single().NiveauExposition.ShouldBe(NiveauExposition.Eleve);
    }

    [Fact]
    public void Un_retrait_valide_cloture_le_lien_et_permet_l_archivage()
    {
        var poste = PosteExpose();
        Should.Throw<DomainException>(() => poste.Archiver(new DateOnly(2026, 11, 1)));

        var retrait = Proposition(poste, TypeModification.Retrait, null, new DateOnly(2027, 1, 1));
        retrait.Valider("cpmt-1", Maintenant, new DateOnly(2026, 9, 20), Guid.CreateVersion7());
        poste.AppliquerProposition(retrait);
        poste.Archiver(new DateOnly(2027, 1, 1));

        poste.RisquesAu(new DateOnly(2027, 1, 1)).ShouldBeEmpty();
        poste.Risques.ShouldHaveSingleItem().Validite.ValidTo.ShouldBe(new DateOnly(2027, 1, 1));
        poste.Statut.ShouldBe(StatutPoste.Archive);
    }

    [Fact]
    public void Ajouter_un_risque_deja_present_est_refuse_a_l_application()
    {
        var poste = PosteExpose();
        var doublon = Proposition(poste, TypeModification.Ajout, NiveauExposition.Faible, new DateOnly(2027, 1, 1));
        doublon.Valider("cpmt-1", Maintenant, new DateOnly(2026, 9, 20), Guid.CreateVersion7());

        Should.Throw<DomainException>(() => poste.AppliquerProposition(doublon));
    }

    [Fact]
    public void Une_proposition_refusee_ne_peut_plus_etre_validee()
    {
        var proposition = Proposition(Cariste(), TypeModification.Ajout, NiveauExposition.Faible, Effet);
        proposition.Refuser("cpmt-1", Maintenant, "Risque non pertinent");

        proposition.Statut.ShouldBe(StatutProposition.Refusee);
        proposition.MotifRefus.ShouldBe("Risque non pertinent");
        Should.Throw<DomainException>(() => proposition.Valider("cpmt-1", Maintenant, new DateOnly(2026, 9, 1), Guid.CreateVersion7()));
    }

    [Fact]
    public void Un_ajout_sans_niveau_d_exposition_est_refuse() =>
        Should.Throw<DomainException>(() => Proposition(Cariste(), TypeModification.Ajout, null, Effet));

    [Fact]
    public void Une_proposition_vide_est_refusee() =>
        Should.Throw<DomainException>(() =>
            PropositionPosteRisque.Soumettre(Cariste(), OrigineProposition.Interne, "gestionnaire", Maintenant, "motif", Effet, []));
}
