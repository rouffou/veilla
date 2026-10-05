using System.Net;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using Sepp.Bff.Employeur.Aval;
using Sepp.Bff.Employeur.Ecrans;

using Shouldly;

namespace Sepp.Bff.Employeur.Tests;

public sealed class EcransEmployeurTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Guid _affilie = Guid.CreateVersion7();
    private readonly IAffiliesApi _affilies = Substitute.For<IAffiliesApi>();
    private readonly IPersonnesApi _personnes = Substitute.For<IPersonnesApi>();
    private readonly IPostesRisquesApi _postesRisques = Substitute.For<IPostesRisquesApi>();

    private EcransEmployeur Ecrans => new(_affilies, _personnes, _postesRisques, new Horloge(new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void La_fiche_ne_garde_que_les_sites_et_contacts_en_vigueur()
    {
        var fiche = EcransEmployeur.Fiche(Jeu.Affilie(_affilie, "Boulangerie Dupont"), Jeu.Aujourdhui);

        fiche.Denomination.ShouldBe("Boulangerie Dupont");
        fiche.Sites.Select(s => s.Nom).ShouldBe(["Atelier"]);
        fiche.Sites[0].UniteEtablissement.ShouldBe("Siège");
        fiche.Contacts.Select(c => c.Nom).ShouldBe(["Alice Martin"]);
    }

    [Fact]
    public void La_recherche_de_travailleurs_ignore_casse_et_accents_et_pagine()
    {
        var travailleurs = new[]
        {
            Jeu.Travailleur("Lefèvre", "Hélène"), Jeu.Travailleur("Dupont", "Jean"), Jeu.Travailleur("Durand", "Éric"),
            Jeu.Travailleur("Martin", "Lea"),
        };

        EcransEmployeur.Paginer(travailleurs, "helene", 1, 20).Elements.Single().Nom.ShouldBe("Lefèvre");
        EcransEmployeur.Paginer(travailleurs, "eric durand", 1, 20).Total.ShouldBe(1);

        var page = EcransEmployeur.Paginer(travailleurs, null, 2, 2);
        page.Total.ShouldBe(4);
        page.Elements.Select(t => t.Nom).ShouldBe(["Lefèvre", "Martin"]);
    }

    [Fact]
    public void La_taille_de_page_est_bornee()
    {
        var page = EcransEmployeur.Paginer([Jeu.Travailleur("A", "B")], null, -3, 10_000);
        page.Page.ShouldBe(1);
        page.Taille.ShouldBe(EcransEmployeur.TailleMaximale);
    }

    [Fact]
    public void Les_postes_sont_enrichis_du_libelle_des_risques_et_marques_exposes()
    {
        var postes = new[]
        {
            Jeu.Poste(_affilie, "Soudeur", "Actif", "BRUIT"),
            Jeu.Poste(_affilie, "Comptable"),
            Jeu.Poste(_affilie, "Ancien cariste", "Archive", "VIBRATIONS"),
        };

        var ecran = EcransEmployeur.Postes(postes, [new RisqueAval("BRUIT", "Physique", "Bruit")]);

        ecran.Select(p => p.Intitule).ShouldBe(["Comptable", "Soudeur", "Ancien cariste"]);
        ecran.Single(p => p.Intitule == "Soudeur").Expose.ShouldBeTrue();
        ecran.Single(p => p.Intitule == "Soudeur").Risques.Single().Libelle.ShouldBe("Bruit");
        ecran.Single(p => p.Intitule == "Ancien cariste").Expose.ShouldBeFalse();
        ecran.Single(p => p.Intitule == "Ancien cariste").Risques.Single().Libelle.ShouldBe("VIBRATIONS");
    }

    [Fact]
    public void La_derniere_version_de_chaque_liste_est_signalee()
    {
        var listes = EcransEmployeur.Listes(
        [
            Jeu.Liste(_affilie, "PosteSecurite", 1), Jeu.Liste(_affilie, "PosteSecurite", 2), Jeu.Liste(_affilie, "TravailNuit", 1),
        ]);

        listes.Select(l => (l.Type, l.Version, l.Derniere)).ShouldBe(
            [("PosteSecurite", 2, true), ("PosteSecurite", 1, false), ("TravailNuit", 1, true)]);
    }

    [Fact]
    public async Task Le_tableau_de_bord_ne_presente_que_des_compteurs_reels()
    {
        _personnes.ListerTravailleursAsync(_affilie, null, Arg.Any<CancellationToken>())
            .Returns([Jeu.Travailleur("Dupont", "Jean"), Jeu.Travailleur("Martin", "Lea")]);
        _postesRisques.ListerPostesAsync(_affilie, Arg.Any<CancellationToken>())
            .Returns([Jeu.Poste(_affilie, "Soudeur", "Actif", "BRUIT"), Jeu.Poste(_affilie, "Comptable"), Jeu.Poste(_affilie, "Vieux", "Archive", "BRUIT")]);
        _postesRisques.ListerPropositionsPosteRisqueAsync(_affilie, Arg.Any<CancellationToken>()).Returns([Proposition("Soumise"), Proposition("Validee")]);
        _postesRisques.ListerPropositionsListeAsync(_affilie, Arg.Any<CancellationToken>()).Returns([]);

        var tableau = await Ecrans.TableauDeBordAsync(_affilie, Ct);

        tableau.Date.ShouldBe(Jeu.Aujourdhui);
        Valeur(tableau, "travailleurs").ShouldBe(2);
        Valeur(tableau, "postesActifs").ShouldBe(2);
        Valeur(tableau, "postesExposes").ShouldBe(1);
        Valeur(tableau, "propositionsEnAttente").ShouldBe(1);
        foreach (var code in new[] { "examensDus", "examensEnRetard", "examensPlanifies", "missionsEnCours", "mesuresPlanAction", "soldeUnites" })
        {
            var indicateur = tableau.Indicateurs.Single(i => i.Code == code);
            indicateur.Disponible.ShouldBeFalse();
            indicateur.Valeur.ShouldBeNull();
            indicateur.Raison.ShouldBe(Indicateur.AVenir);
        }
    }

    [Fact]
    public async Task Une_source_en_panne_rend_ses_compteurs_indisponibles_sans_faire_echouer_le_tableau_de_bord()
    {
        _personnes.ListerTravailleursAsync(_affilie, null, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ServiceIndisponibleException(NomsServices.Personnes));
        _postesRisques.ListerPostesAsync(_affilie, Arg.Any<CancellationToken>()).Returns([Jeu.Poste(_affilie, "Soudeur", "Actif", "BRUIT")]);
        _postesRisques.ListerPropositionsPosteRisqueAsync(_affilie, Arg.Any<CancellationToken>()).Returns([]);
        _postesRisques.ListerPropositionsListeAsync(_affilie, Arg.Any<CancellationToken>()).Returns([]);

        var tableau = await Ecrans.TableauDeBordAsync(_affilie, Ct);

        var travailleurs = tableau.Indicateurs.Single(i => i.Code == "travailleurs");
        travailleurs.Disponible.ShouldBeFalse();
        travailleurs.Raison.ShouldBe(Indicateur.Indisponible);
        Valeur(tableau, "postesExposes").ShouldBe(1);
    }

    [Fact]
    public async Task Les_affilies_inconnus_du_service_sont_signales_a_part()
    {
        var connu = Guid.CreateVersion7();
        var inconnu = Guid.CreateVersion7();
        _affilies.ObtenirAffilieAsync(connu, Arg.Any<CancellationToken>()).Returns(Jeu.Affilie(connu, "Garage Lambert"));
        _affilies.ObtenirAffilieAsync(inconnu, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErreurAvalException(NomsServices.Affilies, HttpStatusCode.NotFound, "affilie.inconnu", null));

        var mes = await Ecrans.MesAffiliesAsync([connu, inconnu], Ct);

        mes.Affilies.Single().Denomination.ShouldBe("Garage Lambert");
        mes.Introuvables.ShouldBe([inconnu]);
    }

    [Fact]
    public async Task Une_liste_d_un_autre_affilie_est_inconnue()
    {
        var liste = Jeu.Liste(Guid.CreateVersion7(), "PosteSecurite", 1);
        _postesRisques.ObtenirListeNominativeAsync(liste.Id, Arg.Any<CancellationToken>()).Returns(liste);

        var erreur = await Should.ThrowAsync<ErreurAvalException>(() => Ecrans.ListeNominativeAsync(_affilie, liste.Id, Ct));

        erreur.Status.ShouldBe(HttpStatusCode.NotFound);
        await _personnes.DidNotReceiveWithAnyArgs().ListerTravailleursAsync(default, default, Ct);
    }

    [Fact]
    public async Task Le_detail_d_une_liste_nomme_les_travailleurs_y_compris_ceux_sortis_depuis()
    {
        var present = Jeu.Travailleur("Dupont", "Jean");
        var sorti = Jeu.Travailleur("Zola", "Emile");
        var poste = Jeu.Poste(_affilie, "Soudeur", "Actif", "BRUIT");
        var liste = Jeu.Liste(_affilie, "PosteSecurite", 3,
            new LigneListeAval(sorti.Id, poste.Id, ["BRUIT"], null, "Calcul"),
            new LigneListeAval(present.Id, poste.Id, ["BRUIT"], new DateOnly(2026, 3, 1), "Calcul"));
        _postesRisques.ObtenirListeNominativeAsync(liste.Id, Arg.Any<CancellationToken>()).Returns(liste);
        _postesRisques.ListerPostesAsync(_affilie, Arg.Any<CancellationToken>()).Returns([poste]);
        _personnes.ListerTravailleursAsync(_affilie, liste.DateReference, Arg.Any<CancellationToken>()).Returns([present]);
        _personnes.ObtenirPersonneAsync(sorti.Id, Arg.Any<CancellationToken>()).Returns(sorti);

        var detail = await Ecrans.ListeNominativeAsync(_affilie, liste.Id, Ct);

        detail.Lignes.Select(l => (l.Nom, l.Poste)).ShouldBe([("Dupont", "Soudeur"), ("Zola", "Soudeur")]);
    }

    [Fact]
    public async Task Une_proposition_sur_un_poste_d_un_autre_affilie_n_est_pas_transmise()
    {
        var poste = Jeu.Poste(Guid.CreateVersion7(), "Soudeur");
        _postesRisques.ObtenirPosteAsync(poste.Id, Arg.Any<CancellationToken>()).Returns(poste);

        var erreur = await Should.ThrowAsync<ErreurAvalException>(() =>
            Ecrans.ProposerPosteAsync(_affilie, poste.Id, new PropositionPosteCorps("Bruit mesuré", Jeu.Aujourdhui, []), Ct));

        erreur.Status.ShouldBe(HttpStatusCode.NotFound);
        await _postesRisques.DidNotReceiveWithAnyArgs().ProposerModificationPosteAsync(default, default!, Ct);
    }

    [Fact]
    public async Task Une_proposition_de_poste_est_deleguee_au_service_postes_et_risques()
    {
        var poste = Jeu.Poste(_affilie, "Soudeur");
        var id = Guid.CreateVersion7();
        _postesRisques.ObtenirPosteAsync(poste.Id, Arg.Any<CancellationToken>()).Returns(poste);
        _postesRisques.ProposerModificationPosteAsync(poste.Id, Arg.Any<PropositionPosteRisqueCorpsAval>(), Arg.Any<CancellationToken>()).Returns(id);

        var soumise = await Ecrans.ProposerPosteAsync(_affilie, poste.Id,
            new PropositionPosteCorps("Bruit mesuré", Jeu.Aujourdhui, [new LigneRisqueSaisie("Ajout", "BRUIT", "Eleve")]), Ct);

        soumise.ShouldBe(new PropositionSoumise(id, "Soumise"));
        await _postesRisques.Received(1).ProposerModificationPosteAsync(poste.Id,
            Arg.Is<PropositionPosteRisqueCorpsAval>(c => c.Motif == "Bruit mesuré" && c.Lignes.Single().RisqueCode == "BRUIT" && c.AvisCppt == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Les_propositions_des_deux_natures_sont_reunies_avec_leur_cible()
    {
        var poste = Jeu.Poste(_affilie, "Soudeur");
        _postesRisques.ListerPostesAsync(_affilie, Arg.Any<CancellationToken>()).Returns([poste]);
        _postesRisques.ListerPropositionsPosteRisqueAsync(_affilie, Arg.Any<CancellationToken>()).Returns([Proposition("Refusee", poste.Id)]);
        _postesRisques.ListerPropositionsListeAsync(_affilie, Arg.Any<CancellationToken>()).Returns(
        [
            new PropositionListeAval(Guid.CreateVersion7(), Guid.CreateVersion7(), _affilie, "TravailNuit", 2, "PortailEmployeur",
                DateTimeOffset.Parse("2026-10-01T10:00:00Z"), "Nouvel engagé", "Soumise", null, null, null),
        ]);

        var propositions = await Ecrans.PropositionsAsync(_affilie, Ct);

        propositions.Select(p => (p.Nature, p.Cible, p.Statut)).ShouldBe(
            [("liste-nominative", "TravailNuit v2", "Soumise"), ("poste-risque", "Soudeur", "Refusee")]);
    }

    private PropositionPosteRisqueAval Proposition(string statut, Guid? posteId = null) =>
        new(Guid.CreateVersion7(), posteId ?? Guid.CreateVersion7(), _affilie, "PortailEmployeur", DateTimeOffset.Parse("2026-09-15T10:00:00Z"),
            "Motif", Jeu.Aujourdhui, statut, null, statut == "Refusee" ? "Avis CPPT manquant" : null);

    private static int? Valeur(TableauDeBord tableau, string code) => tableau.Indicateurs.Single(i => i.Code == code).Valeur;

    private sealed class Horloge(DateTimeOffset maintenant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => maintenant;
    }
}
