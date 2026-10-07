using Sepp.BuildingBlocks.Domain;
using Sepp.Communications.Application.Evenements;
using Sepp.Communications.Domain.Messages;
using Sepp.Contracts.Audit;
using Sepp.Contracts.Documents;
using Sepp.Contracts.Planification;

using Shouldly;

namespace Sepp.Communications.Application.Tests;

/// <summary>DOC-03 à DOC-05, SAN-10, SAN-11, SAN-13 : messages déclenchés par les événements des autres services.</summary>
public class EvenementsTests
{
    private static readonly Guid Personne = Guid.CreateVersion7();
    private static readonly Guid Affilie = Guid.CreateVersion7();
    private static readonly DateTimeOffset Debut = new(2026, 10, 12, 7, 30, 0, TimeSpan.Zero);
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Contexte _c = new();

    private DocumentPublieHandler DocumentPublie() => new(_c.Createur, _c.Store);

    private ConvocationEmiseHandler Convocation() => new(_c.Createur, _c.Store);

    private RendezVousPlanifieHandler RendezVousPlanifie() => new(_c.Createur, _c.Store);

    private RendezVousAnnuleHandler RendezVousAnnule() => new(_c.Createur, _c.Store, _c.Store);

    private RappelRendezVousDuHandler Rappel() => new(_c.Createur, _c.Store, _c.Store);

    private static ConvocationEmise ConvocationDe(Guid rendezVous, string canal = "Email", bool recommande = false, string type = "Convocation") =>
        new(Guid.CreateVersion7(), rendezVous, Personne, Affilie, Guid.CreateVersion7(), "VISITE_PERIODIQUE", Debut, canal, recommande, type, null);

    // ---- Un e-mail ne contient jamais de donnée de santé ---------------------------------------------------------------

    [Fact]
    public async Task Un_email_ne_contient_jamais_de_donnee_de_sante_quel_que_soit_l_evenement()
    {
        _c.Annuaire.Personne(Personne, prefere: Canal.Email);
        _c.Annuaire.Affilie(Affilie);
        var rendezVous = Guid.CreateVersion7();

        // Contrats « sensibles » : code de modèle médical, zone médicale, type d'acte médical, motif d'annulation.
        await DocumentPublie().HandleAsync(new DocumentPublie(Guid.CreateVersion7(), "medicale", "personne", Personne, "SANTE.EVALUATION.TRAVAILLEUR"), _ct);
        await DocumentPublie().HandleAsync(new DocumentPublie(Guid.CreateVersion7(), "psychosociale", "personne", Personne, "PSY.RAPPORT.INTERVENTION"), _ct);
        await Convocation().HandleAsync(ConvocationDe(rendezVous, "Email") with { TypeActe = "EXAMEN_PSYCHOSOCIAL" }, _ct);
        await Rappel().HandleAsync(new RappelRendezVousDu(Guid.CreateVersion7(), Personne, Affilie, Guid.CreateVersion7(), Debut, "Email", 1), _ct);
        await RendezVousAnnule().HandleAsync(new RendezVousAnnule(Guid.CreateVersion7(), Personne, "ABSENCE_MEDECIN_DU_TRAVAIL"), _ct);
        await _c.Expediteur.ExpedierEchusAsync(_ct);

        var emails = _c.Canaux[Canal.Email].Envoyes;
        emails.Count.ShouldBe(5);
        foreach (var email in emails)
        {
            var texte = $"{email.Sujet}\n{email.Corps}";
            foreach (var interdit in new[]
                     {
                         "SANTE", "EVALUATION", "medicale", "psychosociale", "PSY.", "EXAMEN", "VISITE", "ABSENCE", "MEDECIN", "santé", "médic", "examen", "psycho",
                         "rendez-vous", "décision", "document",
                     })
            {
                texte.ShouldNotContain(interdit, Case.Insensitive);
            }

            texte.ShouldContain("https://travailleur.exemple.test/messages/");
            var horsLien = System.Text.RegularExpressions.Regex.Replace(texte, @"https://\S+", string.Empty);
            horsLien.ShouldNotContain("2026");
            horsLien.ShouldNotContain("09:30");
        }

        _c.Store.Messages.Where(m => m.Canal == Canal.Email).ShouldAllBe(m => m.ContenuGenerique);
    }

    // ---- Documents ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Un_document_publie_notifie_le_destinataire_sans_envoyer_le_document()
    {
        _c.Annuaire.Personne(Personne);
        var evenement = new DocumentPublie(Guid.CreateVersion7(), "medicale", "personne", Personne, "SANTE.EVALUATION.TRAVAILLEUR");

        await DocumentPublie().HandleAsync(evenement, _ct);
        await _c.Expediteur.ExpedierEchusAsync(_ct);

        var message = _c.Store.Messages.ShouldHaveSingleItem();
        message.Type.ShouldBe(TypeMessage.NotificationDocument);
        message.ObjetId.ShouldBe(evenement.DocumentId);
        message.Statut.ShouldBe(StatutMessage.Envoye);
        message.Preuves.ShouldHaveSingleItem();
        message.Corps.ShouldContain(message.Id.ToString());
    }

    [Fact]
    public async Task Le_rejeu_d_un_document_publie_ne_cree_qu_un_seul_message()
    {
        _c.Annuaire.Personne(Personne);
        var evenement = new DocumentPublie(Guid.CreateVersion7(), "standard", "personne", Personne, "COURRIER");

        await DocumentPublie().HandleAsync(evenement, _ct);
        await DocumentPublie().HandleAsync(evenement, _ct);
        await _c.Expediteur.ExpedierEchusAsync(_ct);
        await _c.Expediteur.ExpedierEchusAsync(_ct);

        _c.Store.Messages.Count.ShouldBe(1);
        _c.Envoyes.Count().ShouldBe(1);
    }

    [Fact]
    public async Task L_exemplaire_du_dossier_n_est_jamais_notifie()
    {
        await DocumentPublie().HandleAsync(new DocumentPublie(Guid.CreateVersion7(), "medicale", "dossier", Personne, "SANTE.EVALUATION.DOSSIER"), _ct);

        _c.Store.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_document_destine_a_l_affilie_va_vers_l_eBox_entreprise_avec_le_lien_du_portail_employeur()
    {
        _c.Annuaire.Affilie(Affilie);

        await DocumentPublie().HandleAsync(new DocumentPublie(Guid.CreateVersion7(), "standard", "affilie", Affilie, "SANTE.EVALUATION.EMPLOYEUR"), _ct);
        await _c.Expediteur.ExpedierEchusAsync(_ct);

        var envoi = _c.Canaux[Canal.EBoxEntreprise].Envoyes.ShouldHaveSingleItem();
        envoi.Corps.ShouldContain("https://employeur.exemple.test/messages/");
        envoi.Langue.ShouldBe(Language.Nl);
        envoi.Sujet.ShouldBe("Nieuw document beschikbaar");
    }

    // ---- Rendez-vous ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Une_convocation_et_l_evenement_de_planification_n_envoient_qu_une_fois()
    {
        _c.Annuaire.Personne(Personne);
        var rendezVous = Guid.CreateVersion7();

        await RendezVousPlanifie().HandleAsync(new RendezVousPlanifie(rendezVous, Personne, Affilie, Debut, []), _ct);
        await Convocation().HandleAsync(ConvocationDe(rendezVous, "Email"), _ct);
        await Convocation().HandleAsync(ConvocationDe(rendezVous, "Email"), _ct);
        await _c.Expediteur.ExpedierEchusAsync(_ct);

        _c.Store.Messages.ShouldHaveSingleItem().Type.ShouldBe(TypeMessage.ConvocationRendezVous);
        _c.Envoyes.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Une_reconvocation_est_un_nouveau_message()
    {
        _c.Annuaire.Personne(Personne);
        var rendezVous = Guid.CreateVersion7();

        await Convocation().HandleAsync(ConvocationDe(rendezVous), _ct);
        await Convocation().HandleAsync(ConvocationDe(rendezVous, type: "Reconvocation"), _ct);

        _c.Store.Messages.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Le_canal_indique_par_la_planification_est_utilise_s_il_est_joignable()
    {
        _c.Annuaire.Personne(Personne, prefere: Canal.Email);

        await Convocation().HandleAsync(ConvocationDe(Guid.CreateVersion7(), "Sms"), _ct);

        _c.Store.Messages.ShouldHaveSingleItem().Canal.ShouldBe(Canal.Sms);
    }

    [Fact]
    public async Task A_defaut_de_telephone_la_preference_du_travailleur_est_retenue()
    {
        _c.Annuaire.Personne(Personne, prefere: Canal.Courrier, telephone: null);

        await Convocation().HandleAsync(ConvocationDe(Guid.CreateVersion7(), "Sms"), _ct);

        _c.Store.Messages.ShouldHaveSingleItem().Canal.ShouldBe(Canal.Courrier);
    }

    [Fact]
    public async Task Une_convocation_recommandee_ajoute_un_recommande_electronique()
    {
        _c.Annuaire.Personne(Personne);

        await Convocation().HandleAsync(ConvocationDe(Guid.CreateVersion7(), "Email", recommande: true), _ct);
        await _c.Expediteur.ExpedierEchusAsync(_ct);

        _c.Store.Messages.Select(m => (m.Canal, m.Recommande)).Order().ShouldBe([(Canal.Email, false), (Canal.RecommandeElectronique, true)]);
        var recommande = _c.Canaux[Canal.RecommandeElectronique].Envoyes.ShouldHaveSingleItem();
        recommande.Corps.ShouldContain("lundi 12 octobre 2026 à 09:30");
    }

    [Fact]
    public async Task Sans_recommande_electronique_possible_le_courrier_recommande_est_utilise()
    {
        _c.Annuaire.Personne(Personne, email: null);

        await Convocation().HandleAsync(ConvocationDe(Guid.CreateVersion7(), "Sms", recommande: true), _ct);
        await _c.Expediteur.ExpedierEchusAsync(_ct);

        var courrier = _c.Canaux[Canal.Courrier].Envoyes.ShouldHaveSingleItem();
        courrier.Recommande.ShouldBeTrue();
    }

    [Fact]
    public async Task Un_recommande_impossible_laisse_un_message_abandonne_visible()
    {
        _c.Annuaire.Personne(Personne, email: null, adresse: false);

        await Convocation().HandleAsync(ConvocationDe(Guid.CreateVersion7(), "Sms", recommande: true), _ct);

        var echec = _c.Store.Messages.Single(m => m.Recommande);
        echec.Statut.ShouldBe(StatutMessage.Abandonne);
        echec.DerniereErreur.ShouldBe("recommande-impossible");
    }

    [Fact]
    public async Task Un_destinataire_inconnu_laisse_un_message_abandonne_dans_le_journal()
    {
        await Convocation().HandleAsync(ConvocationDe(Guid.CreateVersion7()), _ct);

        var message = _c.Store.Messages.ShouldHaveSingleItem();
        message.Statut.ShouldBe(StatutMessage.Abandonne);
        message.DerniereErreur.ShouldBe("destinataire-inconnu");
        _c.Envoyes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_travailleur_injoignable_laisse_un_message_abandonne()
    {
        _c.Annuaire.Destinataires[(TypeDestinataire.Personne, Personne)] = new Destinataire(TypeDestinataire.Personne, Personne, Language.Fr, null, null, null, null, null, null, false, null);

        await Convocation().HandleAsync(ConvocationDe(Guid.CreateVersion7()), _ct);

        _c.Store.Messages.ShouldHaveSingleItem().DerniereErreur.ShouldBe("aucun-canal-joignable");
    }

    [Fact]
    public async Task L_annulation_d_un_rendez_vous_annule_les_messages_pas_encore_partis_et_previent_le_travailleur()
    {
        _c.Annuaire.Personne(Personne);
        var rendezVous = Guid.CreateVersion7();
        await Convocation().HandleAsync(ConvocationDe(rendezVous), _ct);
        await Rappel().HandleAsync(new RappelRendezVousDu(rendezVous, Personne, Affilie, Guid.CreateVersion7(), Debut, "Email", 1), _ct);

        await RendezVousAnnule().HandleAsync(new RendezVousAnnule(rendezVous, Personne, "ABSENCE"), _ct);

        _c.Store.Messages.Where(m => m.Type is TypeMessage.ConvocationRendezVous or TypeMessage.RappelRendezVous).ShouldAllBe(m => m.Statut == StatutMessage.Annule);
        _c.Store.Messages.Single(m => m.Type == TypeMessage.AnnulationRendezVous).Statut.ShouldBe(StatutMessage.EnAttente);
        await _c.Expediteur.ExpedierEchusAsync(_ct);
        _c.Envoyes.Count().ShouldBe(1);
    }

    [Fact]
    public async Task L_annulation_ne_touche_pas_une_convocation_deja_envoyee()
    {
        _c.Annuaire.Personne(Personne);
        var rendezVous = Guid.CreateVersion7();
        await Convocation().HandleAsync(ConvocationDe(rendezVous), _ct);
        await _c.Expediteur.ExpedierEchusAsync(_ct);

        await RendezVousAnnule().HandleAsync(new RendezVousAnnule(rendezVous, Personne, "ABSENCE"), _ct);

        _c.Store.Messages.Single(m => m.Type == TypeMessage.ConvocationRendezVous).Statut.ShouldBe(StatutMessage.Envoye);
    }

    [Fact]
    public async Task Un_rappel_tardif_apres_annulation_n_est_pas_envoye()
    {
        _c.Annuaire.Personne(Personne);
        var rendezVous = Guid.CreateVersion7();
        await RendezVousAnnule().HandleAsync(new RendezVousAnnule(rendezVous, Personne, "ABSENCE"), _ct);

        await Rappel().HandleAsync(new RappelRendezVousDu(rendezVous, Personne, Affilie, Guid.CreateVersion7(), Debut, "Email", 2), _ct);

        _c.Store.Messages.ShouldHaveSingleItem().Type.ShouldBe(TypeMessage.AnnulationRendezVous);
    }

    [Fact]
    public async Task Chaque_rappel_est_distinct_et_idempotent()
    {
        _c.Annuaire.Personne(Personne);
        var rendezVous = Guid.CreateVersion7();

        await Rappel().HandleAsync(new RappelRendezVousDu(rendezVous, Personne, Affilie, Guid.CreateVersion7(), Debut, "Email", 1), _ct);
        await Rappel().HandleAsync(new RappelRendezVousDu(rendezVous, Personne, Affilie, Guid.CreateVersion7(), Debut, "Email", 1), _ct);
        await Rappel().HandleAsync(new RappelRendezVousDu(rendezVous, Personne, Affilie, Guid.CreateVersion7(), Debut, "Email", 2), _ct);

        _c.Store.Messages.Count.ShouldBe(2);
    }

    // ---- Bris de glace ------------------------------------------------------------------------------------------------

    private static Destinataire Dirigeant(Guid id) =>
        new(TypeDestinataire.Interne, id, Language.Fr, "Dirigeant", "dirigeant@exemple.test", null, null, null, null, true, Canal.Email);

    [Fact]
    public async Task Un_bris_de_glace_alerte_les_dirigeants_par_une_notification_generique()
    {
        var dirigeant = Guid.CreateVersion7();
        _c.Annuaire.Dirigeants.Add(Dirigeant(dirigeant));
        var evenement = new BrisDeGlaceSignale(Guid.CreateVersion7(), "medicale", "surveillance-medicale", "infirmier-42", "dossier-sante", Guid.CreateVersion7());

        await new BrisDeGlaceSignaleHandler(_c.Createur, _c.Annuaire, _c.Store).HandleAsync(evenement, _ct);
        await _c.Expediteur.ExpedierEchusAsync(_ct);

        var email = _c.Canaux[Canal.Email].Envoyes.ShouldHaveSingleItem();
        email.Destinataire.Id.ShouldBe(dirigeant);
        var texte = $"{email.Sujet}\n{email.Corps}";
        texte.ShouldContain("https://interne.exemple.test/messages/");
        foreach (var interdit in new[] { "medicale", "infirmier-42", "dossier-sante", "surveillance", "bris", "zone" })
        {
            texte.ShouldNotContain(interdit, Case.Insensitive);
        }
    }

    [Fact]
    public async Task Le_bris_de_glace_rejoue_n_alerte_qu_une_fois()
    {
        _c.Annuaire.Dirigeants.Add(Dirigeant(Guid.CreateVersion7()));
        var evenement = new BrisDeGlaceSignale(Guid.CreateVersion7(), "psychosociale", "psychosocial", "u", "dossier", Guid.CreateVersion7());
        var handler = new BrisDeGlaceSignaleHandler(_c.Createur, _c.Annuaire, _c.Store);

        await handler.HandleAsync(evenement, _ct);
        await handler.HandleAsync(evenement, _ct);

        _c.Store.Messages.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Sans_dirigeant_a_alerter_l_evenement_echoue_pour_etre_rejoue()
    {
        var evenement = new BrisDeGlaceSignale(Guid.CreateVersion7(), "medicale", "s", "u", "o", Guid.CreateVersion7());

        await Should.ThrowAsync<InvalidOperationException>(() => new BrisDeGlaceSignaleHandler(_c.Createur, _c.Annuaire, _c.Store).HandleAsync(evenement, _ct));
    }
}
