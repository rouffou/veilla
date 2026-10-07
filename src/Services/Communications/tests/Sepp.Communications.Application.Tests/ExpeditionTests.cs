using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Communications.Application.Messages;
using Sepp.Communications.Domain.Messages;

using Shouldly;

namespace Sepp.Communications.Application.Tests;

/// <summary>DOC-03, DOC-05 : envoi, preuves, reprises, statuts, relance manuelle et envoi manuel.</summary>
public class ExpeditionTests
{
    private static readonly Guid Personne = Guid.CreateVersion7();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Contexte _c = new(Roles.GestionnaireDossiers);

    private async Task<Message> Creer(Canal canal = Canal.Email)
    {
        _c.Annuaire.Personne(Personne, prefere: canal);
        var crees = await _c.Createur.CreerAsync(new Expedition.DemandeMessage(TypeMessage.NotificationDocument, TypeDestinataire.Personne, Personne,
            "document", Guid.CreateVersion7(), $"cle-{Guid.CreateVersion7()}"), _ct);
        return crees.Single();
    }

    [Fact]
    public async Task Un_envoi_reussi_conserve_la_preuve_du_canal_et_l_empreinte_du_contenu()
    {
        var message = await Creer();

        (await _c.Expediteur.ExpedierEchusAsync(_ct)).ShouldBe(1);

        message.Statut.ShouldBe(StatutMessage.Envoye);
        var preuve = message.Preuves.ShouldHaveSingleItem();
        preuve.Type.ShouldBe("accuse-Email");
        preuve.Reference.ShouldStartWith("ref-");
        preuve.Empreinte.ShouldBe(message.EmpreinteContenu());
        message.EnvoyeLe.ShouldBe(_c.Horloge.GetUtcNow());
    }

    [Fact]
    public async Task Les_coordonnees_sont_lues_a_l_envoi_et_ne_sont_pas_conservees_dans_le_message()
    {
        var message = await Creer();

        await _c.Expediteur.ExpedierEchusAsync(_ct);

        _c.Canaux[Canal.Email].Envoyes.Single().Destinataire.Email.ShouldBe("marie@exemple.test");
        foreach (var propriete in typeof(Message).GetProperties())
        {
            (propriete.GetValue(message)?.ToString() ?? string.Empty).ShouldNotContain("marie@exemple.test");
        }
    }

    [Fact]
    public async Task Un_echec_temporaire_programme_une_reprise_qui_aboutit()
    {
        var message = await Creer();
        _c.Canaux[Canal.Email].Pannes.Enqueue(new ErreurEnvoiException("smtp-indisponible", definitive: false));

        await _c.Expediteur.ExpedierEchusAsync(_ct);

        message.Statut.ShouldBe(StatutMessage.EnEchec);
        message.DerniereErreur.ShouldBe("smtp-indisponible");
        message.ProchaineTentative.ShouldBe(_c.Horloge.GetUtcNow() + TimeSpan.FromMinutes(1));

        // Pas de nouvel envoi avant l'échéance de la reprise.
        (await _c.Expediteur.ExpedierEchusAsync(_ct)).ShouldBe(0);
        _c.Horloge.Avancer(TimeSpan.FromMinutes(1));
        (await _c.Expediteur.ExpedierEchusAsync(_ct)).ShouldBe(1);

        message.Statut.ShouldBe(StatutMessage.Envoye);
        message.Tentatives.ShouldBe(2);
        message.DerniereErreur.ShouldBeNull();
    }

    [Fact]
    public async Task Les_reprises_epuisees_abandonnent_puis_la_relance_manuelle_renvoie()
    {
        var message = await Creer();
        var politique = PolitiqueReprise.Defaut;
        for (var i = 0; i < politique.TentativesMaximales; i++)
        {
            _c.Canaux[Canal.Email].Pannes.Enqueue(new ErreurEnvoiException("smtp-indisponible", definitive: false));
        }

        for (var i = 0; i < politique.TentativesMaximales; i++)
        {
            await _c.Expediteur.ExpedierEchusAsync(_ct);
            _c.Horloge.Avancer(TimeSpan.FromHours(13));
        }

        message.Statut.ShouldBe(StatutMessage.Abandonne);
        (await _c.Expediteur.ExpedierEchusAsync(_ct)).ShouldBe(0);

        var relance = new RelancerMessageHandler(_c.Store, _c.Store, _c.Utilisateur, _c.Horloge);
        (await relance.HandleAsync(new RelancerMessage(message.Id), _ct)).IsSuccess.ShouldBeTrue();
        await _c.Expediteur.ExpedierEchusAsync(_ct);

        message.Statut.ShouldBe(StatutMessage.Envoye);
    }

    [Fact]
    public async Task Une_erreur_definitive_abandonne_sans_reprise()
    {
        var message = await Creer();
        _c.Canaux[Canal.Email].Pannes.Enqueue(new ErreurEnvoiException("adresse-email-invalide", definitive: true));

        await _c.Expediteur.ExpedierEchusAsync(_ct);

        message.Statut.ShouldBe(StatutMessage.Abandonne);
        message.Tentatives.ShouldBe(1);
    }

    [Fact]
    public async Task Une_erreur_technique_inattendue_est_une_reprise_sans_coordonnee_dans_le_journal()
    {
        var message = await Creer();
        _c.Annuaire.Destinataires[(TypeDestinataire.Personne, Personne)] = null;
        _c.Annuaire.Personne(Personne);
        var canal = new CanalQuiPlante();
        var expediteur = new Expedition.ExpediteurMessages(_c.Store, _c.Annuaire, [canal], _c.Store, _c.Horloge);

        await expediteur.ExpedierEchusAsync(_ct);

        message.Statut.ShouldBe(StatutMessage.EnEchec);
        message.DerniereErreur.ShouldBe("erreur-technique:InvalidOperationException");
        message.DerniereErreur!.ShouldNotContain("marie@exemple.test");
    }

    [Fact]
    public async Task Un_destinataire_devenu_inconnu_abandonne_le_message()
    {
        var message = await Creer();
        _c.Annuaire.Destinataires[(TypeDestinataire.Personne, Personne)] = null;

        await _c.Expediteur.ExpedierEchusAsync(_ct);

        message.Statut.ShouldBe(StatutMessage.Abandonne);
        message.DerniereErreur.ShouldBe("destinataire-inconnu");
    }

    [Fact]
    public async Task Un_canal_non_configure_abandonne_le_message()
    {
        var message = await Creer();
        var expediteur = new Expedition.ExpediteurMessages(_c.Store, _c.Annuaire, [], _c.Store, _c.Horloge);

        await expediteur.ExpedierEchusAsync(_ct);

        message.DerniereErreur.ShouldBe("canal-non-configure");
        message.Statut.ShouldBe(StatutMessage.Abandonne);
    }

    [Fact]
    public async Task Un_message_reserve_par_une_autre_instance_n_est_pas_envoye_deux_fois()
    {
        await Creer();
        _c.Store.RefuserReservation = true;

        (await _c.Expediteur.ExpedierEchusAsync(_ct)).ShouldBe(0);

        _c.Envoyes.ShouldBeEmpty();
    }

    // ---- Envoi manuel -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task L_envoi_manuel_par_email_n_envoie_jamais_le_texte_libre()
    {
        _c.Annuaire.Personne(Personne);
        var handler = new EnvoyerMessageHandler(_c.Store, _c.Annuaire, _c.Liens, _c.Store, _c.Utilisateur, _c.Horloge);

        var id = (await handler.HandleAsync(new EnvoyerMessage(TypeDestinataire.Personne, Personne, Canal.Email, false, "Résultat d'analyse", "Vous êtes diabétique."), _ct)).Value;
        await _c.Expediteur.ExpedierEchusAsync(_ct);

        var email = _c.Canaux[Canal.Email].Envoyes.ShouldHaveSingleItem();
        email.MessageId.ShouldBe(id);
        $"{email.Sujet}{email.Corps}".ShouldNotContain("diabétique");
        $"{email.Sujet}{email.Corps}".ShouldNotContain("analyse");
        _c.Store.Messages.Single().ContenuGenerique.ShouldBeTrue();
    }

    [Fact]
    public async Task L_envoi_manuel_sur_un_canal_securise_porte_le_texte_saisi_avec_le_lien()
    {
        _c.Annuaire.Personne(Personne);
        var handler = new EnvoyerMessageHandler(_c.Store, _c.Annuaire, _c.Liens, _c.Store, _c.Utilisateur, _c.Horloge);

        await handler.HandleAsync(new EnvoyerMessage(TypeDestinataire.Personne, Personne, Canal.Portail, false, "Information", "Merci de passer au centre."), _ct);
        await _c.Expediteur.ExpedierEchusAsync(_ct);

        var envoi = _c.Canaux[Canal.Portail].Envoyes.ShouldHaveSingleItem();
        envoi.Sujet.ShouldBe("Information");
        envoi.Corps.ShouldContain("Merci de passer au centre.");
        envoi.Corps.ShouldContain("https://travailleur.exemple.test/messages/");
    }

    [Fact]
    public async Task L_envoi_manuel_est_controle()
    {
        _c.Annuaire.Personne(Personne, telephone: null);
        var handler = new EnvoyerMessageHandler(_c.Store, _c.Annuaire, _c.Liens, _c.Store, _c.Utilisateur, _c.Horloge);

        (await handler.HandleAsync(new EnvoyerMessage(TypeDestinataire.Personne, Personne, Canal.Sms, false, null, null), _ct)).Error!.Code.ShouldBe("message.canal-injoignable");
        (await handler.HandleAsync(new EnvoyerMessage(TypeDestinataire.Personne, Personne, Canal.Portail, false, " ", "x"), _ct)).Error!.Code.ShouldBe("message.contenu");
        (await handler.HandleAsync(new EnvoyerMessage(TypeDestinataire.Personne, Guid.CreateVersion7(), Canal.Portail, false, "s", "c"), _ct)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
        (await handler.HandleAsync(new EnvoyerMessage(TypeDestinataire.Interne, Personne, Canal.Email, false, null, null), _ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await handler.HandleAsync(new EnvoyerMessage(TypeDestinataire.Personne, Personne, Canal.Email, true, null, null), _ct)).Error!.Code.ShouldBe("message.invalide");
        _c.Store.Messages.ShouldBeEmpty();
    }

    // ---- Droits --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Le_journal_est_reserve_aux_internes_habilites()
    {
        var message = await Creer();

        foreach (var role in new[] { Roles.Employeur, Roles.Travailleur, Roles.Sipp, Roles.Direction })
        {
            var utilisateur = new FakeUser(role);
            (await new RechercherMessagesHandler(_c.Store, utilisateur).HandleAsync(new RechercherMessages(null, null, null, null, null, null, null, null), _ct))
                .Error!.Kind.ShouldBe(ErrorKind.Forbidden, role);
            (await new ObtenirMessageHandler(_c.Store, utilisateur).HandleAsync(new ObtenirMessage(message.Id), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden, role);
        }

        var gestionnaire = (await new RechercherMessagesHandler(_c.Store, _c.Utilisateur).HandleAsync(new RechercherMessages(null, Personne, null, null, null, null, 1, 10), _ct)).Value;
        gestionnaire.Total.ShouldBe(1);
        gestionnaire.Messages.Single().Id.ShouldBe(message.Id);
    }

    [Fact]
    public async Task Le_detail_d_un_message_donne_le_corps_et_les_preuves()
    {
        var message = await Creer();
        await _c.Expediteur.ExpedierEchusAsync(_ct);

        var detail = (await new ObtenirMessageHandler(_c.Store, _c.Utilisateur).HandleAsync(new ObtenirMessage(message.Id), _ct)).Value;

        detail.Preuves.ShouldHaveSingleItem();
        detail.Corps.ShouldContain(message.Id.ToString());
        detail.ContenuGenerique.ShouldBeTrue();
        (await new ObtenirMessageHandler(_c.Store, _c.Utilisateur).HandleAsync(new ObtenirMessage(Guid.CreateVersion7()), _ct)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Les_droits_d_envoi_manuel_de_relance_et_d_expedition_sont_distincts()
    {
        var message = await Creer();
        var lecteur = new FakeUser(Roles.Cpmt);
        var admin = new FakeUser(Roles.AdministrateurFonctionnel);

        (await new EnvoyerMessageHandler(_c.Store, _c.Annuaire, _c.Liens, _c.Store, lecteur, _c.Horloge)
            .HandleAsync(new EnvoyerMessage(TypeDestinataire.Personne, Personne, Canal.Portail, false, "s", "c"), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new RelancerMessageHandler(_c.Store, _c.Store, lecteur, _c.Horloge).HandleAsync(new RelancerMessage(message.Id), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new ExpedierMessagesEchusHandler(_c.Expediteur, lecteur).HandleAsync(new ExpedierMessagesEchus(), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);

        (await new ExpedierMessagesEchusHandler(_c.Expediteur, admin).HandleAsync(new ExpedierMessagesEchus(), _ct)).Value.Traites.ShouldBe(1);
        (await new RelancerMessageHandler(_c.Store, _c.Store, admin, _c.Horloge).HandleAsync(new RelancerMessage(message.Id), _ct)).Error!.Kind.ShouldBe(ErrorKind.Conflict);
    }

    private sealed class CanalQuiPlante : ICanalEnvoi
    {
        public Canal Canal => Canal.Email;

        public Task<ResultatEnvoi> EnvoyerAsync(Envoi envoi, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"Échec SMTP pour {envoi.Destinataire.Email}");
    }
}
