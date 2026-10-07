using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Referentiels;
using Sepp.Referentiels.Application.Calendrier;
using Sepp.Referentiels.Application.Nomenclatures;
using Sepp.Referentiels.Application.Parametres;

using Shouldly;

namespace Sepp.Referentiels.Application.Tests;

public class UseCasesTests
{
    private readonly InMemoryStore _store = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task L_initialisation_cree_les_parametres_legaux_une_seule_fois()
    {
        var init = new InitialiserParametresLegaux(_store, _store);

        (await init.ExecuteAsync(_ct)).ShouldBe(CatalogueParametresLegaux.Definitions.Count);
        (await init.ExecuteAsync(_ct)).ShouldBe(0);
        _store.Parametres.Select(p => p.Code.Value).ShouldContain("REINTEGRATION.INVITATION.DELAI");
    }

    [Fact]
    public async Task L_administrateur_modifie_un_parametre_et_un_evenement_est_publie()
    {
        await new InitialiserParametresLegaux(_store, _store).ExecuteAsync(_ct);
        var handler = new DefinirValeurParametreHandler(_store, _store, _store, new FakeUser(Roles.AdministrateurFonctionnel));

        var result = await handler.HandleAsync(new DefinirValeurParametre("SANTE.REPRISE.DELAI", 12, new DateOnly(2027, 1, 1)), _ct);

        result.IsSuccess.ShouldBeTrue();
        var evt = _store.Published.ShouldHaveSingleItem().ShouldBeOfType<ParametreLegalModifie>();
        evt.Code.ShouldBe("SANTE.REPRISE.DELAI");
        evt.Valeur.ShouldBe(12);
    }

    [Fact]
    public async Task Un_conseiller_ne_peut_pas_modifier_un_parametre()
    {
        await new InitialiserParametresLegaux(_store, _store).ExecuteAsync(_ct);
        var handler = new DefinirValeurParametreHandler(_store, _store, _store, new FakeUser(Roles.Cpmt));

        var result = await handler.HandleAsync(new DefinirValeurParametre("SANTE.REPRISE.DELAI", 12, new DateOnly(2027, 1, 1)), _ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        _store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_valeur_antidatee_renvoie_une_erreur_de_validation()
    {
        await new InitialiserParametresLegaux(_store, _store).ExecuteAsync(_ct);
        var handler = new DefinirValeurParametreHandler(_store, _store, _store, new FakeUser(Roles.AdministrateurFonctionnel));

        var result = await handler.HandleAsync(new DefinirValeurParametre("SANTE.REPRISE.DELAI", 12, new DateOnly(2025, 1, 1)), _ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task Un_parametre_inconnu_renvoie_introuvable()
    {
        var result = await new ObtenirParametreHandler(_store).HandleAsync(new ObtenirParametre("INCONNU", new DateOnly(2026, 1, 1), Language.Fr), _ct);

        result.Error!.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task L_echeance_tient_compte_des_jours_feries_supplementaires()
    {
        var handler = new AjouterJourFerieHandler(_store, _store, _store, new FakeUser(Roles.AdministrateurFonctionnel));
        (await handler.HandleAsync(new AjouterJourFerie(new DateOnly(2026, 9, 29), "TEST", "Test", "Test", "Test", null), _ct)).IsSuccess.ShouldBeTrue();

        // Lundi 28/09/2026 + 1 jour ouvrable : le mardi 29 est férié → mercredi 30.
        var echeance = await new CalculerEcheanceHandler(_store).HandleAsync(new CalculerEcheance(new DateOnly(2026, 9, 28), 1), _ct);

        echeance.Value.Echeance.ShouldBe(new DateOnly(2026, 9, 30));
        // L'événement porte l'état complet des jours supplémentaires (calendrier des consommateurs, DAT-08).
        _store.Published.ShouldHaveSingleItem().ShouldBeOfType<JoursFeriesModifies>()
            .JoursSupplementaires.ShouldBe([new DateOnly(2026, 9, 29)]);
    }

    [Fact]
    public async Task Les_jours_feries_sont_renvoyes_dans_la_langue_demandee()
    {
        var jours = await new ListerJoursFeriesHandler(_store).HandleAsync(new ListerJoursFeries(2026, Language.De), _ct);

        jours.Value.Count.ShouldBe(10);
        jours.Value.ShouldAllBe(j => j.Legal);
        jours.Value.ShouldContain(j => j.Libelle == "Weihnachten");
    }

    [Fact]
    public async Task Une_nomenclature_est_creee_puis_alimentee()
    {
        var admin = new FakeUser(Roles.AdministrateurFonctionnel);
        var libelle = new LibellesDto("Commissions paritaires", "Paritaire comités", "Paritätische Kommissionen", null);
        (await new CreerNomenclatureHandler(_store, _store, _store, admin).HandleAsync(new CreerNomenclature("cp", libelle), _ct)).IsSuccess.ShouldBeTrue();
        (await new CreerNomenclatureHandler(_store, _store, _store, admin).HandleAsync(new CreerNomenclature("CP", libelle), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Conflict);

        var entree = new AjouterEntree("CP", "200", new LibellesDto("CP auxiliaire pour employés", "PC voor bedienden", "PK für Angestellte", null), new DateOnly(2026, 1, 1), null);
        (await new AjouterEntreeHandler(_store, _store, _store, admin).HandleAsync(entree, _ct)).IsSuccess.ShouldBeTrue();

        var dto = await new ObtenirNomenclatureHandler(_store).HandleAsync(new ObtenirNomenclature("cp", new DateOnly(2026, 6, 1), Language.Nl), _ct);
        dto.Value.Entrees.ShouldHaveSingleItem().Libelle.ShouldBe("PC voor bedienden");
        _store.Published.OfType<NomenclatureModifiee>().Count().ShouldBe(2);
    }
}
