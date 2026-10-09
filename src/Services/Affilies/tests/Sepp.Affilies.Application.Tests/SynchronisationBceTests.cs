using System.Globalization;
using System.Text.Json.Nodes;

using Sepp.Affilies.Application.Affilies;
using Sepp.Affilies.Application.Bce;
using Sepp.Affilies.Application.Securite;
using Sepp.Affilies.Domain.Affilies;
using Sepp.Affilies.Domain.Ecarts;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Affilies;
using Sepp.Contracts.Integrations;

using Shouldly;

namespace Sepp.Affilies.Application.Tests;

/// <summary>Lecture simulée des données BCE chez Intégrations.</summary>
internal sealed class FakeEntrepriseBce : IEntrepriseBceClient
{
    public EntrepriseBce? Reponse { get; set; }

    public bool Indisponible { get; set; }

    public int Appels { get; private set; }

    public Task<EntrepriseBce?> LireAsync(string numeroBce, CancellationToken cancellationToken)
    {
        Appels++;
        return Indisponible
            ? throw new EntrepriseBceIndisponibleException("Intégrations injoignable.")
            : Task.FromResult(Reponse);
    }
}

/// <summary>AFF-01, AFF-02, INT-04 — Consommation de <c>integrations.donnees-bce-recues.v1</c> par Affiliés.</summary>
public class SynchronisationBceTests
{
    private const string Bce = "0202.239.951";
    private const string BceCanonique = "0202239951";
    private static readonly DateOnly Extraction = new(2026, 10, 9);
    private static readonly FicheSaisie Fiche = new("Boulangerie Dupont", "SRL", "10.711", "118.03", CategorieTarifaire.B, Language.Fr, RegimeLinguistique.Francais);

    private readonly InMemoryStore _store = new();
    private readonly FakeEntrepriseBce _bce = new();
    private readonly HorlogeFixe _horloge = new(new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero));
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static string NumeroUnite(string huitChiffres) =>
        huitChiffres + (97 - (long.Parse(huitChiffres, CultureInfo.InvariantCulture) % 97)).ToString("00", CultureInfo.InvariantCulture);

    private static UniteBce Unite(string numero, string nom = "Siège BCE", string rue = "Rue de la Loi") =>
        new(numero, nom, rue, "16", null, "1000", "Bruxelles", "BE", new DateOnly(2010, 1, 1));

    private static EntrepriseBce Entreprise(string denomination = "Boulangerie Dupont SRL", string nace = "10.711", params UniteBce[] unites) =>
        new(BceCanonique, denomination, "SRL", nace, Extraction, unites);

    private static DonneesBceRecues Evenement(string numero = BceCanonique, Guid? affilieId = null) =>
        new(numero, affilieId, "Boulangerie Dupont SRL", "SRL", "10.711", [], Extraction);

    // Traitement technique du consommateur : aucun rôle (aucun utilisateur), comme hors requête HTTP.
    private DonneesBceRecuesHandler Handler()
    {
        var utilisateur = new FakeUser([]);
        var acces = new ControleAcces(utilisateur, utilisateur);
        var modificateur = new ModificateurAffilie(_store, _store, _store, _store, utilisateur, acces, _horloge);
        return new DonneesBceRecuesHandler(_store, _bce, _store, modificateur, _store, _horloge);
    }

    private async Task<Guid> CreerAsync()
    {
        var gestionnaire = new FakeUser([Roles.GestionnaireDossiers]);
        var acces = new ControleAcces(gestionnaire, gestionnaire);
        var modificateur = new ModificateurAffilie(_store, _store, _store, _store, gestionnaire, acces, _horloge);
        var resultat = await new CreerAffilieHandler(_store, _store, acces, modificateur, _store)
            .HandleAsync(new CreerAffilie(Bce, Fiche, new DateOnly(2024, 1, 1), null, null), _ct);
        resultat.IsSuccess.ShouldBeTrue(resultat.Error?.Message);
        _store.Published.Clear();
        return resultat.Value;
    }

    [Fact]
    public async Task La_fiche_et_les_unites_sont_mises_a_jour_depuis_les_donnees_lues_chez_Integrations()
    {
        var id = await CreerAsync();
        _bce.Reponse = Entreprise("Dupont & Fils SA", "47.110", Unite(NumeroUnite("21234567"), "Boulangerie centrale"), Unite(NumeroUnite("21234568")));

        await Handler().HandleAsync(Evenement(affilieId: id), _ct);

        var affilie = _store.Affilies.Single();
        affilie.Denomination.ShouldBe("Dupont & Fils SA");
        affilie.CodeNace.ShouldBe("47.110");
        affilie.UnitesEtablissement.Count.ShouldBe(2);
        affilie.UnitesEtablissement.Single(u => u.Numero.Value == NumeroUnite("21234567")).Nom.ShouldBe("Boulangerie centrale");
        _store.Ecarts.ShouldBeEmpty();
        _bce.Appels.ShouldBe(1);
    }

    [Fact]
    public async Task La_mise_a_jour_est_historisee_sous_l_identite_technique_et_publie_AffilieModifie()
    {
        await CreerAsync();
        _bce.Reponse = Entreprise("Dupont & Fils SA", unites: Unite(NumeroUnite("21234567")));

        await Handler().HandleAsync(Evenement(), _ct);

        var modification = _store.Historique.Last();
        modification.Action.ShouldBe(DonneesBceRecuesHandler.ActionHistorique);
        modification.Auteur.ShouldBe("test-user");
        var avant = JsonNode.Parse(modification.Avant!)!.AsObject();
        var apres = JsonNode.Parse(modification.Apres!)!.AsObject();
        avant["fiche"]!["denomination"]!.GetValue<string>().ShouldBe("Boulangerie Dupont");
        apres["fiche"]!["denomination"]!.GetValue<string>().ShouldBe("Dupont & Fils SA");
        apres.ContainsKey("unitesEtablissement").ShouldBeTrue();
        _store.Published.ShouldHaveSingleItem().ShouldBeOfType<AffilieModifie>();
    }

    [Fact]
    public async Task Un_meme_message_rejoue_est_sans_effet_sur_l_affilie_et_les_ecarts()
    {
        await CreerAsync();
        _bce.Reponse = Entreprise("Dupont & Fils SA", unites: [Unite(NumeroUnite("21234567")), Unite("1234567890")]);
        await Handler().HandleAsync(Evenement(), _ct);
        var version = _store.Affilies.Single().NumeroVersion;
        var historique = _store.Historique.Count;
        var publies = _store.Published.Count;

        await Handler().HandleAsync(Evenement(), _ct);

        _store.Affilies.Single().NumeroVersion.ShouldBe(version);
        _store.Historique.Count.ShouldBe(historique);
        _store.Published.Count.ShouldBe(publies);
        _store.Ecarts.ShouldHaveSingleItem().Code.ShouldBe(CodesEcartBce.UniteInvalide);
    }

    [Fact]
    public async Task Un_affilie_inconnu_est_consigne_comme_ecart_sans_lire_Integrations_ni_faire_echouer_le_message()
    {
        await Handler().HandleAsync(Evenement(), _ct);

        var ecart = _store.Ecarts.ShouldHaveSingleItem();
        ecart.Code.ShouldBe(CodesEcartBce.AffilieInconnu);
        ecart.NumeroBce.ShouldBe(BceCanonique);
        ecart.AffilieId.ShouldBeNull();
        ecart.Statut.ShouldBe(StatutEcart.Ouvert);
        _bce.Appels.ShouldBe(0);
        _store.Affilies.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_numero_BCE_invalide_est_consigne_comme_ecart()
    {
        await Handler().HandleAsync(Evenement("0202239952"), _ct);

        _store.Ecarts.ShouldHaveSingleItem().Code.ShouldBe(CodesEcartBce.NumeroInvalide);
    }

    [Fact]
    public async Task L_affilie_est_retrouve_par_le_numero_BCE_meme_si_l_AffilieId_de_l_evenement_differe()
    {
        await CreerAsync();
        _bce.Reponse = Entreprise("Dupont & Fils SA");

        await Handler().HandleAsync(Evenement(affilieId: Guid.CreateVersion7()), _ct);

        _store.Affilies.Single().Denomination.ShouldBe("Dupont & Fils SA");
        _store.Ecarts.ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_unite_d_un_autre_affilie_est_un_ecart_rattache_a_l_affilie_et_le_reste_est_applique()
    {
        var id = await CreerAsync();
        var autre = Affilie.Creer(new NumeroBce("0403.170.701"), new DonneesFiche("Autre SA", "SA", "10.711", "118.03", CategorieTarifaire.A, Language.Fr, RegimeLinguistique.Francais), new DateOnly(2024, 1, 1));
        var numeroPris = NumeroUnite("21234567");
        autre.AjouterUniteEtablissement(new NumeroUniteEtablissement(numeroPris), "Chez l'autre", new Adresse("Rue X", "1", null, "1000", "Bruxelles"), Language.Fr, new DateOnly(2020, 1, 1));
        _store.Affilies.Add(autre);
        _bce.Reponse = Entreprise("Dupont & Fils SA", unites: [Unite(numeroPris), Unite(NumeroUnite("21234568"))]);

        await Handler().HandleAsync(Evenement(), _ct);

        var ecart = _store.Ecarts.ShouldHaveSingleItem();
        ecart.Code.ShouldBe(CodesEcartBce.UniteAutreAffilie);
        ecart.Reference.ShouldBe(numeroPris);
        ecart.AffilieId.ShouldBe(id);
        var affilie = _store.Affilies.Single(a => a.Id == id);
        affilie.UnitesEtablissement.ShouldHaveSingleItem().Numero.Value.ShouldBe(NumeroUnite("21234568"));
        affilie.Denomination.ShouldBe("Dupont & Fils SA");
    }

    [Fact]
    public async Task Un_ecart_sans_changement_de_fiche_est_quand_meme_valide()
    {
        await CreerAsync();
        _bce.Reponse = Entreprise("Boulangerie Dupont", unites: Unite("1234567890"));

        await Handler().HandleAsync(Evenement(), _ct);

        _store.Ecarts.ShouldHaveSingleItem().Code.ShouldBe(CodesEcartBce.UniteInvalide);
        _store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_ecart_redetecte_n_est_pas_duplique_et_un_ecart_qui_ne_se_represente_plus_est_clos()
    {
        await CreerAsync();
        var invalide = Unite("1234567890");
        _bce.Reponse = Entreprise("Boulangerie Dupont", unites: invalide);
        await Handler().HandleAsync(Evenement(), _ct);
        _horloge.Maintenant = _horloge.Maintenant.AddHours(2);
        await Handler().HandleAsync(Evenement(), _ct);

        var ecart = _store.Ecarts.ShouldHaveSingleItem();
        ecart.DerniereDetectionLe.ShouldBeGreaterThan(ecart.DetecteLe);
        ecart.Statut.ShouldBe(StatutEcart.Ouvert);

        _bce.Reponse = Entreprise("Boulangerie Dupont", unites: Unite(NumeroUnite("21234567")));
        await Handler().HandleAsync(Evenement(), _ct);

        ecart.Statut.ShouldBe(StatutEcart.Resolu);
        ecart.ResoluPar.ShouldBe("system");
    }

    [Fact]
    public async Task Sans_donnees_chez_Integrations_la_fiche_vient_de_l_evenement_et_les_unites_restent_avec_un_ecart()
    {
        await CreerAsync();
        _bce.Reponse = null;

        await Handler().HandleAsync(Evenement() with { Denomination = "Dupont & Fils SA" }, _ct);

        _store.Affilies.Single().Denomination.ShouldBe("Dupont & Fils SA");
        _store.Ecarts.ShouldHaveSingleItem().Code.ShouldBe(CodesEcartBce.DetailIndisponible);
    }

    [Fact]
    public async Task Une_panne_technique_de_lecture_fait_echouer_le_message_sans_rien_modifier()
    {
        await CreerAsync();
        _bce.Indisponible = true;

        await Should.ThrowAsync<EntrepriseBceIndisponibleException>(() => Handler().HandleAsync(Evenement(), _ct));

        _store.Affilies.Single().Denomination.ShouldBe("Boulangerie Dupont");
        _store.Ecarts.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_affilie_cloture_n_est_pas_modifie_et_l_ecart_est_consigne()
    {
        var id = await CreerAsync();
        var absorbant = Guid.CreateVersion7();
        var affilie = _store.Affilies.Single(a => a.Id == id);
        var effet = new DateOnly(2026, 6, 1);
        affilie.RealiserOperation(affilie.ProjeterFusion(absorbant, effet).Id, effet);
        _bce.Reponse = Entreprise("Dupont & Fils SA");

        await Handler().HandleAsync(Evenement(), _ct);

        affilie.Denomination.ShouldBe("Boulangerie Dupont");
        _store.Ecarts.ShouldHaveSingleItem().Code.ShouldBe(CodesEcartBce.AffilieCloture);
    }

    [Fact]
    public async Task Seul_le_gestionnaire_liste_et_traite_les_ecarts()
    {
        await Handler().HandleAsync(Evenement(), _ct);
        var ecartId = _store.Ecarts.Single().Id;
        var gestionnaire = new FakeUser([Roles.GestionnaireDossiers]);
        var employeur = new FakeUser([Roles.Employeur], Guid.CreateVersion7());

        var liste = await new ListerEcartsBceHandler(_store, new ControleAcces(gestionnaire, gestionnaire)).HandleAsync(new ListerEcartsBce(), _ct);
        var refusListe = await new ListerEcartsBceHandler(_store, new ControleAcces(employeur, employeur)).HandleAsync(new ListerEcartsBce(), _ct);
        var refusResolution = await new ResoudreEcartBceHandler(_store, _store, new ControleAcces(employeur, employeur), employeur, _horloge)
            .HandleAsync(new ResoudreEcartBce(ecartId), _ct);

        liste.Value.ShouldHaveSingleItem().Code.ShouldBe(CodesEcartBce.AffilieInconnu);
        refusListe.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        refusResolution.Error!.Kind.ShouldBe(ErrorKind.Forbidden);

        var resolution = await new ResoudreEcartBceHandler(_store, _store, new ControleAcces(gestionnaire, gestionnaire), gestionnaire, _horloge)
            .HandleAsync(new ResoudreEcartBce(ecartId), _ct);

        resolution.IsSuccess.ShouldBeTrue();
        _store.Ecarts.Single().Statut.ShouldBe(StatutEcart.Resolu);
        _store.Ecarts.Single().ResoluPar.ShouldBe("test-user");
        (await new ListerEcartsBceHandler(_store, new ControleAcces(gestionnaire, gestionnaire)).HandleAsync(new ListerEcartsBce(), _ct)).Value.ShouldBeEmpty();
        (await new ResoudreEcartBceHandler(_store, _store, new ControleAcces(gestionnaire, gestionnaire), gestionnaire, _horloge)
            .HandleAsync(new ResoudreEcartBce(Guid.CreateVersion7()), _ct)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
    }
}
