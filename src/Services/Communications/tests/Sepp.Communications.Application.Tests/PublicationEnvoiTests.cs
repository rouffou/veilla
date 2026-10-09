using Sepp.BuildingBlocks.Domain;
using Sepp.Communications.Application.Evenements;
using Sepp.Communications.Domain.Messages;
using Sepp.Contracts.Communications;
using Sepp.Contracts.Examens;
using Sepp.Contracts.Planification;

using Shouldly;

namespace Sepp.Communications.Application.Tests;

/// <summary>
/// DOC-05, SAN-10, ARC-33 : l'envoi effectif et l'abandon d'une convocation sont annoncés (<c>message-envoye</c>,
/// <c>message-abandonne</c>), une seule fois ; seule <c>convocation-emise</c> crée une convocation.
/// </summary>
public class PublicationEnvoiTests
{
    private static readonly Guid Personne = Guid.CreateVersion7();
    private static readonly Guid Affilie = Guid.CreateVersion7();
    private static readonly DateTimeOffset Debut = new(2026, 10, 12, 7, 30, 0, TimeSpan.Zero);
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Contexte _c = new();

    private static ConvocationEmise Convocation(Guid? convocationId = null, string canal = "Email") =>
        new(convocationId ?? Guid.CreateVersion7(), Guid.CreateVersion7(), Personne, Affilie, Guid.CreateVersion7(), TypesExamen.ExamenReprise, Debut,
            canal, false, "Convocation", null);

    private Task Emettre(ConvocationEmise e) => new ConvocationEmiseHandler(_c.Createur, _c.Store).HandleAsync(e, _ct);

    [Fact]
    public async Task Un_rendez_vous_planifie_sans_convocation_emise_ne_cree_aucun_message()
    {
        _c.Annuaire.Personne(Personne);

        // Le rendez-vous peut ne pas être convoqué (réservation sans convocation) : Communications n'a rien à envoyer.
        _c.Store.Messages.ShouldBeEmpty();
        (await _c.Expediteur.ExpedierEchusAsync(_ct)).ShouldBe(0);
        _c.Store.Published.ShouldBeEmpty();
        typeof(ConvocationEmiseHandler).Assembly.GetTypes().Where(t => t.Name == "RendezVousPlanifieHandler").ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_envoi_reussi_publie_message_envoye_avec_la_reference_de_la_convocation()
    {
        _c.Annuaire.Personne(Personne);
        var convocationId = Guid.CreateVersion7();
        await Emettre(Convocation(convocationId));

        await _c.Expediteur.ExpedierEchusAsync(_ct);

        var message = _c.Store.Messages.ShouldHaveSingleItem();
        message.ReferenceOrigineId.ShouldBe(convocationId);
        var evenement = _c.Store.Published.OfType<MessageEnvoye>().ShouldHaveSingleItem();
        evenement.MessageId.ShouldBe(message.Id);
        evenement.ReferenceOrigineId.ShouldBe(convocationId);
        evenement.TypeMessage.ShouldBe("ConvocationRendezVous");
        evenement.Canal.ShouldBe("Email");
        evenement.Recommande.ShouldBeFalse();
        evenement.ObjetType.ShouldBe("rendez-vous");
        evenement.EnvoyeLe.ShouldBe(message.EnvoyeLe!.Value);
    }

    [Fact]
    public async Task Un_message_envoye_n_est_pas_publie_une_seconde_fois()
    {
        _c.Annuaire.Personne(Personne);
        await Emettre(Convocation());

        await _c.Expediteur.ExpedierEchusAsync(_ct);
        await _c.Expediteur.ExpedierEchusAsync(_ct);

        _c.Store.Published.OfType<MessageEnvoye>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Un_echec_temporaire_ne_publie_rien_puis_l_envoi_de_la_reprise_est_publie()
    {
        _c.Annuaire.Personne(Personne);
        await Emettre(Convocation());
        _c.Canaux[Canal.Email].Pannes.Enqueue(new ErreurEnvoiException("smtp-indisponible", definitive: false));

        await _c.Expediteur.ExpedierEchusAsync(_ct);
        _c.Store.Published.ShouldBeEmpty();

        _c.Horloge.Avancer(TimeSpan.FromMinutes(1));
        await _c.Expediteur.ExpedierEchusAsync(_ct);
        _c.Store.Published.OfType<MessageEnvoye>().ShouldHaveSingleItem();
        _c.Store.Published.OfType<MessageAbandonne>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_abandon_apres_epuisement_des_reprises_publie_message_abandonne_une_seule_fois()
    {
        _c.Annuaire.Personne(Personne);
        var convocationId = Guid.CreateVersion7();
        await Emettre(Convocation(convocationId));

        for (var i = 0; i < PolitiqueReprise.Defaut.TentativesMaximales + 2; i++)
        {
            _c.Canaux[Canal.Email].Pannes.Enqueue(new ErreurEnvoiException("smtp-indisponible", definitive: false));
            await _c.Expediteur.ExpedierEchusAsync(_ct);
            _c.Horloge.Avancer(TimeSpan.FromDays(1));
        }

        _c.Store.Messages.ShouldHaveSingleItem().Statut.ShouldBe(StatutMessage.Abandonne);
        var evenement = _c.Store.Published.OfType<MessageAbandonne>().ShouldHaveSingleItem();
        evenement.ReferenceOrigineId.ShouldBe(convocationId);
        evenement.CodeErreur.ShouldBe("smtp-indisponible");
        _c.Store.Published.OfType<MessageEnvoye>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_erreur_definitive_publie_immediatement_message_abandonne()
    {
        _c.Annuaire.Personne(Personne);
        await Emettre(Convocation());
        _c.Canaux[Canal.Email].Pannes.Enqueue(new ErreurEnvoiException("adresse-email-invalide", definitive: true));

        await _c.Expediteur.ExpedierEchusAsync(_ct);

        _c.Store.Published.OfType<MessageAbandonne>().ShouldHaveSingleItem().CodeErreur.ShouldBe("adresse-email-invalide");
    }

    [Fact]
    public async Task Une_convocation_abandonnee_des_sa_creation_est_annoncee_pour_ne_pas_rester_ignoree()
    {
        // Destinataire inconnu : le message est créé directement à l'état abandonné.
        var convocationId = Guid.CreateVersion7();

        await Emettre(Convocation(convocationId));

        var evenement = _c.Store.Published.OfType<MessageAbandonne>().ShouldHaveSingleItem();
        evenement.ReferenceOrigineId.ShouldBe(convocationId);
        evenement.CodeErreur.ShouldBe("destinataire-inconnu");
    }

    [Fact]
    public async Task Une_convocation_recommandee_publie_un_evenement_par_message_avec_la_meme_reference()
    {
        _c.Annuaire.Personne(Personne);
        var convocationId = Guid.CreateVersion7();
        await Emettre(Convocation(convocationId) with { Recommande = true });

        await _c.Expediteur.ExpedierEchusAsync(_ct);

        var envoyes = _c.Store.Published.OfType<MessageEnvoye>().ToList();
        envoyes.Count.ShouldBe(2);
        envoyes.ShouldAllBe(e => e.ReferenceOrigineId == convocationId);
        envoyes.Select(e => e.Recommande).Order().ShouldBe([false, true]);
    }

    [Fact]
    public async Task Une_notification_de_document_publie_aussi_message_envoye_sans_reference_d_origine()
    {
        _c.Annuaire.Personne(Personne);
        await new DocumentPublieHandler(_c.Createur, _c.Store).HandleAsync(
            new Contracts.Documents.DocumentPublie(Guid.CreateVersion7(), "standard", "personne", Personne, "COURRIER"), _ct);

        await _c.Expediteur.ExpedierEchusAsync(_ct);

        var evenement = _c.Store.Published.OfType<MessageEnvoye>().ShouldHaveSingleItem();
        evenement.ReferenceOrigineId.ShouldBeNull();
        evenement.TypeMessage.ShouldBe("NotificationDocument");
    }
}
