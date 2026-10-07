using Sepp.BuildingBlocks.Domain;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Convocations;
using Sepp.Planification.Domain.Ressources;

using Shouldly;

namespace Sepp.Planification.Domain.Tests;

public class ConvocationEtAgendaTests
{
    [Fact]
    public void Un_recommande_ne_passe_que_par_courrier_ou_email()
    {
        var rdv = Fabrique.RendezVous();

        Should.Throw<DomainException>(() => Convocation.Emettre(rdv, CanalConvocation.Sms, true, TypeConvocation.Convocation, null, Fabrique.Maintenant));
        Convocation.Emettre(rdv, CanalConvocation.Email, true, TypeConvocation.Convocation, null, Fabrique.Maintenant).Recommande.ShouldBeTrue();
        Convocation.Emettre(rdv, CanalConvocation.Sms, false, TypeConvocation.Convocation, null, Fabrique.Maintenant).Canal.ShouldBe(CanalConvocation.Sms);
    }

    [Fact]
    public void La_convocation_reprend_la_personne_et_l_affilie_du_rendez_vous()
    {
        var rdv = Fabrique.RendezVous();
        var lot = Guid.CreateVersion7();

        var convocation = Convocation.Emettre(rdv, CanalConvocation.Courrier, false, TypeConvocation.Reconvocation, lot, Fabrique.Maintenant);

        convocation.RendezVousId.ShouldBe(rdv.Id);
        convocation.PersonneId.ShouldBe(rdv.PersonneId);
        convocation.AffilieId.ShouldBe(rdv.AffilieId);
        convocation.LotId.ShouldBe(lot);
        convocation.Type.ShouldBe(TypeConvocation.Reconvocation);
    }

    [Fact]
    public void L_envoi_effectif_est_enregistre_avec_l_identifiant_du_message()
    {
        var convocation = Convocation.Emettre(Fabrique.RendezVous(), CanalConvocation.Email, false, TypeConvocation.Convocation, null, Fabrique.Maintenant);

        convocation.EnregistrerEnvoi(" msg-42 ", Fabrique.Maintenant);

        convocation.MessageId.ShouldBe("msg-42");
        convocation.DateEnvoi.ShouldBe(Fabrique.Maintenant);
        Should.Throw<DomainException>(() => convocation.EnregistrerEnvoi(" ", Fabrique.Maintenant));
    }

    [Theory]
    [InlineData(null, null, CanalConvocation.Courrier, false, CanalConvocation.Courrier)]
    [InlineData(null, CanalConvocation.Email, CanalConvocation.Courrier, false, CanalConvocation.Email)]
    [InlineData(CanalConvocation.Sms, CanalConvocation.Email, CanalConvocation.Courrier, false, CanalConvocation.Sms)]
    [InlineData(CanalConvocation.Sms, CanalConvocation.Email, CanalConvocation.Courrier, true, CanalConvocation.Courrier)]
    [InlineData(null, CanalConvocation.Portail, CanalConvocation.Email, true, CanalConvocation.Courrier)]
    [InlineData(null, CanalConvocation.Email, CanalConvocation.Courrier, true, CanalConvocation.Email)]
    public void Le_canal_suit_la_demande_puis_la_preference_puis_le_defaut(CanalConvocation? demande, CanalConvocation? preference, CanalConvocation defaut,
        bool recommande, CanalConvocation attendu) =>
        PolitiqueCanal.Resoudre(demande, preference, defaut, recommande).ShouldBe(attendu);

    // PLA-09 : aucune donnée médicale dans l'agenda externe.
    [Fact]
    public void L_intitule_de_l_agenda_est_neutre_et_ne_contient_aucune_donnee_medicale()
    {
        var creneau = Fabrique.Creneau(typeActe: "EXAMEN_REPRISE");
        var rdv = Fabrique.RendezVous(creneau);

        var evenement = EvenementAgenda.Pour(rdv, "Centre de Namur");

        evenement.Titre.ShouldBe("Rendez-vous SEPP");
        var contenu = $"{evenement.Titre} {evenement.Description} {evenement.Lieu}";
        foreach (var interdit in new[] { "REPRISE", "reprise", "EXAMEN", "examen", "visite", "VISITE", rdv.PersonneId.ToString(), rdv.AffilieId.ToString(), rdv.Id.ToString(),
                     rdv.ObligationIds[0].ToString(), "santé", "médical" })
        {
            contenu.ShouldNotContain(interdit);
        }

        evenement.Description.ShouldContain(EvenementAgenda.ReferenceCourte(rdv.Id));
        evenement.Debut.ShouldBe(rdv.Debut);
        evenement.Fin.ShouldBe(rdv.Fin);
    }

    [Fact]
    public void Seule_une_ressource_humaine_rattache_un_agenda_externe()
    {
        var conseiller = Ressource.Creer(TypeRessource.Conseiller, "Dr A.", "kc-user-1", ["visite-periodique", "EXAMEN_REPRISE"], null);
        var salle = Ressource.Creer(TypeRessource.Salle, "Salle 2", null, [], null);

        conseiller.RattacherAgendaExterne(FournisseurAgenda.Microsoft365, "a@sepp.test");
        conseiller.CompteAgenda.ShouldBe("a@sepp.test");
        Should.Throw<DomainException>(() => salle.RattacherAgendaExterne(FournisseurAgenda.Google, "salle@sepp.test"));
    }

    [Fact]
    public void Les_competences_sont_normalisees_dedoublonnees_et_triees()
    {
        var ressource = Ressource.Creer(TypeRessource.Appareil, "Audiomètre AU-12", "INV-12", ["spirometrie", "AUDIOMETRIE", "audiometrie"], null);

        ressource.Competences.ShouldBe(["AUDIOMETRIE", "SPIROMETRIE"]);
        ressource.PossedeCompetence("AUDIOMETRIE").ShouldBeTrue();
        ressource.PossedeCompetence("VISITE_PERIODIQUE").ShouldBeFalse();
    }

    [Fact]
    public void Les_regles_des_lieux_sont_appliquees()
    {
        Should.Throw<DomainException>(() => Lieu.Creer(TypeLieu.CabinetEntreprise, "Cabinet", null, "5000", null, null, null));
        Should.Throw<DomainException>(() => Lieu.Creer(TypeLieu.Distance, "Visio", "Rue X 1", null, null, null, null));
        Should.Throw<DomainException>(() => Lieu.Creer(TypeLieu.CentreFixe, " ", null, null, null, null, null));

        var centre = Lieu.Creer(TypeLieu.CentreFixe, "Centre de Namur", "Rue de Fer 1", "5000", new Coordonnees(50.46, 4.86), null, null);
        centre.EstDansZone("50").ShouldBeTrue();
        centre.EstDansZone("40").ShouldBeFalse();
    }

    [Fact]
    public void Les_indisponibilites_sont_stockees_en_utc_meme_si_la_source_donne_un_decalage()
    {
        var debut = new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.FromHours(1));

        var conge = Absence.Creer(Fabrique.Conseiller, debut, debut.AddDays(2), SourceAbsence.OutilRh, "RH-UTC");
        conge.Actualiser(debut, debut.AddDays(3));

        conge.Debut.Offset.ShouldBe(TimeSpan.Zero);
        conge.Fin.Offset.ShouldBe(TimeSpan.Zero);
        conge.Debut.ShouldBe(debut);
    }

    [Fact]
    public void Une_indisponibilite_importee_porte_la_reference_de_sa_source()
    {
        var debut = Fabrique.Instant(Fabrique.Lundi, 9);

        Should.Throw<DomainException>(() => Absence.Creer(Fabrique.Conseiller, debut, debut.AddHours(8), SourceAbsence.OutilRh, null));
        var conge = Absence.Creer(Fabrique.Conseiller, debut, debut.AddHours(8), SourceAbsence.OutilRh, "RH-1");

        conge.Actualiser(debut, debut.AddHours(8)).ShouldBeFalse();
        conge.Actualiser(debut, debut.AddHours(16)).ShouldBeTrue();
        conge.Chevauche(debut.AddHours(10), debut.AddHours(11)).ShouldBeTrue();
        conge.Chevauche(debut.AddHours(16), debut.AddHours(17)).ShouldBeFalse();
    }
}
