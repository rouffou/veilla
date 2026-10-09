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

    private static Adresse Adr(string rue) => new(rue, "1", null, "1000", "Bruxelles");

    private static DemandeMutationRegistreNational ChangementAdresse(string reference, DateOnly effet, string rue) =>
        Demande(reference, TypeMutationRegistreNational.ChangementAdresse, effet) with { Adresse = Adr(rue) };

    [Fact]
    public void Une_mutation_d_adresse_plus_ancienne_recue_apres_une_plus_recente_est_historisee_sans_changer_l_adresse_courante()
    {
        var personne = Travailleuse();
        var (recente, _) = personne.AppliquerMutation(ChangementAdresse("RN-2", new DateOnly(2026, 9, 1), "Rue Récente"));

        var (tardive, appliquee) = personne.AppliquerMutation(ChangementAdresse("RN-1", new DateOnly(2026, 3, 1), "Rue Ancienne"));

        appliquee.ShouldBeTrue();
        personne.Adresse!.Rue.ShouldBe("Rue Récente");
        personne.Mutations.Count.ShouldBe(2);
        personne.EstValeurCourante(recente).ShouldBeTrue();
        personne.EstValeurCourante(tardive).ShouldBeFalse();

        // Avant / après de la mutation tardive : la valeur en vigueur à sa date d'effet (celle d'origine), puis la nouvelle.
        tardive.Avant!.ShouldContain("Rue de la Loi");
        tardive.Apres!.ShouldContain("Rue Ancienne");
    }

    [Fact]
    public void Une_mutation_tardive_s_intercale_dans_l_historique_apres_la_mutation_qui_la_precede()
    {
        var personne = Travailleuse();
        personne.AppliquerMutation(ChangementAdresse("RN-1", new DateOnly(2026, 2, 1), "Rue A"));
        personne.AppliquerMutation(ChangementAdresse("RN-3", new DateOnly(2026, 9, 1), "Rue C"));

        var (milieu, _) = personne.AppliquerMutation(ChangementAdresse("RN-2", new DateOnly(2026, 5, 1), "Rue B"));

        personne.Adresse!.Rue.ShouldBe("Rue C");
        milieu.Avant!.ShouldContain("Rue A");
        milieu.Apres!.ShouldContain("Rue B");
    }

    [Fact]
    public void Une_mutation_plus_ancienne_que_toutes_les_autres_a_pour_avant_la_valeur_d_origine()
    {
        var personne = Travailleuse();
        personne.AppliquerMutation(ChangementAdresse("RN-1", new DateOnly(2026, 6, 1), "Rue A"));

        var (premiere, _) = personne.AppliquerMutation(ChangementAdresse("RN-0", new DateOnly(2026, 1, 15), "Rue Z"));

        personne.Adresse!.Rue.ShouldBe("Rue A");
        premiere.Avant!.ShouldContain("Rue de la Loi");
    }

    [Fact]
    public void A_dates_d_effet_egales_la_derniere_mutation_recue_l_emporte()
    {
        var personne = Travailleuse();
        var (premiere, _) = personne.AppliquerMutation(ChangementAdresse("RN-1", Effet, "Rue Première"));

        var (seconde, _) = personne.AppliquerMutation(ChangementAdresse("RN-2", Effet, "Rue Seconde"));

        personne.Adresse!.Rue.ShouldBe("Rue Seconde");
        personne.EstValeurCourante(seconde).ShouldBeTrue();
        personne.EstValeurCourante(premiere).ShouldBeFalse();
        (premiere.Rang, seconde.Rang).ShouldBe((1, 2));
    }

    [Fact]
    public void L_ordre_des_dates_d_effet_est_independant_par_attribut()
    {
        var personne = Travailleuse();
        personne.AppliquerMutation(Demande("RN-1", TypeMutationRegistreNational.ChangementNom, new DateOnly(2026, 9, 1)) with { Nom = "Martin" });

        // Un prénom plus ancien que le nom reste appliqué : aucune mutation de prénom n'est plus récente.
        personne.AppliquerMutation(Demande("RN-2", TypeMutationRegistreNational.ChangementPrenom, new DateOnly(2026, 3, 1)) with { Prenom = "Claire" });
        personne.AppliquerMutation(Demande("RN-3", TypeMutationRegistreNational.ChangementNom, new DateOnly(2026, 2, 1)) with { Nom = "Durand" });

        (personne.Nom, personne.Prenom).ShouldBe(("Martin", "Claire"));
    }

    [Fact]
    public void Une_langue_ou_un_nom_anterieurs_recus_tardivement_ne_remplacent_pas_la_valeur_courante()
    {
        var personne = Travailleuse();
        personne.AppliquerMutation(Demande("RN-1", TypeMutationRegistreNational.ChangementLangue, new DateOnly(2026, 9, 1)) with { Langue = Language.Nl });
        personne.AppliquerMutation(Demande("RN-2", TypeMutationRegistreNational.ChangementLangue, new DateOnly(2026, 4, 1)) with { Langue = Language.De });

        personne.Langue.ShouldBe(Language.Nl);
        personne.Mutations.Count.ShouldBe(2);
    }

    [Fact]
    public void Le_deces_n_est_pas_soumis_a_l_ordre_des_dates_d_effet()
    {
        var personne = Travailleuse();
        personne.AppliquerMutation(Demande("RN-1", TypeMutationRegistreNational.Deces, new DateOnly(2026, 9, 15)));

        // Une autre date de décès reste refusée : la correction est manuelle, même si elle est plus ancienne.
        Should.Throw<DomainException>(() => personne.AppliquerMutation(Demande("RN-2", TypeMutationRegistreNational.Deces, new DateOnly(2026, 9, 1))))
            .Message.ShouldContain("traitée manuellement");
        personne.DateDeces.ShouldBe(new DateOnly(2026, 9, 15));
    }

    [Fact]
    public void Une_occupation_qui_debute_apres_le_deces_est_refusee_avec_un_code_explicite()
    {
        var personne = Travailleuse();
        personne.AppliquerMutation(Demande("RN-9", TypeMutationRegistreNational.Deces, new DateOnly(2026, 9, 15)));
        personne.ClearDomainEvents();

        var nouvelle = new NouvelleOccupation(Affilie, null, TypeTravailleur.Salarie, TypeContrat.DureeIndeterminee, new DateOnly(2026, 9, 16), null, "DIM1");

        Should.Throw<OccupationApresDecesException>(() => personne.DebuterOccupation(nouvelle))
            .Message.ShouldContain("après le décès");
        OccupationApresDecesException.Code.ShouldBe("occupation.apres-deces");
        personne.Occupations.ShouldBeEmpty();
        personne.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Une_occupation_qui_debute_le_jour_du_deces_ou_avant_est_cloturee_a_la_date_du_deces()
    {
        var personne = Travailleuse();
        var deces = new DateOnly(2026, 9, 15);
        personne.AppliquerMutation(Demande("RN-9", TypeMutationRegistreNational.Deces, deces));
        personne.ClearDomainEvents();

        var ouverte = personne.DebuterOccupation(new NouvelleOccupation(Affilie, null, TypeTravailleur.Salarie, TypeContrat.DureeIndeterminee, new DateOnly(2026, 6, 1), null, null));
        var jourDuDeces = personne.DebuterOccupation(new NouvelleOccupation(Guid.CreateVersion7(), null, TypeTravailleur.Salarie, TypeContrat.DureeIndeterminee, deces, null, null));
        var finPlusTard = personne.DebuterOccupation(new NouvelleOccupation(Guid.CreateVersion7(), null, TypeTravailleur.Salarie, TypeContrat.DureeDeterminee, new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31), null));
        var finAvant = personne.DebuterOccupation(new NouvelleOccupation(Guid.CreateVersion7(), null, TypeTravailleur.Salarie, TypeContrat.DureeDeterminee, new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31), null));

        ouverte.DateFin.ShouldBe(deces);
        jourDuDeces.DateFin.ShouldBe(deces);
        finPlusTard.DateFin.ShouldBe(deces);
        finAvant.DateFin.ShouldBe(new DateOnly(2026, 8, 31));
        personne.DomainEvents.OfType<OccupationCloturee>().Count().ShouldBe(4);
    }
}
