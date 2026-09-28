using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.PostesRisques;
using Sepp.PostesRisques.Application.Risques;
using Sepp.PostesRisques.Application.Surcharges;
using Sepp.PostesRisques.Domain.Risques;
using Sepp.PostesRisques.Domain.Surcharges;

using Shouldly;

namespace Sepp.PostesRisques.Application.Tests;

/// <summary>AFF-11, AFF-12 : import du référentiel ; AFF-13 : surcharges réservées au CPMT.</summary>
public class RisquesEtSurchargesTests
{
    private const string Entete =
        "code;categorie;libelle_fr;libelle_nl;libelle_de;libelle_en;reference_legale;type_surveillance;frequence_mois;actes_supplementaires;vaccins;surveillance_prolongee;valide_du";

    private readonly InMemoryStore _store = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly FakeUser _admin = new("admin", Roles.AdministrateurFonctionnel);
    private readonly DateOnly _aujourdhui = new(2026, 9, 28);

    private Task<Result<ImportRisquesDto>> Importer(string contenu, ICurrentUser? user = null) =>
        new ImporterRisquesHandler(_store, _store, _store, user ?? _admin).HandleAsync(new ImporterRisques(contenu, _aujourdhui), _ct);

    internal static string JeuExemple()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sepp.slnx")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "Services", "PostesRisques", "exemples", "risques-exemple.csv"));
    }

    [Fact]
    public async Task Le_jeu_d_exemple_s_importe_et_couvre_toutes_les_categories()
    {
        var contenu = JeuExemple();
        contenu.ShouldContain("JEU D'EXEMPLE — À REMPLACER");

        var result = await Importer(contenu);

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        result.Value.RisquesCrees.ShouldBe(12);
        _store.Risques.ShouldAllBe(r => r.Code.StartsWith("EX.") && r.ReferenceLegale.StartsWith("EXEMPLE"));
        _store.Risques.Select(r => r.Categorie).Distinct().Count().ShouldBe(Enum.GetValues<CategorieRisque>().Length - 1);
        _store.Published.OfType<RegleSurveillanceModifiee>().Count().ShouldBe(12);
        _store.Risques.Single(r => r.Code == "EX.SECU.POSTE").RegleAu(_aujourdhui)!.ActesSupplementaires.ShouldBe(["EX.AUDITION", "EX.VISION"]);
    }

    [Fact]
    public async Task Reimporter_le_meme_fichier_ne_change_rien()
    {
        await Importer(JeuExemple());
        var publies = _store.Published.Count;

        var result = await Importer(JeuExemple());

        result.Value.ShouldBe(new ImportRisquesDto(0, 0, 0, 12));
        _store.Published.Count.ShouldBe(publies);
    }

    [Fact]
    public async Task Une_nouvelle_frequence_importee_versionne_la_regle()
    {
        await Importer($"{Entete}\nEX.A;Physique;A;A;A;;Ref;EvaluationSantePeriodique;36;;;non;2026-01-01");

        var result = await Importer($"{Entete}\nEX.A;physique;A;A;A;;Ref;evaluation_sante_periodique;24;;;non;2027-01-01");

        result.Value.ReglesVersionnees.ShouldBe(1);
        var risque = _store.Risques.ShouldHaveSingleItem();
        risque.Regles.Count.ShouldBe(2);
        risque.RegleAu(new DateOnly(2027, 1, 1))!.Version.ShouldBe(2);
        _store.Published.OfType<RegleSurveillanceModifiee>().Last().FrequenceMois.ShouldBe(24);
    }

    [Fact]
    public async Task Un_import_avec_une_ligne_invalide_n_enregistre_rien()
    {
        var contenu = $"{Entete}\n# commentaire\nEX.OK;Physique;A;A;A;;Ref;EvaluationSantePeriodique;12;;;non;\n" +
                      "EX.KO;Inconnue;A;A;A;;Ref;EvaluationSantePeriodique;12;;;non;\n\"EX.GUILLEMETS;Physique";

        var result = await Importer(contenu);

        result.Error!.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Message.ShouldContain("ligne 5");
        _store.Risques.ShouldBeEmpty();
        _store.Saves.ShouldBe(0);
        _store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Les_champs_entre_guillemets_peuvent_contenir_le_separateur()
    {
        var result = await Importer($"{Entete}\nEX.Q;Chimique;\"Solvants; diluants\";\"Oplosmiddelen\";\"Lösungsmittel\";;\"Ref \"\"x\"\"\";EvaluationSantePeriodique;12;;;oui;");

        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        var risque = _store.Risques.ShouldHaveSingleItem();
        risque.Libelle.Fr.ShouldBe("Solvants; diluants");
        risque.ReferenceLegale.ShouldBe("Ref \"x\"");
        risque.RegleAu(_aujourdhui)!.SurveillanceProlongee.ShouldBeTrue();
    }

    [Fact]
    public async Task Seul_l_administrateur_fonctionnel_importe_le_referentiel() =>
        (await Importer(JeuExemple(), new FakeUser("cpmt", Roles.Cpmt))).Error!.Kind.ShouldBe(ErrorKind.Forbidden);

    [Fact]
    public async Task Une_colonne_manquante_est_signalee() =>
        (await Importer("code;categorie\nEX.A;Physique")).Error!.Message.ShouldContain("libelle_fr");

    private Task<Result<Guid>> Surcharger(ICurrentUser user, Guid affilie, Guid personne, DateOnly du, DateOnly? au = null) =>
        new DefinirSurchargeFrequenceHandler(_store, _store, _store, _store, _store, user, FakePerimetre.Interne)
            .HandleAsync(new DefinirSurchargeFrequence(affilie, CibleSurcharge.Personne, personne, "ex.phys.bruit", 6, "Suivi rapproché", du, au), _ct);

    [Fact]
    public async Task Le_cpmt_surcharge_une_frequence_pour_un_travailleur_et_l_evenement_est_publie()
    {
        await Importer(JeuExemple());
        _store.Published.Clear();
        var affilie = Guid.CreateVersion7();
        var personne = Guid.CreateVersion7();

        var result = await Surcharger(new FakeUser("cpmt-1", Roles.Cpmt), affilie, personne, new DateOnly(2026, 10, 1));

        result.IsSuccess.ShouldBeTrue();
        _store.Surcharges.ShouldHaveSingleItem().CpmtId.ShouldBe("cpmt-1");
        var evt = _store.Published.ShouldHaveSingleItem().ShouldBeOfType<SurchargeFrequenceDefinie>();
        evt.CibleType.ShouldBe("Personne");
        evt.CibleId.ShouldBe(personne);
        evt.CodeRisque.ShouldBe("EX.PHYS.BRUIT");
        evt.FrequenceMois.ShouldBe(6);

        (await Surcharger(new FakeUser("cpmt-1", Roles.Cpmt), affilie, personne, new DateOnly(2027, 1, 1)))
            .Error!.Kind.ShouldBe(ErrorKind.Conflict);

        (await new CloturerSurchargeFrequenceHandler(_store, _store, _store, new FakeUser("cpmt-1", Roles.Cpmt), FakePerimetre.Interne)
            .HandleAsync(new CloturerSurchargeFrequence(result.Value, new DateOnly(2027, 1, 1)), _ct)).IsSuccess.ShouldBeTrue();
        (await Surcharger(new FakeUser("cpmt-1", Roles.Cpmt), affilie, personne, new DateOnly(2027, 1, 1))).IsSuccess.ShouldBeTrue();
        _store.Published.OfType<SurchargeFrequenceDefinie>().Count(e => e.ValideJusquAu == new DateOnly(2027, 1, 1)).ShouldBe(1);
    }

    [Theory]
    [InlineData(Roles.Infirmier)]
    [InlineData(Roles.GestionnaireDossiers)]
    [InlineData(Roles.Employeur)]
    [InlineData(Roles.AdministrateurFonctionnel)]
    public async Task Seul_le_cpmt_surcharge_une_frequence(string role)
    {
        await Importer(JeuExemple());

        (await Surcharger(new FakeUser("x", role), Guid.CreateVersion7(), Guid.CreateVersion7(), new DateOnly(2026, 10, 1)))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        _store.Surcharges.ShouldBeEmpty();
    }

    [Fact]
    public async Task L_infirmier_consulte_les_surcharges_mais_pas_l_employeur()
    {
        var affilie = Guid.CreateVersion7();

        (await new ListerSurchargesFrequenceHandler(_store, new FakeUser("inf", Roles.Infirmier), FakePerimetre.Interne)
            .HandleAsync(new ListerSurchargesFrequence(affilie, null, null, null), _ct)).IsSuccess.ShouldBeTrue();
        (await new ListerSurchargesFrequenceHandler(_store, new FakeUser("emp", Roles.Employeur), new FakePerimetre(true, affilie))
            .HandleAsync(new ListerSurchargesFrequence(affilie, null, null, null), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Le_referentiel_est_consultable_par_categorie_et_dans_la_langue_demandee()
    {
        await Importer(JeuExemple());

        var risques = await new ListerRisquesHandler(_store).HandleAsync(new ListerRisques(CategorieRisque.Physique, _aujourdhui, Language.De), _ct);

        risques.Value.Select(r => r.Code).ShouldBe(["EX.PHYS.BRUIT", "EX.PHYS.VIBRATIONS"]);
        risques.Value[0].Libelle.ShouldBe("Beispiel — Lärm");
        risques.Value[0].RegleApplicable!.TypeSurveillance.ShouldBe(TypeSurveillance.ActesMedicauxSupplementaires);
    }
}
