using System.Text.Json;
using System.Text.Json.Nodes;

using Sepp.Affilies.Application.Affilies;
using Sepp.Affilies.Application.Concertation;
using Sepp.Affilies.Application.Groupes;
using Sepp.Affilies.Application.Hierarchie;
using Sepp.Affilies.Application.Historique;
using Sepp.Affilies.Application.Operations;
using Sepp.Affilies.Domain.Affilies;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Affilies;

using Shouldly;

namespace Sepp.Affilies.Application.Tests;

public class UseCasesTests
{
    private const string Bce = "0202.239.951";
    private static readonly FicheSaisie Fiche = new("Boulangerie Dupont", "SRL", "10.711", "118.03", CategorieTarifaire.B, Language.Fr, RegimeLinguistique.Francais);
    private static readonly AdresseDto Adresse = new("Rue de la Loi", "16", null, "1000", "Bruxelles");
    private static readonly ContactSaisi Marie = new("Marie Dupont", "RH", RoleContact.PersonneDeContact, "marie@dupont.be", null);

    private readonly InMemoryStore _store = new();
    private readonly HorlogeFixe _horloge = new(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private Contexte Gestionnaire => Pour(Roles.GestionnaireDossiers);

    private Contexte Pour(string role, params Guid[] affilies) => new(_store, new FakeUser([role], affilies), _horloge);

    private async Task<Guid> CreerAsync(string bce = Bce, string? seppOrigine = null)
    {
        var ctx = Gestionnaire;
        var result = await new CreerAffilieHandler(_store, _store, ctx.Acces, ctx.Modificateur, _store)
            .HandleAsync(new CreerAffilie(bce, Fiche, new DateOnly(2024, 1, 1), null, seppOrigine), _ct);
        result.IsSuccess.ShouldBeTrue(result.Error?.Message);
        return result.Value;
    }

    private Task<Result<Guid>> AjouterContactAsync(Contexte ctx, Guid affilieId) =>
        new AjouterContactHandler(ctx.Modificateur).HandleAsync(new AjouterContact(affilieId, Marie, new DateOnly(2025, 1, 1)), _ct);

    [Fact]
    public async Task Le_gestionnaire_affilie_un_employeur_et_publie_AffilieCree()
    {
        var id = await CreerAsync();

        var evt = _store.Published.ShouldHaveSingleItem().ShouldBeOfType<AffilieCree>();
        evt.AffilieId.ShouldBe(id);
        evt.NumeroBce.ShouldBe("0202239951");
        evt.CategorieTarifaire.ShouldBe("B");

        var creation = _store.Historique.ShouldHaveSingleItem();
        creation.Action.ShouldBe("affilie.cree");
        creation.NumeroVersion.ShouldBe(1);
        creation.Auteur.ShouldBe("test-user");
        creation.Avant.ShouldBeNull();
        JsonNode.Parse(creation.Apres!)!["fiche"]!["denomination"]!.GetValue<string>().ShouldBe("Boulangerie Dupont");
    }

    [Fact]
    public async Task Un_numero_BCE_deja_affilie_est_en_conflit()
    {
        await CreerAsync();
        var ctx = Gestionnaire;

        var result = await new CreerAffilieHandler(_store, _store, ctx.Acces, ctx.Modificateur, _store)
            .HandleAsync(new CreerAffilie("BE0202239951", Fiche, new DateOnly(2024, 1, 1), null, null), _ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public async Task Un_numero_BCE_invalide_est_une_erreur_de_validation()
    {
        var ctx = Gestionnaire;

        var result = await new CreerAffilieHandler(_store, _store, ctx.Acces, ctx.Modificateur, _store)
            .HandleAsync(new CreerAffilie("0202.239.952", Fiche, new DateOnly(2024, 1, 1), null, null), _ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Message.ShouldContain("modulo 97");
    }

    [Theory]
    [InlineData(Roles.Employeur)]
    [InlineData(Roles.Sipp)]
    [InlineData(Roles.Cpmt)]
    public async Task Seul_le_gestionnaire_affilie_un_employeur(string role)
    {
        var ctx = Pour(role, Guid.CreateVersion7());

        var result = await new CreerAffilieHandler(_store, _store, ctx.Acces, ctx.Modificateur, _store)
            .HandleAsync(new CreerAffilie(Bce, Fiche, new DateOnly(2024, 1, 1), null, null), _ct);

        result.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        _store.Affilies.ShouldBeEmpty();
    }

    [Fact]
    public async Task Modifier_la_fiche_historise_avant_et_apres_et_publie_AffilieModifie()
    {
        var id = await CreerAsync();
        _store.Published.Clear();

        var result = await new ModifierFicheHandler(Gestionnaire.Modificateur, _store)
            .HandleAsync(new ModifierFiche(id, Fiche with { Denomination = "Dupont & Fils", CategorieTarifaire = CategorieTarifaire.C }, null), _ct);

        result.IsSuccess.ShouldBeTrue();
        var evt = _store.Published.ShouldHaveSingleItem().ShouldBeOfType<AffilieModifie>();
        evt.CategorieTarifaire.ShouldBe("C");
        evt.Statut.ShouldBe("Actif");

        var modification = _store.Historique.Last();
        modification.Action.ShouldBe("fiche.modifiee");
        modification.NumeroVersion.ShouldBe(2);
        var avant = JsonNode.Parse(modification.Avant!)!.AsObject();
        var apres = JsonNode.Parse(modification.Apres!)!.AsObject();
        avant.Select(p => p.Key).ShouldBe(["fiche"]);
        avant["fiche"]!.AsObject().Select(p => p.Key).ShouldBe(["denomination", "categorieTarifaire"], ignoreOrder: true);
        avant["fiche"]!["denomination"]!.GetValue<string>().ShouldBe("Boulangerie Dupont");
        apres["fiche"]!["denomination"]!.GetValue<string>().ShouldBe("Dupont & Fils");
        apres["fiche"]!["categorieTarifaire"]!.GetValue<string>().ShouldBe("C");
    }

    [Fact]
    public async Task Une_modification_sans_changement_n_est_pas_historisee()
    {
        var id = await CreerAsync();
        var saves = _store.Saves;

        (await new ModifierFicheHandler(Gestionnaire.Modificateur, _store).HandleAsync(new ModifierFiche(id, Fiche, null), _ct)).IsSuccess.ShouldBeTrue();

        // La version de l'agrégat change mais l'instantané est identique : pas d'entrée ni de transaction.
        _store.Saves.ShouldBe(saves);
        _store.Historique.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Un_groupe_inconnu_est_refuse()
    {
        var id = await CreerAsync();

        var result = await new ModifierFicheHandler(Gestionnaire.Modificateur, _store).HandleAsync(new ModifierFiche(id, Fiche, Guid.CreateVersion7()), _ct);

        result.Error!.Code.ShouldBe("groupe.inconnu");
    }

    [Fact]
    public async Task Le_gestionnaire_rattache_l_affilie_a_un_groupe()
    {
        var id = await CreerAsync();
        var groupeId = (await new CreerGroupeHandler(_store, _store, Gestionnaire.Acces).HandleAsync(new CreerGroupe("Groupe Dupont"), _ct)).Value;

        (await new ModifierFicheHandler(Gestionnaire.Modificateur, _store).HandleAsync(new ModifierFiche(id, Fiche, groupeId), _ct)).IsSuccess.ShouldBeTrue();
        (await new RenommerGroupeHandler(_store, _store, Gestionnaire.Acces).HandleAsync(new RenommerGroupe(groupeId, "Dupont Holding"), _ct)).IsSuccess.ShouldBeTrue();

        _store.Affilies.Single().GroupeId.ShouldBe(groupeId);
        var groupes = await new ListerGroupesHandler(_store, Gestionnaire.Acces).HandleAsync(new ListerGroupes(), _ct);
        groupes.Value.ShouldHaveSingleItem().Nom.ShouldBe("Dupont Holding");
        (await new ListerGroupesHandler(_store, Pour(Roles.Employeur, id).Acces).HandleAsync(new ListerGroupes(), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new CreerGroupeHandler(_store, _store, Pour(Roles.Employeur, id).Acces).HandleAsync(new CreerGroupe("X"), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Theory]
    [InlineData(Roles.Employeur)]
    [InlineData(Roles.Sipp)]
    public async Task L_employeur_et_le_SIPP_gerent_les_contacts_de_leur_affilie(string role)
    {
        var id = await CreerAsync();
        _store.Published.Clear();

        var result = await AjouterContactAsync(Pour(role, id), id);

        result.IsSuccess.ShouldBeTrue();
        _store.Affilies.Single().Contacts.ShouldHaveSingleItem().Nom.ShouldBe("Marie Dupont");
        _store.Published.ShouldBeEmpty("Un contact ne modifie pas la fiche publiée (AffilieModifie).");
        var entree = _store.Historique.Last();
        entree.Action.ShouldBe("contact.ajoute");
        JsonNode.Parse(entree.Apres!)!["contacts"]!.AsArray().ShouldHaveSingleItem()!["email"]!.GetValue<string>().ShouldBe("marie@dupont.be");
        JsonNode.Parse(entree.Avant!)!["contacts"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task L_employeur_ne_modifie_pas_un_autre_affilie()
    {
        var id = await CreerAsync();

        var result = await AjouterContactAsync(Pour(Roles.Employeur, Guid.CreateVersion7()), id);

        result.Error!.Code.ShouldBe("affilie.hors-perimetre");
    }

    [Fact]
    public async Task Un_employeur_sans_claim_affilie_n_a_aucun_perimetre()
    {
        var id = await CreerAsync();

        (await AjouterContactAsync(Pour(Roles.Employeur), id)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new ObtenirAffilieHandler(_store, Pour(Roles.Employeur).Acces).HandleAsync(new ObtenirAffilie(id), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task L_employeur_ne_modifie_ni_la_fiche_ni_la_hierarchie()
    {
        var id = await CreerAsync();
        var employeur = Pour(Roles.Employeur, id);

        (await new ModifierFicheHandler(employeur.Modificateur, _store).HandleAsync(new ModifierFiche(id, Fiche, null), _ct))
            .Error!.Code.ShouldBe("affilie.reserve-gestionnaire");
        (await new AjouterUniteEtablissementHandler(employeur.Modificateur, _store)
                .HandleAsync(new AjouterUniteEtablissement(id, "2.123.456.791", "Siège", Adresse, Language.Fr, new DateOnly(2024, 1, 1)), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new ProjeterOperationHandler(employeur.Modificateur, _store)
                .HandleAsync(new ProjeterOperation(id, TypeOperation.TransfertSortant, new DateOnly(2027, 1, 1), null, null, "Autre SEPP"), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Un_conseiller_interne_lit_mais_ne_modifie_pas()
    {
        var id = await CreerAsync();
        var cpmt = Pour(Roles.Cpmt);

        (await new ObtenirAffilieHandler(_store, cpmt.Acces).HandleAsync(new ObtenirAffilie(id), _ct)).IsSuccess.ShouldBeTrue();
        (await AjouterContactAsync(cpmt, id)).Error!.Code.ShouldBe("affilie.ecriture-interdite");
    }

    [Fact]
    public async Task Un_role_interne_non_affilie_n_a_pas_de_lecture()
    {
        var id = await CreerAsync();

        (await new ObtenirAffilieHandler(_store, Pour(Roles.Dpo).Acces).HandleAsync(new ObtenirAffilie(id), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Le_SIPP_organise_la_concertation_de_son_affilie()
    {
        var id = await CreerAsync();
        var sipp = Pour(Roles.Sipp, id);

        var organeId = (await new InstallerOrganeHandler(sipp.Modificateur).HandleAsync(new InstallerOrgane(id, TypeOrgane.ComitePpt, new DateOnly(2024, 1, 1)), _ct)).Value;
        var reunionId = (await new PlanifierReunionHandler(sipp.Modificateur)
            .HandleAsync(new PlanifierReunion(id, organeId, new DateOnly(2025, 3, 12), null, false), _ct)).Value;
        var ordreDuJour = Guid.CreateVersion7();
        (await new ModifierReunionHandler(sipp.Modificateur)
            .HandleAsync(new ModifierReunion(id, organeId, reunionId, new DateOnly(2025, 3, 12), ordreDuJour, true), _ct)).IsSuccess.ShouldBeTrue();

        var reunion = _store.Affilies.Single().OrganesConcertation.Single().Reunions.Single();
        reunion.OrdreDuJourDocumentId.ShouldBe(ordreDuJour);
        reunion.ParticipationSepp.ShouldBeTrue();
        (await new DissoudreOrganeHandler(sipp.Modificateur).HandleAsync(new DissoudreOrgane(id, organeId, new DateOnly(2025, 1, 1)), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task Modifier_puis_terminer_un_contact_conserve_l_historique_des_lignes()
    {
        var id = await CreerAsync();
        var ctx = Gestionnaire;
        var contactId = (await AjouterContactAsync(ctx, id)).Value;

        var nouveauId = (await new ModifierContactHandler(ctx.Modificateur)
            .HandleAsync(new ModifierContact(id, contactId, Marie with { Role = RoleContact.ConseillerPreventionInterne }, new DateOnly(2025, 6, 1)), _ct)).Value;
        (await new TerminerContactHandler(ctx.Modificateur).HandleAsync(new TerminerContact(id, nouveauId, new DateOnly(2026, 1, 1)), _ct)).IsSuccess.ShouldBeTrue();

        var contacts = _store.Affilies.Single().Contacts;
        contacts.Count.ShouldBe(2);
        contacts.ShouldAllBe(c => !c.Validite.IsOpen);
        _store.Historique.Select(h => h.Action).ShouldBe(["affilie.cree", "contact.ajoute", "contact.modifie", "contact.termine"]);
        _store.Historique.Select(h => h.NumeroVersion).ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public async Task Un_element_inconnu_renvoie_introuvable()
    {
        var id = await CreerAsync();

        var result = await new TerminerContactHandler(Gestionnaire.Modificateur).HandleAsync(new TerminerContact(id, Guid.CreateVersion7(), new DateOnly(2026, 1, 1)), _ct);

        result.Error!.Kind.ShouldBe(ErrorKind.NotFound);
        (await new ObtenirAffilieHandler(_store, Gestionnaire.Acces).HandleAsync(new ObtenirAffilie(Guid.CreateVersion7()), _ct)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task La_hierarchie_est_construite_par_le_gestionnaire()
    {
        var id = await CreerAsync();
        var ctx = Gestionnaire;

        var uniteId = (await new AjouterUniteEtablissementHandler(ctx.Modificateur, _store)
            .HandleAsync(new AjouterUniteEtablissement(id, "2.123.456.791", "Siège", Adresse, Language.Fr, new DateOnly(2024, 1, 1)), _ct)).Value;
        var siteId = (await new AjouterSiteHandler(ctx.Modificateur)
            .HandleAsync(new AjouterSite(id, uniteId, "Atelier", Adresse, 50.84, 4.35, new DateOnly(2024, 1, 1)), _ct)).Value;
        var parentId = (await new AjouterDepartementHandler(ctx.Modificateur).HandleAsync(new AjouterDepartement(id, siteId, "Production", null, new DateOnly(2024, 1, 1)), _ct)).Value;
        var enfantId = (await new AjouterDepartementHandler(ctx.Modificateur).HandleAsync(new AjouterDepartement(id, siteId, "Four", parentId, new DateOnly(2024, 1, 1)), _ct)).Value;
        (await new ModifierDepartementHandler(ctx.Modificateur).HandleAsync(new ModifierDepartement(id, parentId, "Production", enfantId), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await new ModifierSiteHandler(ctx.Modificateur).HandleAsync(new ModifierSite(id, siteId, "Atelier central", Adresse, null, null), _ct)).IsSuccess.ShouldBeTrue();
        (await new ModifierUniteEtablissementHandler(ctx.Modificateur).HandleAsync(new ModifierUniteEtablissement(id, uniteId, "Siège social", Adresse, Language.Nl), _ct)).IsSuccess.ShouldBeTrue();
        (await new FermerDepartementHandler(ctx.Modificateur).HandleAsync(new FermerDepartement(id, enfantId, new DateOnly(2025, 1, 1)), _ct)).IsSuccess.ShouldBeTrue();
        (await new FermerSiteHandler(ctx.Modificateur).HandleAsync(new FermerSite(id, siteId, new DateOnly(2026, 1, 1)), _ct)).IsSuccess.ShouldBeTrue();
        (await new FermerUniteEtablissementHandler(ctx.Modificateur).HandleAsync(new FermerUniteEtablissement(id, uniteId, new DateOnly(2026, 1, 1)), _ct)).IsSuccess.ShouldBeTrue();

        var dto = (await new ObtenirAffilieHandler(_store, ctx.Acces).HandleAsync(new ObtenirAffilie(id), _ct)).Value;
        var unite = dto.UnitesEtablissement.ShouldHaveSingleItem();
        unite.Numero.ShouldBe("2123.456.791");
        unite.Nom.ShouldBe("Siège social");
        unite.ValideJusquAu.ShouldBe(new DateOnly(2026, 1, 1));
        unite.Sites.Single().Nom.ShouldBe("Atelier central");
        unite.Sites.Single().Departements.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Une_unite_d_etablissement_d_un_autre_affilie_est_en_conflit()
    {
        var premier = await CreerAsync();
        var second = await CreerAsync("0403.170.701");
        var ctx = Gestionnaire;
        var commande = (Guid id) => new AjouterUniteEtablissement(id, "2.123.456.791", "Siège", Adresse, Language.Fr, new DateOnly(2024, 1, 1));

        (await new AjouterUniteEtablissementHandler(ctx.Modificateur, _store).HandleAsync(commande(premier), _ct)).IsSuccess.ShouldBeTrue();
        (await new AjouterUniteEtablissementHandler(ctx.Modificateur, _store).HandleAsync(commande(second), _ct)).Error!.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public async Task La_recherche_d_un_employeur_est_limitee_a_son_perimetre()
    {
        var dupont = await CreerAsync();
        await CreerAsync("0403.170.701");

        var interne = await new RechercherAffiliesHandler(_store, Gestionnaire.Acces).HandleAsync(new RechercherAffilies(null, "dupont"), _ct);
        var externe = await new RechercherAffiliesHandler(_store, Pour(Roles.Employeur, dupont).Acces).HandleAsync(new RechercherAffilies(null, "DUPONT"), _ct);
        var parBce = await new RechercherAffiliesHandler(_store, Gestionnaire.Acces).HandleAsync(new RechercherAffilies("BE0403170701", null), _ct);

        interne.Value.Total.ShouldBe(2);
        externe.Value.Elements.ShouldHaveSingleItem().Id.ShouldBe(dupont);
        parBce.Value.Elements.ShouldHaveSingleItem().NumeroBce.ShouldBe("0403.170.701");
        (await new RechercherAffiliesHandler(_store, Gestionnaire.Acces).HandleAsync(new RechercherAffilies("123", null), _ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await new RechercherAffiliesHandler(_store, Gestionnaire.Acces).HandleAsync(new RechercherAffilies(null, null, 1, 1000), _ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task La_recherche_par_BCE_respecte_le_perimetre()
    {
        var id = await CreerAsync();

        (await new ObtenirAffilieParBceHandler(_store, Pour(Roles.Employeur, id).Acces).HandleAsync(new ObtenirAffilieParBce("0202239951"), _ct)).Value.Id.ShouldBe(id);
        (await new ObtenirAffilieParBceHandler(_store, Pour(Roles.Employeur, Guid.CreateVersion7()).Acces).HandleAsync(new ObtenirAffilieParBce(Bce), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new ObtenirAffilieParBceHandler(_store, Gestionnaire.Acces).HandleAsync(new ObtenirAffilieParBce("0403.170.701"), _ct)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Une_fusion_est_projetee_puis_realisee_a_sa_date_d_effet()
    {
        var absorbe = await CreerAsync();
        var absorbant = await CreerAsync("0403.170.701");
        var ctx = Gestionnaire;
        _store.Published.Clear();

        (await new ProjeterOperationHandler(ctx.Modificateur, _store)
                .HandleAsync(new ProjeterOperation(absorbe, TypeOperation.Fusion, new DateOnly(2026, 10, 1), Guid.CreateVersion7(), null, null), _ct))
            .Error!.Code.ShouldBe("operation.absorbant-inconnu");
        var operationId = (await new ProjeterOperationHandler(ctx.Modificateur, _store)
            .HandleAsync(new ProjeterOperation(absorbe, TypeOperation.Fusion, new DateOnly(2026, 10, 1), absorbant, null, null), _ct)).Value;

        var projetee = _store.Published.ShouldHaveSingleItem().ShouldBeOfType<OperationAffilieModifiee>();
        projetee.Statut.ShouldBe("Projetee");
        projetee.AffilieAbsorbantId.ShouldBe(absorbant);

        // 28/09/2026 : trop tôt.
        (await new RealiserOperationHandler(ctx.Modificateur).HandleAsync(new RealiserOperation(absorbe, operationId), _ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);

        _horloge.Maintenant = new DateTimeOffset(2026, 9, 30, 22, 30, 0, TimeSpan.Zero); // 1er octobre à Bruxelles
        _store.Published.Clear();
        (await new RealiserOperationHandler(ctx.Modificateur).HandleAsync(new RealiserOperation(absorbe, operationId), _ct)).IsSuccess.ShouldBeTrue();

        _store.Published.OfType<OperationAffilieModifiee>().ShouldHaveSingleItem().Statut.ShouldBe("Realisee");
        _store.Published.OfType<AffilieModifie>().ShouldHaveSingleItem().Statut.ShouldBe("Absorbe");
        _store.Affilies.Single(a => a.Id == absorbe).DateFin.ShouldBe(new DateOnly(2026, 9, 30));
    }

    [Fact]
    public async Task Une_scission_et_un_transfert_verifient_leurs_donnees()
    {
        var id = await CreerAsync();
        var ctx = Gestionnaire;
        var handler = new ProjeterOperationHandler(ctx.Modificateur, _store);

        (await handler.HandleAsync(new ProjeterOperation(id, TypeOperation.Scission, new DateOnly(2027, 1, 1), null, [Guid.CreateVersion7()], null), _ct))
            .Error!.Code.ShouldBe("operation.beneficiaire-inconnu");
        (await handler.HandleAsync(new ProjeterOperation(id, TypeOperation.TransfertSortant, new DateOnly(2027, 1, 1), null, null, null), _ct))
            .Error!.Code.ShouldBe("operation.incomplete");
        (await handler.HandleAsync(new ProjeterOperation(id, TypeOperation.TransfertEntrant, new DateOnly(2027, 1, 1), null, null, "X"), _ct))
            .Error!.Code.ShouldBe("operation.transfert-entrant");

        var transfertId = (await handler.HandleAsync(new ProjeterOperation(id, TypeOperation.TransfertSortant, new DateOnly(2027, 1, 1), null, null, "SEPP Exemple"), _ct)).Value;
        _store.Published.Clear();
        (await new AnnulerOperationHandler(ctx.Modificateur).HandleAsync(new AnnulerOperation(id, transfertId), _ct)).IsSuccess.ShouldBeTrue();

        var annulee = _store.Published.ShouldHaveSingleItem().ShouldBeOfType<OperationAffilieModifiee>();
        annulee.Statut.ShouldBe("Annulee");
        annulee.SeppContrepartie.ShouldBe("SEPP Exemple");
    }

    [Fact]
    public async Task Un_transfert_entrant_publie_son_operation_a_l_affiliation()
    {
        await CreerAsync(seppOrigine: "SEPP d'origine");

        _store.Published.Count.ShouldBe(2);
        _store.Published.OfType<OperationAffilieModifiee>().ShouldHaveSingleItem().TypeOperation.ShouldBe("TransfertEntrant");
    }

    [Fact]
    public async Task Resilier_puis_annuler_la_resiliation()
    {
        var id = await CreerAsync();
        var ctx = Gestionnaire;

        (await new ResilierAffiliationHandler(ctx.Modificateur).HandleAsync(new ResilierAffiliation(id, new DateOnly(2026, 12, 31)), _ct)).IsSuccess.ShouldBeTrue();
        _store.Published.OfType<AffilieModifie>().Last().Statut.ShouldBe("Resilie");
        (await new AnnulerResiliationHandler(ctx.Modificateur).HandleAsync(new AnnulerResiliation(id), _ct)).IsSuccess.ShouldBeTrue();
        _store.Published.OfType<AffilieModifie>().Last().Statut.ShouldBe("Actif");
    }

    [Fact]
    public async Task L_historique_est_consultable_dans_l_ordre_des_versions()
    {
        var id = await CreerAsync();
        await AjouterContactAsync(Gestionnaire, id);
        await new ResilierAffiliationHandler(Gestionnaire.Modificateur).HandleAsync(new ResilierAffiliation(id, new DateOnly(2026, 12, 31)), _ct);

        var historique = (await new ConsulterHistoriqueHandler(_store, _store, Pour(Roles.Employeur, id).Acces).HandleAsync(new ConsulterHistorique(id), _ct)).Value;

        historique.Select(h => h.Version).ShouldBe([1, 2, 3]);
        historique[0].Avant.ShouldBeNull();
        historique[2].Action.ShouldBe("affiliation.resiliee");
        historique[2].Avant!.Value.GetProperty("fiche").GetProperty("statut").GetString().ShouldBe("Actif");
        historique[2].Apres!.Value.GetProperty("fiche").GetProperty("dateFin").GetString().ShouldBe("2026-12-31");
        historique[2].Horodatage.ShouldBe(_horloge.Maintenant);
        (await new ConsulterHistoriqueHandler(_store, _store, Pour(Roles.Employeur, Guid.CreateVersion7()).Acces).HandleAsync(new ConsulterHistorique(id), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public void La_difference_compare_les_listes_par_identifiant()
    {
        var avant = JsonNode.Parse("""{"a":1,"liste":[{"id":"1","x":1},{"id":"2","x":2},{"id":"3","x":3}]}""");
        var apres = JsonNode.Parse("""{"a":1,"liste":[{"id":"1","x":1},{"id":"2","x":20},{"id":"4","x":4}]}""");

        var difference = Instantane.Difference(avant, apres)!.Value;

        difference.Avant!.ToJsonString().ShouldBe("""{"liste":[{"id":"2","x":2},{"id":"3","x":3}]}""");
        difference.Apres!.ToJsonString().ShouldBe("""{"liste":[{"id":"2","x":20},{"id":"4","x":4}]}""");
        Instantane.Difference(avant, avant!.DeepClone()).ShouldBeNull();
        JsonSerializer.Serialize(Instantane.Difference(JsonNode.Parse("[1,2]"), JsonNode.Parse("[1,3]"))!.Value.Apres).ShouldBe("[1,3]");
    }
}
