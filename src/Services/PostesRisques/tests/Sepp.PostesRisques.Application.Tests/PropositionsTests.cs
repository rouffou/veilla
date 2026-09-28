using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.PostesRisques;
using Sepp.PostesRisques.Application.Postes;
using Sepp.PostesRisques.Domain;
using Sepp.PostesRisques.Domain.Postes;
using Sepp.PostesRisques.Domain.Risques;

using Shouldly;

namespace Sepp.PostesRisques.Application.Tests;

/// <summary>AFF-10, AFF-14, AFF-31 : catalogue de postes et workflow proposition → validation par le CPMT.</summary>
public class PropositionsTests
{
    internal static readonly DateTimeOffset Maintenant = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
    internal static readonly DateOnly Effet = new(2026, 10, 1);

    private readonly InMemoryStore _store = new();
    private readonly FixedClock _clock = new(Maintenant);
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Guid _affilie = Guid.CreateVersion7();

    private FakeUser Employeur { get; } = new("employeur-1", Roles.Employeur);

    private FakeUser Cpmt { get; } = new("cpmt-1", Roles.Cpmt);

    private FakePerimetre PerimetreEmployeur => new(true, _affilie);

    internal static Risque AjouterRisque(InMemoryStore store, string code, CategorieRisque categorie)
    {
        var risque = Risque.Creer(code, categorie, new LocalizedLabel($"{code} fr", $"{code} nl", $"{code} de"), "EXEMPLE");
        risque.DefinirRegle(TypeSurveillance.EvaluationSantePeriodique, 12, [], [], false, new DateOnly(2026, 1, 1));
        store.Risques.Add(risque);
        return risque;
    }

    private async Task<Guid> CreerPoste(ICurrentUser user, IPerimetreAffilies perimetre)
    {
        var result = await new CreerPosteHandler(_store, _store, user, perimetre)
            .HandleAsync(new CreerPoste(_affilie, "Cariste", "Conduite de chariots élévateurs", "EX.CARISTE"), _ct);
        return result.Value;
    }

    private Task<Result<Guid>> Proposer(Guid posteId, ICurrentUser user, IPerimetreAffilies perimetre, params LigneRisqueSaisie[] lignes) =>
        new SoumettrePropositionPosteRisqueHandler(_store, _store, _store, _store, user, perimetre, _clock)
            .HandleAsync(new SoumettrePropositionPosteRisque(posteId, "Nouveau chariot", Effet, lignes, null), _ct);

    private Task<Result<Unit>> Valider(Guid propositionId, ICurrentUser user, IPerimetreAffilies perimetre, DateOnly? dateAvis, Guid? document) =>
        new ValiderPropositionPosteRisqueHandler(_store, _store, _store, _store, user, perimetre, _clock)
            .HandleAsync(new ValiderPropositionPosteRisque(propositionId, dateAvis, document), _ct);

    [Fact]
    public async Task L_employeur_propose_et_le_cpmt_valide_avec_l_avis_du_comite()
    {
        AjouterRisque(_store, "EX.SECU.CONDUITE", CategorieRisque.Conduite);
        var posteId = await CreerPoste(Employeur, PerimetreEmployeur);

        var soumise = await Proposer(posteId, Employeur, PerimetreEmployeur, new LigneRisqueSaisie(TypeModification.Ajout, "ex.secu.conduite", NiveauExposition.Eleve));

        soumise.IsSuccess.ShouldBeTrue();
        var proposition = _store.Propositions.ShouldHaveSingleItem();
        proposition.Origine.ShouldBe(OrigineProposition.PortailEmployeur);
        proposition.ProposeePar.ShouldBe("employeur-1");
        _store.Postes.Single().Risques.ShouldBeEmpty();
        _store.Published.ShouldBeEmpty();

        var document = Guid.CreateVersion7();
        (await Valider(soumise.Value, Cpmt, FakePerimetre.Interne, new DateOnly(2026, 9, 20), document)).IsSuccess.ShouldBeTrue();

        proposition.Statut.ShouldBe(StatutProposition.Validee);
        proposition.DecidePar.ShouldBe("cpmt-1");
        var lien = _store.Postes.Single().RisquesAu(Effet).ShouldHaveSingleItem();
        lien.ValideParCpmtId.ShouldBe("cpmt-1");
        lien.DocumentAvisCpptId.ShouldBe(document);
        var evt = _store.Published.ShouldHaveSingleItem().ShouldBeOfType<ProfilRisquePosteModifie>();
        evt.PosteId.ShouldBe(posteId);
        evt.AffilieId.ShouldBe(_affilie);
        evt.CodesRisques.ShouldBe(["EX.SECU.CONDUITE"]);
        evt.ValideDu.ShouldBe(Effet);
    }

    [Theory]
    [InlineData(Roles.Employeur)]
    [InlineData(Roles.Sipp)]
    [InlineData(Roles.GestionnaireDossiers)]
    [InlineData(Roles.Infirmier)]
    [InlineData(Roles.ConseillerSecurite)]
    [InlineData(Roles.AdministrateurFonctionnel)]
    public async Task Un_non_cpmt_ne_peut_pas_valider_une_proposition(string role)
    {
        AjouterRisque(_store, "EX.SECU.CONDUITE", CategorieRisque.Conduite);
        var posteId = await CreerPoste(Employeur, PerimetreEmployeur);
        var soumise = await Proposer(posteId, Employeur, PerimetreEmployeur, new LigneRisqueSaisie(TypeModification.Ajout, "EX.SECU.CONDUITE", NiveauExposition.Eleve));

        var result = await Valider(soumise.Value, new FakeUser("autre", role), new FakePerimetre(true, _affilie), new DateOnly(2026, 9, 20), Guid.CreateVersion7());

        result.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        _store.Propositions.Single().Statut.ShouldBe(StatutProposition.Soumise);
        _store.Postes.Single().Risques.ShouldBeEmpty();
        _store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Le_cpmt_dirigeant_peut_valider()
    {
        AjouterRisque(_store, "EX.SECU.CONDUITE", CategorieRisque.Conduite);
        var posteId = await CreerPoste(Employeur, PerimetreEmployeur);
        var soumise = await Proposer(posteId, Employeur, PerimetreEmployeur, new LigneRisqueSaisie(TypeModification.Ajout, "EX.SECU.CONDUITE", NiveauExposition.Faible));

        (await Valider(soumise.Value, new FakeUser("dirigeant", Roles.CpmtDirigeant), FakePerimetre.Interne, new DateOnly(2026, 9, 1), Guid.CreateVersion7()))
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Valider_sans_avis_du_comite_ppt_est_refuse()
    {
        AjouterRisque(_store, "EX.SECU.CONDUITE", CategorieRisque.Conduite);
        var posteId = await CreerPoste(Employeur, PerimetreEmployeur);
        var soumise = await Proposer(posteId, Employeur, PerimetreEmployeur, new LigneRisqueSaisie(TypeModification.Ajout, "EX.SECU.CONDUITE", NiveauExposition.Eleve));

        var result = await Valider(soumise.Value, Cpmt, FakePerimetre.Interne, null, null);

        result.Error!.Kind.ShouldBe(ErrorKind.Validation);
        _store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task L_avis_joint_a_la_proposition_suffit_pour_valider()
    {
        AjouterRisque(_store, "EX.SECU.CONDUITE", CategorieRisque.Conduite);
        var posteId = await CreerPoste(Employeur, PerimetreEmployeur);
        var soumise = await Proposer(posteId, Employeur, PerimetreEmployeur, new LigneRisqueSaisie(TypeModification.Ajout, "EX.SECU.CONDUITE", NiveauExposition.Eleve));
        (await new JoindreAvisCpptHandler(_store, _store, Employeur, PerimetreEmployeur)
            .HandleAsync(new JoindreAvisCppt(soumise.Value, new DateOnly(2026, 9, 15), Guid.CreateVersion7()), _ct)).IsSuccess.ShouldBeTrue();

        (await Valider(soumise.Value, Cpmt, FakePerimetre.Interne, null, null)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Le_cpmt_refuse_et_l_historique_est_conserve()
    {
        AjouterRisque(_store, "EX.SECU.CONDUITE", CategorieRisque.Conduite);
        var posteId = await CreerPoste(Employeur, PerimetreEmployeur);
        var soumise = await Proposer(posteId, Employeur, PerimetreEmployeur, new LigneRisqueSaisie(TypeModification.Ajout, "EX.SECU.CONDUITE", NiveauExposition.Eleve));

        (await new RefuserPropositionPosteRisqueHandler(_store, _store, Employeur, _clock)
            .HandleAsync(new RefuserPropositionPosteRisque(soumise.Value, "non"), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new RefuserPropositionPosteRisqueHandler(_store, _store, Cpmt, _clock)
            .HandleAsync(new RefuserPropositionPosteRisque(soumise.Value, "Exposition non démontrée"), _ct)).IsSuccess.ShouldBeTrue();

        var historique = await new ListerPropositionsPosteRisqueHandler(_store, Employeur, PerimetreEmployeur)
            .HandleAsync(new ListerPropositionsPosteRisque(_affilie, posteId, null), _ct);
        historique.Value.ShouldHaveSingleItem().Statut.ShouldBe(StatutProposition.Refusee);
        _store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_employeur_hors_perimetre_ne_voit_ni_ne_modifie_le_poste()
    {
        var posteId = await CreerPoste(new FakeUser("gestionnaire", Roles.GestionnaireDossiers), FakePerimetre.Interne);
        var etranger = new FakePerimetre(true, Guid.CreateVersion7());

        (await new ObtenirPosteHandler(_store, Employeur, etranger).HandleAsync(new ObtenirPoste(posteId), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new ModifierPosteHandler(_store, _store, Employeur, etranger).HandleAsync(new ModifierPoste(posteId, "X", null, null), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new ListerPropositionsPosteRisqueHandler(_store, Employeur, etranger).HandleAsync(new ListerPropositionsPosteRisque(null, null, null), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task Le_cpmt_ne_peut_pas_modifier_le_catalogue_mais_peut_proposer()
    {
        AjouterRisque(_store, "EX.SECU.CONDUITE", CategorieRisque.Conduite);
        (await new CreerPosteHandler(_store, _store, Cpmt, FakePerimetre.Interne).HandleAsync(new CreerPoste(_affilie, "X", null, null), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);

        var posteId = await CreerPoste(Employeur, PerimetreEmployeur);
        var soumise = await Proposer(posteId, Cpmt, FakePerimetre.Interne, new LigneRisqueSaisie(TypeModification.Ajout, "EX.SECU.CONDUITE", NiveauExposition.Moyen));

        soumise.IsSuccess.ShouldBeTrue();
        _store.Propositions.Single().Origine.ShouldBe(OrigineProposition.Interne);
    }

    [Fact]
    public async Task Une_proposition_incoherente_ou_sur_un_risque_inconnu_est_refusee()
    {
        AjouterRisque(_store, "EX.SECU.CONDUITE", CategorieRisque.Conduite);
        var posteId = await CreerPoste(Employeur, PerimetreEmployeur);

        (await Proposer(posteId, Employeur, PerimetreEmployeur, new LigneRisqueSaisie(TypeModification.Retrait, "EX.SECU.CONDUITE", null)))
            .Error!.Code.ShouldBe("proposition.incoherente");
        (await Proposer(posteId, Employeur, PerimetreEmployeur, new LigneRisqueSaisie(TypeModification.Ajout, "EX.INCONNU", NiveauExposition.Moyen)))
            .Error!.Code.ShouldBe("risque.inconnu");
        _store.Propositions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_poste_expose_ne_peut_etre_archive_qu_apres_un_retrait_valide()
    {
        AjouterRisque(_store, "EX.SECU.CONDUITE", CategorieRisque.Conduite);
        var posteId = await CreerPoste(Employeur, PerimetreEmployeur);
        var ajout = await Proposer(posteId, Employeur, PerimetreEmployeur, new LigneRisqueSaisie(TypeModification.Ajout, "EX.SECU.CONDUITE", NiveauExposition.Moyen));
        await Valider(ajout.Value, Cpmt, FakePerimetre.Interne, new DateOnly(2026, 9, 20), Guid.CreateVersion7());
        var archiver = new ArchiverPosteHandler(_store, _store, Employeur, PerimetreEmployeur);

        (await archiver.HandleAsync(new ArchiverPoste(posteId, new DateOnly(2026, 11, 1)), _ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);

        var retrait = await new SoumettrePropositionPosteRisqueHandler(_store, _store, _store, _store, Employeur, PerimetreEmployeur, _clock)
            .HandleAsync(new SoumettrePropositionPosteRisque(posteId, "Fin d'activité", new DateOnly(2026, 11, 1),
                [new LigneRisqueSaisie(TypeModification.Retrait, "EX.SECU.CONDUITE", null)], new AvisCpptSaisie(new DateOnly(2026, 9, 25), Guid.CreateVersion7())), _ct);
        (await Valider(retrait.Value, Cpmt, FakePerimetre.Interne, null, null)).IsSuccess.ShouldBeTrue();

        (await archiver.HandleAsync(new ArchiverPoste(posteId, new DateOnly(2026, 11, 1)), _ct)).IsSuccess.ShouldBeTrue();
        _store.Published.OfType<ProfilRisquePosteModifie>().Last().CodesRisques.ShouldBeEmpty();
    }

    [Fact]
    public async Task Le_profil_de_risques_expose_la_regle_de_surveillance()
    {
        AjouterRisque(_store, "EX.SECU.CONDUITE", CategorieRisque.Conduite);
        var posteId = await CreerPoste(Employeur, PerimetreEmployeur);
        var ajout = await Proposer(posteId, Employeur, PerimetreEmployeur, new LigneRisqueSaisie(TypeModification.Ajout, "EX.SECU.CONDUITE", NiveauExposition.Moyen));
        await Valider(ajout.Value, Cpmt, FakePerimetre.Interne, new DateOnly(2026, 9, 20), Guid.CreateVersion7());

        var profil = await new ObtenirProfilRisquesHandler(_store, _store, Employeur, PerimetreEmployeur)
            .HandleAsync(new ObtenirProfilRisques(posteId, Effet, Language.Nl), _ct);

        var risque = profil.Value.Risques.ShouldHaveSingleItem();
        risque.Libelle.ShouldBe("EX.SECU.CONDUITE nl");
        risque.Regle!.FrequenceMois.ShouldBe(12);
    }
}
