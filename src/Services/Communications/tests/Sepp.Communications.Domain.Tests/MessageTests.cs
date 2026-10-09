using Sepp.BuildingBlocks.Domain;
using Sepp.Communications.Domain.Messages;

using Shouldly;

namespace Sepp.Communications.Domain.Tests;

/// <summary>DOC-03 à DOC-05 : message, statuts, reprises et preuves d'envoi.</summary>
public class MessageTests
{
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
    private static readonly PolitiqueReprise Politique = PolitiqueReprise.Defaut;

    private static Message Nouveau(Canal canal = Canal.Email, bool recommande = false, ContenuMessage? contenu = null, string cle = "cle-1")
    {
        var id = Message.NouvelIdentifiant();
        return Message.Creer(new NouveauMessage(id, TypeMessage.NotificationDocument, canal, recommande, TypeDestinataire.Personne, Guid.CreateVersion7(), Language.Fr,
            "document", Guid.CreateVersion7(), cle, contenu ?? Gabarits.Generique(canal, Language.Fr, "https://exemple.test/m/1")), Maintenant);
    }

    [Fact]
    public void Un_message_est_cree_en_attente_et_echu_immediatement()
    {
        var message = Nouveau();

        message.Statut.ShouldBe(StatutMessage.EnAttente);
        message.EstEchu(Maintenant).ShouldBeTrue();
        message.EstEchu(Maintenant.AddSeconds(-1)).ShouldBeFalse();
        message.ContenuGenerique.ShouldBeTrue();
    }

    [Theory]
    [InlineData(Canal.Email)]
    [InlineData(Canal.Sms)]
    public void Un_email_ou_un_sms_refuse_tout_contenu_detaille(Canal canal) =>
        Should.Throw<DomainException>(() => Nouveau(canal, contenu: new ContenuMessage("Résultat de l'examen", "Vous êtes apte.", EstGenerique: false)))
            .Message.ShouldContain("générique");

    [Theory]
    [InlineData(Canal.Portail)]
    [InlineData(Canal.Courrier)]
    [InlineData(Canal.EBoxCitoyen)]
    [InlineData(Canal.EBoxEntreprise)]
    public void Les_canaux_securises_acceptent_un_contenu_detaille(Canal canal) =>
        Should.NotThrow(() => Nouveau(canal, contenu: new ContenuMessage("Convocation", "Le 12 octobre à 9 h.", EstGenerique: false)));

    [Fact]
    public void Un_sms_est_limite_en_longueur() =>
        Should.Throw<DomainException>(() => Nouveau(Canal.Sms, contenu: new ContenuMessage("x", new string('a', Message.LongueurMaximaleSms + 1), true)));

    [Fact]
    public void Le_recommande_passe_par_le_recommande_electronique_ou_le_courrier()
    {
        Should.Throw<DomainException>(() => Nouveau(Canal.Email, recommande: true));
        Should.Throw<DomainException>(() => Nouveau(Canal.RecommandeElectronique, recommande: false));
        Should.NotThrow(() => Nouveau(Canal.RecommandeElectronique, recommande: true));
        Should.NotThrow(() => Nouveau(Canal.Courrier, recommande: true));
    }

    [Fact]
    public void Un_envoi_reussi_conserve_une_preuve_liee_au_contenu()
    {
        var message = Nouveau();

        var preuve = message.EnregistrerEnvoi(Maintenant.AddSeconds(2), "accuse-depot-smtp", "250 OK id=abc");

        message.Statut.ShouldBe(StatutMessage.Envoye);
        message.EnvoyeLe.ShouldBe(Maintenant.AddSeconds(2));
        message.ProchaineTentative.ShouldBeNull();
        message.Preuves.ShouldHaveSingleItem().ShouldBeSameAs(preuve);
        preuve.Empreinte.ShouldBe(message.EmpreinteContenu());
        preuve.Empreinte.Length.ShouldBe(64);
        Should.Throw<DomainException>(() => message.EnregistrerEnvoi(Maintenant, "x", "y"));
    }

    private static Message Convocation(Guid convocationId) =>
        Message.Creer(new NouveauMessage(Message.NouvelIdentifiant(), TypeMessage.ConvocationRendezVous, Canal.Email, false, TypeDestinataire.Personne,
            Guid.CreateVersion7(), Language.Fr, "rendez-vous", Guid.CreateVersion7(), "rdv-1", Gabarits.Generique(Canal.Email, Language.Fr, "https://exemple.test/m/1"),
            convocationId), Maintenant);

    [Fact]
    public void La_creation_et_un_echec_temporaire_ne_levent_aucun_evenement()
    {
        var message = Convocation(Guid.CreateVersion7());
        message.DomainEvents.ShouldBeEmpty();

        message.EnregistrerEchec(Maintenant, "smtp-indisponible", definitif: false, Politique);

        message.Statut.ShouldBe(StatutMessage.EnEchec);
        message.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Un_envoi_reussi_leve_un_evenement_avec_la_reference_d_origine()
    {
        var convocationId = Guid.CreateVersion7();
        var message = Convocation(convocationId);
        message.ReferenceOrigineId.ShouldBe(convocationId);

        message.EnregistrerEnvoi(Maintenant.AddSeconds(2), "accuse-depot-smtp", "250 OK");

        var evenement = message.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<MessageEnvoyeDomaine>();
        evenement.MessageId.ShouldBe(message.Id);
        evenement.ReferenceOrigineId.ShouldBe(convocationId);
        evenement.Type.ShouldBe(TypeMessage.ConvocationRendezVous);
        evenement.Canal.ShouldBe(Canal.Email);
        evenement.OccurredAt.ShouldBe(Maintenant.AddSeconds(2));
    }

    [Fact]
    public void L_abandon_definitif_leve_un_evenement_une_seule_fois()
    {
        var convocationId = Guid.CreateVersion7();
        var message = Convocation(convocationId);

        for (var i = 0; i < Politique.TentativesMaximales; i++)
        {
            message.EnregistrerEchec(Maintenant, "smtp-indisponible", definitif: false, Politique);
        }

        var evenement = message.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<MessageAbandonneDomaine>();
        evenement.ReferenceOrigineId.ShouldBe(convocationId);
        evenement.CodeErreur.ShouldBe("smtp-indisponible");
        Should.Throw<DomainException>(() => message.EnregistrerEchec(Maintenant, "x", definitif: true, Politique));
        message.DomainEvents.Count.ShouldBe(1);
    }

    [Fact]
    public void Une_erreur_definitive_leve_l_evenement_d_abandon_des_la_premiere_tentative()
    {
        var message = Convocation(Guid.CreateVersion7());

        message.EnregistrerEchec(Maintenant, "adresse-email-invalide", definitif: true, Politique);

        message.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<MessageAbandonneDomaine>().CodeErreur.ShouldBe("adresse-email-invalide");
    }

    [Fact]
    public void Un_message_sans_reference_d_origine_leve_quand_meme_ses_evenements()
    {
        var message = Nouveau();

        message.EnregistrerEnvoi(Maintenant, "t", "r");

        message.ReferenceOrigineId.ShouldBeNull();
        message.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<MessageEnvoyeDomaine>().ReferenceOrigineId.ShouldBeNull();
    }

    [Fact]
    public void L_annulation_ne_leve_aucun_evenement()
    {
        var message = Convocation(Guid.CreateVersion7());

        message.Annuler("rendez-vous-annule");

        message.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Un_echec_programme_une_reprise_aux_delais_croissants()
    {
        var message = Nouveau();
        var instant = Maintenant;

        for (var tentative = 1; tentative <= Politique.Delais.Count; tentative++)
        {
            message.EnregistrerEchec(instant, "smtp-indisponible", definitif: false, Politique);

            message.Statut.ShouldBe(StatutMessage.EnEchec);
            message.Tentatives.ShouldBe(tentative);
            message.ProchaineTentative.ShouldBe(instant + Politique.Delais[tentative - 1]);
            message.EstEchu(instant).ShouldBeFalse();
            message.EstEchu(message.ProchaineTentative!.Value).ShouldBeTrue();
            instant = message.ProchaineTentative.Value;
        }
    }

    [Fact]
    public void Les_reprises_epuisees_abandonnent_le_message()
    {
        var message = Nouveau();

        for (var i = 0; i < Politique.TentativesMaximales; i++)
        {
            message.EnregistrerEchec(Maintenant, "smtp-indisponible", definitif: false, Politique);
        }

        message.Statut.ShouldBe(StatutMessage.Abandonne);
        message.ProchaineTentative.ShouldBeNull();
        message.Tentatives.ShouldBe(Politique.TentativesMaximales);
        message.DerniereErreur.ShouldBe("smtp-indisponible");
    }

    [Fact]
    public void Une_erreur_definitive_abandonne_immediatement()
    {
        var message = Nouveau();

        message.EnregistrerEchec(Maintenant, "adresse-email-invalide", definitif: true, Politique);

        message.Statut.ShouldBe(StatutMessage.Abandonne);
        message.Tentatives.ShouldBe(1);
    }

    [Fact]
    public void Un_message_abandonne_peut_etre_relance_avec_un_nouveau_cycle()
    {
        var message = Nouveau();
        message.EnregistrerEchec(Maintenant, "canal-non-raccorde", definitif: true, Politique);

        message.Relancer(Maintenant.AddHours(1));

        message.Statut.ShouldBe(StatutMessage.EnAttente);
        message.Tentatives.ShouldBe(0);
        message.DerniereErreur.ShouldBeNull();
        message.EstEchu(Maintenant.AddHours(1)).ShouldBeTrue();
        Should.Throw<DomainException>(() => Nouveau().Relancer(Maintenant));
    }

    [Fact]
    public void Seul_un_message_pas_encore_parti_peut_etre_annule()
    {
        var enAttente = Nouveau();
        enAttente.Annuler("rendez-vous-annule");
        enAttente.Statut.ShouldBe(StatutMessage.Annule);
        enAttente.EstEchu(Maintenant).ShouldBeFalse();

        var envoye = Nouveau(cle: "cle-2");
        envoye.EnregistrerEnvoi(Maintenant, "t", "r");
        Should.Throw<DomainException>(() => envoye.Annuler("x"));
        Should.Throw<DomainException>(() => enAttente.EnregistrerEnvoi(Maintenant, "t", "r"));
    }

    [Fact]
    public void Les_champs_obligatoires_sont_controles()
    {
        Should.Throw<DomainException>(() => Message.Creer(new NouveauMessage(Message.NouvelIdentifiant(), TypeMessage.Manuel, Canal.Portail, false, TypeDestinataire.Personne,
            Guid.Empty, Language.Fr, "x", Guid.CreateVersion7(), "k", new ContenuMessage("s", "c", false)), Maintenant));
        Should.Throw<DomainException>(() => Nouveau(cle: " "));
        Should.Throw<DomainException>(() => Nouveau(Canal.Portail, contenu: new ContenuMessage(" ", "c", false)));
    }

    [Fact]
    public void La_politique_de_reprise_refuse_un_delai_hors_plage()
    {
        Should.Throw<DomainException>(() => Politique.DelaiApres(0));
        Should.Throw<DomainException>(() => Politique.DelaiApres(Politique.TentativesMaximales));
    }
}
