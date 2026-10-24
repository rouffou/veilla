using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Decisions;
using Sepp.Contracts.Documents;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.Documents.Application.Documents;
using Sepp.Documents.Application.EvaluationSante;
using Sepp.Documents.Application.Modeles;
using Sepp.Documents.Application.Securite;
using Sepp.Documents.Domain.Commun;
using Sepp.Documents.Domain.Documents;
using Sepp.Documents.Domain.Fusion;
using Sepp.Documents.Domain.Langues;
using Sepp.Documents.Domain.Modeles;

using Shouldly;

namespace Sepp.Documents.Application.Tests;

/// <summary>Décision #356 : chaque instance du service Documents ne sert que ses zones.</summary>
public class ZonesServiesTests
{
    private static readonly Guid Affilie = Guid.CreateVersion7();
    private static readonly Guid Personne = Guid.CreateVersion7();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static DecisionEmise Decision() =>
        new(Guid.CreateVersion7(), Personne, Affilie, CategoriesDecision.Apte, [], new DateOnly(2027, 10, 5));

    private static GenererDocument DemandeMedicale() => new(
        "COURRIER", TypeDestinataire.Personne, Personne, "test", "objet", Guid.CreateVersion7(),
        new Dictionary<string, ValeurChamp> { ["nom"] = ValeurChamp.Simple("Marie") }, null, null, null, Affilie, Personne, false);

    private static void PublierModelesParDefaut(Contexte c)
    {
        foreach (var d in ModelesParDefaut.Definitions)
        {
            var modele = Modele.Creer(d.Code, d.Langue, TypeModele.Formulaire, d.Zone, d.Libelle, ModelesParDefaut.Avertissement, d.Contenu, ModelesParDefaut.Champs);
            modele.Valider("cpmt-dirigeant", c.Horloge.GetUtcNow());
            modele.Publier("admin", c.Horloge.GetUtcNow());
            c.Store.Add(modele);
        }
    }

    private static DecisionEmiseHandler Handler(Contexte c) => new(c.Generateur, c.Store, c.Store, c.ZonesServies);

    [Fact]
    public void La_table_des_exemplaires_repartit_employeur_en_standard_travailleur_et_dossier_en_medicale()
    {
        DecisionEmiseHandler.ZonesDesExemplaires.ShouldBe(new Dictionary<string, ZoneDocument>
        {
            ["employeur"] = ZoneDocument.Standard,
            ["travailleur"] = ZoneDocument.Medicale,
            ["dossier"] = ZoneDocument.Medicale,
        });
    }

    [Fact]
    public async Task L_instance_standard_ne_genere_que_l_exemplaire_de_l_employeur()
    {
        var c = new Contexte("svc", [Roles.Cpmt], ZoneDocument.Standard);
        PublierModelesParDefaut(c);

        await Handler(c).HandleAsync(Decision(), _ct);

        c.Store.Documents.Select(d => d.Exemplaire).ShouldBe(["employeur"]);
        c.Store.Published.OfType<DocumentPublie>().Select(p => p.Zone).ShouldBe(["standard"]);
    }

    [Fact]
    public async Task L_instance_medicale_ne_genere_que_les_exemplaires_du_travailleur_et_du_dossier()
    {
        var c = new Contexte("svc", [Roles.Cpmt], ZoneDocument.Medicale);
        PublierModelesParDefaut(c);

        await Handler(c).HandleAsync(Decision(), _ct);

        c.Store.Documents.Select(d => d.Exemplaire).Order().ShouldBe(["dossier", "travailleur"]);
        c.Store.Documents.ShouldAllBe(d => d.Zone == ZoneDocument.Medicale);
        c.Store.Published.OfType<DocumentPublie>().Select(p => p.Zone).ShouldBe(["medicale"]);
    }

    [Fact]
    public async Task Deux_instances_traitant_le_meme_evenement_produisent_les_trois_exemplaires_sans_doublon()
    {
        var standard = new Contexte("svc", [Roles.Cpmt], ZoneDocument.Standard);
        var medicale = new Contexte("svc", [Roles.Cpmt], ZoneDocument.Medicale);
        PublierModelesParDefaut(standard);
        PublierModelesParDefaut(medicale);
        var decision = Decision();

        await Handler(standard).HandleAsync(decision, _ct);
        await Handler(medicale).HandleAsync(decision, _ct);
        await Handler(standard).HandleAsync(decision, _ct);
        await Handler(medicale).HandleAsync(decision, _ct);

        var cles = standard.Store.Documents.Concat(medicale.Store.Documents).Select(d => d.CleIdempotence).ToList();
        cles.Order().ShouldBe([$"decision:{decision.DecisionId}:dossier", $"decision:{decision.DecisionId}:employeur", $"decision:{decision.DecisionId}:travailleur"]);
    }

    [Fact]
    public async Task La_generation_d_un_document_d_une_zone_non_servie_est_refusee_en_422()
    {
        var c = new Contexte("medecin", [Roles.Cpmt], ZoneDocument.Standard);
        c.Publie("COURRIER", Language.Fr, ZoneDocument.Medicale, "Bonjour {{nom}}", new ChampDeclare("nom", TypeChamp.Texte, true, "Nom"));
        var handler = new GenererDocumentHandler(c.Generateur, c.Store, c.Acces, c.Store, c.Store);

        var resultat = await handler.HandleAsync(DemandeMedicale(), _ct);

        resultat.Error!.Kind.ShouldBe(ErrorKind.Unprocessable);
        resultat.Error.Code.ShouldBe("documents.zone-non-servie");
        c.Store.Documents.ShouldBeEmpty();
    }

    [Fact]
    public async Task La_creation_et_la_validation_d_un_modele_d_une_zone_non_servie_sont_refusees_en_422()
    {
        var admin = new Contexte("admin", [Roles.AdministrateurFonctionnel], ZoneDocument.Standard);
        var creation = await new CreerModeleHandler(admin.Store, admin.Store, admin.Utilisateur, admin.Acces)
            .HandleAsync(new CreerModele("SANTE.TEST", Language.Fr, TypeModele.Formulaire, "medicale", "Test", null, "{{nom}}", [new ChampDto("nom", TypeChamp.Texte, true, "Nom")]), _ct);
        creation.Error!.Code.ShouldBe("documents.zone-non-servie");
        creation.Error.Kind.ShouldBe(ErrorKind.Unprocessable);
        admin.Store.Modeles.ShouldBeEmpty();

        var dirigeant = new Contexte("dirigeant", [Roles.CpmtDirigeant], ZoneDocument.Standard);
        var modele = Modele.Creer("SANTE.TEST", Language.Fr, TypeModele.Formulaire, ZoneDocument.Medicale, "Test", null, "{{nom}}", [new ChampDeclare("nom", TypeChamp.Texte, true, "Nom")]);
        dirigeant.Store.Add(modele);
        var validation = await new ValiderModeleHandler(dirigeant.Store, dirigeant.Store, dirigeant.Utilisateur, dirigeant.Acces, dirigeant.Horloge)
            .HandleAsync(new ValiderModele(modele.Id), _ct);
        validation.Error!.Code.ShouldBe("documents.zone-non-servie");
        validation.Error.Kind.ShouldBe(ErrorKind.Unprocessable);
        modele.Statut.ShouldBe(StatutModele.Brouillon);
    }

    [Fact]
    public async Task La_lecture_d_un_document_d_une_zone_non_servie_repond_404()
    {
        var complet = new Contexte("medecin", [Roles.Cpmt]);
        complet.Publie("COURRIER", Language.Fr, ZoneDocument.Medicale, "Bonjour {{nom}}", new ChampDeclare("nom", TypeChamp.Texte, true, "Nom"));
        var genere = await new GenererDocumentHandler(complet.Generateur, complet.Store, complet.Acces, complet.Store, complet.Store)
            .HandleAsync(DemandeMedicale(), _ct);

        var standard = new Contexte("medecin", [Roles.Cpmt], ZoneDocument.Standard);
        standard.Store.Documents.AddRange(complet.Store.Documents);

        var lecture = await new ObtenirDocumentHandler(standard.Store, standard.Acces).HandleAsync(new ObtenirDocument(genere.Value.Id), _ct);
        lecture.Error!.Kind.ShouldBe(ErrorKind.NotFound);
        var contenu = await new LireContenuDocumentHandler(standard.Store, standard.Stockage, standard.Chiffrement, standard.Store, standard.Acces)
            .HandleAsync(new LireContenuDocument(genere.Value.Id, "suivi"), _ct);
        contenu.Error!.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Theory]
    [InlineData(ZoneDocument.Standard, 3)]
    [InlineData(ZoneDocument.Medicale, 12)]
    public async Task L_amorcage_des_modeles_est_filtre_par_zone(ZoneDocument zone, int attendus)
    {
        var c = new Contexte("svc", [Roles.Cpmt], zone);
        var init = new InitialiserModelesParDefaut(c.Store, c.Store, c.ZonesServies);

        (await init.ExecuteAsync(_ct)).ShouldBe(attendus);

        c.Store.Modeles.ShouldAllBe(m => m.Zone == zone);
        (await init.ExecuteAsync(_ct)).ShouldBe(0);
    }

    [Fact]
    public async Task L_amorcage_d_une_instance_psychosociale_seule_ne_cree_aucun_modele()
    {
        var c = new Contexte("svc", [Roles.Cpmt], ZoneDocument.Psychosociale);

        (await new InitialiserModelesParDefaut(c.Store, c.Store, c.ZonesServies).ExecuteAsync(_ct)).ShouldBe(0);
    }
}
