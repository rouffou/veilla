using Sepp.BuildingBlocks.Domain;
using Sepp.Personnes.Domain.Personnes;

using Shouldly;

namespace Sepp.Personnes.Domain.Tests;

/// <summary>AFF-20, AFF-22, DAT-04 : mutations du registre national appliquées à une personne.</summary>
public class MutationsRegistreNationalTests
{
    private static readonly Guid Affilie = Guid.CreateVersion7();
    private static readonly Guid Poste = Guid.CreateVersion7();
    private static readonly Guid Site = Guid.CreateVersion7();
    private static readonly DateOnly Debut = new(2026, 1, 1);
    private static readonly DateOnly Effet = new(2026, 9, 1);

    private static Personne Travailleuse() => Personne.Creer(
        Niss.Parse(NissTests.Generer("850730", 42)),
        "HASH",
        new Identite("Dupont", "Marie", new DateOnly(1985, 7, 30), Sexe.Feminin, Language.Fr),
        new Coordonnees(new Adresse("Rue de la Loi", "16", null, "1000", "Bruxelles"), null, null, CanalCommunication.Courrier));

    private static Occupation Occupation(Personne personne, DateOnly? fin = null, Guid? affilie = null) =>
        personne.DebuterOccupation(new NouvelleOccupation(affilie ?? Affilie, null, TypeTravailleur.Salarie, TypeContrat.DureeIndeterminee, Debut, fin, null));

    private static DemandeMutationRegistreNational Demande(string reference, TypeMutationRegistreNational type, DateOnly? effet = null) =>
        new(reference, type, effet ?? Effet);

    [Fact]
    public void Un_changement_d_adresse_remplace_l_adresse_et_est_historise()
    {
        var personne = Travailleuse();
        var nouvelle = new Adresse("Avenue Louise", "1", "B", "1050", "Ixelles");

        var (mutation, appliquee) = personne.AppliquerMutation(Demande("RN-1", TypeMutationRegistreNational.ChangementAdresse) with { Adresse = nouvelle });

        appliquee.ShouldBeTrue();
        personne.Adresse.ShouldBe(nouvelle);
        mutation.Avant!.ShouldContain("Rue de la Loi");
        mutation.Apres!.ShouldContain("Avenue Louise");
        mutation.DateEffet.ShouldBe(Effet);
        personne.Mutations.ShouldHaveSingleItem().ShouldBe(mutation);
    }

    [Fact]
    public void Un_changement_de_nom_de_prenom_et_de_langue_est_historise_avec_les_valeurs_avant_et_apres()
    {
        var personne = Travailleuse();

        personne.AppliquerMutation(Demande("RN-1", TypeMutationRegistreNational.ChangementNom) with { Nom = " Martin " });
        personne.AppliquerMutation(Demande("RN-2", TypeMutationRegistreNational.ChangementPrenom) with { Prenom = "Marie-Claire" });
        personne.AppliquerMutation(Demande("RN-3", TypeMutationRegistreNational.ChangementLangue) with { Langue = Language.Nl });

        (personne.Nom, personne.Prenom, personne.Langue).ShouldBe(("Martin", "Marie-Claire", Language.Nl));
        personne.Mutations.Select(m => (m.Reference, m.Avant, m.Apres)).ShouldBe(
        [
            ("RN-1", "\"Dupont\"", "\"Martin\""),
            ("RN-2", "\"Marie\"", "\"Marie-Claire\""),
            ("RN-3", "\"Fr\"", "\"Nl\""),
        ]);
    }

    [Fact]
    public void Rejouer_une_mutation_avec_la_meme_reference_ne_change_rien()
    {
        var personne = Travailleuse();
        var demande = Demande("RN-1", TypeMutationRegistreNational.ChangementNom) with { Nom = "Martin" };
        var (premiere, _) = personne.AppliquerMutation(demande);
        personne.AppliquerMutation(Demande("RN-2", TypeMutationRegistreNational.ChangementNom) with { Nom = "Durand" });

        var (rejeu, appliquee) = personne.AppliquerMutation(demande);

        appliquee.ShouldBeFalse();
        rejeu.ShouldBe(premiere);
        personne.Nom.ShouldBe("Durand");
        personne.Mutations.Count.ShouldBe(2);
    }

    [Fact]
    public void Une_reference_reutilisee_pour_une_autre_mutation_est_refusee()
    {
        var personne = Travailleuse();
        personne.AppliquerMutation(Demande("RN-1", TypeMutationRegistreNational.ChangementNom) with { Nom = "Martin" });

        Should.Throw<DomainException>(() => personne.AppliquerMutation(Demande("RN-1", TypeMutationRegistreNational.ChangementPrenom) with { Prenom = "Anne" }))
            .Message.ShouldContain("déjà utilisée");
    }

    [Theory]
    [InlineData(TypeMutationRegistreNational.ChangementAdresse)]
    [InlineData(TypeMutationRegistreNational.ChangementNom)]
    [InlineData(TypeMutationRegistreNational.ChangementPrenom)]
    [InlineData(TypeMutationRegistreNational.ChangementLangue)]
    public void Une_mutation_sans_sa_valeur_est_refusee_et_ne_change_rien(TypeMutationRegistreNational type)
    {
        var personne = Travailleuse();

        Should.Throw<DomainException>(() => personne.AppliquerMutation(Demande("RN-1", type)));

        personne.Mutations.ShouldBeEmpty();
        (personne.Nom, personne.Prenom, personne.Langue).ShouldBe(("Dupont", "Marie", Language.Fr));
    }

    [Fact]
    public void Une_reference_vide_est_refusee() =>
        Should.Throw<DomainException>(() => Travailleuse().AppliquerMutation(Demande("  ", TypeMutationRegistreNational.Deces)));

    [Fact]
    public void Un_deces_clot_les_occupations_actives_et_leurs_affectations_a_la_date_du_deces()
    {
        var personne = Travailleuse();
        var active = Occupation(personne);
        personne.Affecter(active.Id, Poste, Site, Debut);
        var deja = Occupation(personne, fin: new DateOnly(2026, 8, 31), affilie: Guid.CreateVersion7());
        personne.ClearDomainEvents();
        var deces = new DateOnly(2026, 9, 15);

        personne.AppliquerMutation(Demande("RN-9", TypeMutationRegistreNational.Deces, deces));

        personne.DateDeces.ShouldBe(deces);
        active.DateFin.ShouldBe(deces);
        active.Affectations.ShouldHaveSingleItem().Validite.ValidTo.ShouldBe(deces.AddDays(1));
        deja.DateFin.ShouldBe(new DateOnly(2026, 8, 31));
        personne.DomainEvents.OfType<OccupationCloturee>().ShouldHaveSingleItem().DateFin.ShouldBe(deces);
        personne.DomainEvents.OfType<AffectationHistorisee>().ShouldHaveSingleItem();
        personne.Mutations.ShouldHaveSingleItem().Apres.ShouldBe("\"2026-09-15\"");
    }

    [Fact]
    public void Un_deces_avance_la_sortie_deja_declaree_apres_la_date_du_deces()
    {
        var personne = Travailleuse();
        var occupation = Occupation(personne, fin: new DateOnly(2026, 12, 31));
        personne.ClearDomainEvents();

        personne.AppliquerMutation(Demande("RN-9", TypeMutationRegistreNational.Deces, new DateOnly(2026, 9, 15)));

        occupation.DateFin.ShouldBe(new DateOnly(2026, 9, 15));
        personne.DomainEvents.OfType<OccupationCloturee>().ShouldHaveSingleItem().DateFin.ShouldBe(new DateOnly(2026, 9, 15));
    }

    [Fact]
    public void Un_second_deces_a_la_meme_date_est_historise_sans_nouvel_evenement()
    {
        var personne = Travailleuse();
        Occupation(personne);
        personne.AppliquerMutation(Demande("RN-9", TypeMutationRegistreNational.Deces, new DateOnly(2026, 9, 15)));
        personne.ClearDomainEvents();

        var (_, appliquee) = personne.AppliquerMutation(Demande("RN-10", TypeMutationRegistreNational.Deces, new DateOnly(2026, 9, 15)));

        appliquee.ShouldBeTrue();
        personne.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Un_deces_a_une_autre_date_que_celle_deja_enregistree_est_refuse()
    {
        var personne = Travailleuse();
        personne.AppliquerMutation(Demande("RN-9", TypeMutationRegistreNational.Deces, new DateOnly(2026, 9, 15)));

        Should.Throw<DomainException>(() => personne.AppliquerMutation(Demande("RN-10", TypeMutationRegistreNational.Deces, new DateOnly(2026, 9, 20))))
            .Message.ShouldContain("déjà enregistré");
    }

    [Fact]
    public void Un_deces_anterieur_a_la_naissance_ou_au_debut_d_une_occupation_est_refuse_sans_rien_modifier()
    {
        var personne = Travailleuse();
        var occupation = Occupation(personne);

        Should.Throw<DomainException>(() => personne.AppliquerMutation(Demande("RN-1", TypeMutationRegistreNational.Deces, new DateOnly(1985, 1, 1))))
            .Message.ShouldContain("naissance");
        Should.Throw<DomainException>(() => personne.AppliquerMutation(Demande("RN-2", TypeMutationRegistreNational.Deces, new DateOnly(2025, 12, 31))))
            .Message.ShouldContain("précède le début");

        personne.DateDeces.ShouldBeNull();
        occupation.DateFin.ShouldBeNull();
        personne.Mutations.ShouldBeEmpty();
    }
}
