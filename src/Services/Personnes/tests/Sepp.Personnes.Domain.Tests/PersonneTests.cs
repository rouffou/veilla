using Sepp.BuildingBlocks.Domain;
using Sepp.Personnes.Domain.Personnes;

using Shouldly;

namespace Sepp.Personnes.Domain.Tests;

public class PersonneTests
{
    private static readonly Guid Affilie = Guid.CreateVersion7();
    private static readonly Guid Agence = Guid.CreateVersion7();
    private static readonly Guid Poste = Guid.CreateVersion7();
    private static readonly Guid Site = Guid.CreateVersion7();
    private static readonly DateOnly Debut = new(2026, 1, 1);

    private static Personne Travailleuse(string aammjj = "850730", DateOnly? naissance = null) => Personne.Creer(
        Niss.Parse(NissTests.Generer(aammjj, 42)),
        "HASH",
        new Identite("Dupont", "Marie", naissance ?? new DateOnly(1985, 7, 30), Sexe.Feminin, Language.Fr),
        new Coordonnees(null, "marie@example.test", null, CanalCommunication.Email));

    private static NouvelleOccupation Occupation(DateOnly? fin = null, string? dimona = null) =>
        new(Affilie, null, TypeTravailleur.Salarie, TypeContrat.DureeIndeterminee, Debut, fin, dimona);

    [Fact]
    public void La_date_de_naissance_doit_correspondre_au_niss() =>
        Should.Throw<DomainException>(() => Travailleuse(naissance: new DateOnly(1985, 7, 31)))
            .Message.ShouldContain("date de naissance");

    [Fact]
    public void Le_canal_prefere_exige_la_coordonnee_correspondante() =>
        Should.Throw<DomainException>(() => Travailleuse().ModifierCoordonnees(new Coordonnees(null, null, null, CanalCommunication.Sms)));

    [Fact]
    public void Debuter_une_occupation_leve_un_evenement()
    {
        var personne = Travailleuse();

        var occupation = personne.DebuterOccupation(Occupation(dimona: "dim123"));

        occupation.ReferenceDimona.ShouldBe("DIM123");
        personne.DomainEvents.OfType<OccupationEnregistree>().ShouldHaveSingleItem().OccupationId.ShouldBe(occupation.Id);
    }

    [Fact]
    public void Une_occupation_chevauchante_chez_le_meme_affilie_est_un_doublon()
    {
        var personne = Travailleuse();
        personne.DebuterOccupation(Occupation());

        Should.Throw<DomainException>(() => personne.DebuterOccupation(Occupation() with { DateDebut = new DateOnly(2026, 6, 1) }))
            .Message.ShouldContain("Doublon");
    }

    [Fact]
    public void Un_interimaire_exige_un_affilie_utilisateur_distinct_de_l_agence()
    {
        var personne = Travailleuse();
        var interim = new NouvelleOccupation(Agence, null, TypeTravailleur.Interimaire, TypeContrat.Interim, Debut, null, null);

        Should.Throw<DomainException>(() => personne.DebuterOccupation(interim));
        Should.Throw<DomainException>(() => personne.DebuterOccupation(interim with { AffilieUtilisateurId = Agence }));
        var occupation = personne.DebuterOccupation(interim with { AffilieUtilisateurId = Affilie });

        occupation.AffilieUtilisateurId.ShouldBe(Affilie);
        personne.EstRattacheA(Affilie).ShouldBeTrue();
        personne.EstRattacheA(Agence).ShouldBeTrue();
    }

    [Fact]
    public void Seul_un_interimaire_a_un_affilie_utilisateur() =>
        Should.Throw<DomainException>(() => Travailleuse().DebuterOccupation(Occupation() with { AffilieUtilisateurId = Agence }));

    [Fact]
    public void Terminer_une_occupation_cloture_les_affectations_en_cours_le_lendemain()
    {
        var personne = Travailleuse();
        var occupation = personne.DebuterOccupation(Occupation());
        var affectation = personne.Affecter(occupation.Id, Poste, Site, Debut);
        personne.ClearDomainEvents();

        personne.TerminerOccupation(occupation.Id, new DateOnly(2026, 3, 31)).ShouldBeTrue();

        occupation.DateFin.ShouldBe(new DateOnly(2026, 3, 31));
        affectation.Validite.ValidTo.ShouldBe(new DateOnly(2026, 4, 1));
        personne.DomainEvents.OfType<AffectationHistorisee>().ShouldHaveSingleItem();
        personne.DomainEvents.OfType<OccupationCloturee>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Terminer_deux_fois_a_la_meme_date_est_sans_effet()
    {
        var personne = Travailleuse();
        var occupation = personne.DebuterOccupation(Occupation());
        personne.TerminerOccupation(occupation.Id, new DateOnly(2026, 3, 31));

        personne.TerminerOccupation(occupation.Id, new DateOnly(2026, 3, 31)).ShouldBeFalse();
        Should.Throw<DomainException>(() => personne.TerminerOccupation(occupation.Id, new DateOnly(2026, 4, 30)));
    }

    [Fact]
    public void Une_affectation_doit_commencer_pendant_l_occupation_et_ne_lui_survit_pas()
    {
        var personne = Travailleuse();
        var occupation = personne.DebuterOccupation(Occupation(fin: new DateOnly(2026, 6, 30)));

        Should.Throw<DomainException>(() => personne.Affecter(occupation.Id, Poste, Site, new DateOnly(2025, 12, 31)));
        var affectation = personne.Affecter(occupation.Id, Poste, Site, Debut);

        affectation.Validite.ValidTo.ShouldBe(new DateOnly(2026, 7, 1));
    }

    [Fact]
    public void Plusieurs_postes_sont_possibles_mais_pas_deux_fois_le_meme_poste_sur_une_meme_periode()
    {
        var personne = Travailleuse();
        var occupation = personne.DebuterOccupation(Occupation());
        personne.Affecter(occupation.Id, Poste, Site, Debut);

        personne.Affecter(occupation.Id, Guid.CreateVersion7(), Site, Debut);
        Should.Throw<DomainException>(() => personne.Affecter(occupation.Id, Poste, Site, new DateOnly(2026, 2, 1)));
        personne.Affectations.Count().ShouldBe(2);
    }

    [Fact]
    public void Changer_d_affectation_cloture_l_ancienne_et_en_cree_une_nouvelle()
    {
        var personne = Travailleuse();
        var occupation = personne.DebuterOccupation(Occupation());
        var ancienne = personne.Affecter(occupation.Id, Poste, Site, Debut);
        var autrePoste = Guid.CreateVersion7();

        var nouvelle = personne.ChangerAffectation(ancienne.Id, autrePoste, Site, new DateOnly(2026, 5, 1));

        ancienne.Validite.ShouldBe(new Validity(Debut, new DateOnly(2026, 5, 1)));
        nouvelle.Validite.ShouldBe(new Validity(new DateOnly(2026, 5, 1)));
        nouvelle.PosteId.ShouldBe(autrePoste);
        personne.DomainEvents.OfType<AffectationHistorisee>().Count().ShouldBe(3);
    }

    [Fact]
    public void Une_affectation_cloturee_ne_se_reecrit_pas()
    {
        var personne = Travailleuse();
        var occupation = personne.DebuterOccupation(Occupation());
        var affectation = personne.Affecter(occupation.Id, Poste, Site, Debut);
        personne.TerminerAffectation(affectation.Id, new DateOnly(2026, 5, 1)).ShouldBeTrue();

        personne.TerminerAffectation(affectation.Id, new DateOnly(2026, 5, 1)).ShouldBeFalse();
        Should.Throw<DomainException>(() => personne.TerminerAffectation(affectation.Id, new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void Declarer_une_grossesse_leve_un_evenement_et_refuse_un_chevauchement()
    {
        var personne = Travailleuse();
        personne.DebuterOccupation(Occupation());

        var etat = personne.DeclarerEtatParticulier(TypeEtatParticulier.Grossesse, new DateOnly(2026, 3, 1), null, Affilie);

        etat.AffilieDeclarantId.ShouldBe(Affilie);
        personne.DomainEvents.OfType<EtatParticulierEnregistre>().ShouldHaveSingleItem().Type.ShouldBe(TypeEtatParticulier.Grossesse);
        Should.Throw<DomainException>(() => personne.DeclarerEtatParticulier(TypeEtatParticulier.Grossesse, new DateOnly(2026, 4, 1), null));
    }

    [Fact]
    public void Un_employeur_ne_declare_que_pour_un_travailleur_qu_il_occupe() =>
        Should.Throw<DomainException>(() => Travailleuse().DeclarerEtatParticulier(TypeEtatParticulier.Allaitement, Debut, null, Agence));

    [Fact]
    public void Le_statut_de_jeune_travailleur_s_arrete_a_18_ans()
    {
        var jeune = Personne.Creer(Niss.Parse(NissTests.Generer("100115", 7, apres2000: true)), "HASH2",
            new Identite("Martin", "Léo", new DateOnly(2010, 1, 15), Sexe.Masculin, Language.Nl), new Coordonnees(null, null, null, CanalCommunication.Portail));

        jeune.DeclarerEtatParticulier(TypeEtatParticulier.Jeune, new DateOnly(2026, 7, 1), new DateOnly(2028, 1, 14));
        Should.Throw<DomainException>(() => jeune.DeclarerEtatParticulier(TypeEtatParticulier.Jeune, new DateOnly(2028, 1, 15), null));
    }

    [Fact]
    public void La_fin_d_un_etat_particulier_peut_etre_anticipee_mais_pas_prolongee()
    {
        var personne = Travailleuse();
        var etat = personne.DeclarerEtatParticulier(TypeEtatParticulier.Allaitement, Debut, new DateOnly(2026, 6, 30));

        personne.TerminerEtatParticulier(etat.Id, new DateOnly(2026, 4, 30)).ShouldBeTrue();
        Should.Throw<DomainException>(() => personne.TerminerEtatParticulier(etat.Id, new DateOnly(2026, 5, 31)));
        etat.EstActifAu(new DateOnly(2026, 5, 1)).ShouldBeFalse();
    }
}
