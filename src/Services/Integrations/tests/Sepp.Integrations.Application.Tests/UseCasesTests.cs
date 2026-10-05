using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Affilies;
using Sepp.Contracts.Integrations;
using Sepp.Integrations.Application.Correspondances;
using Sepp.Integrations.Application.Externe;
using Sepp.Integrations.Application.Flux;
using Sepp.Integrations.Domain;
using Sepp.Integrations.Domain.Bce;
using Sepp.Integrations.Domain.Correspondances;
using Sepp.Integrations.Domain.Flux;

using Shouldly;

namespace Sepp.Integrations.Application.Tests;

/// <summary>Assemble la couche application avec des adaptateurs en mémoire.</summary>
internal sealed class Contexte
{
    public const string Employeur = "0202239951";
    public const string Niss = "85073000123";

    public Contexte(params string[] roles)
    {
        Utilisateur = new FakeUser(roles.Length == 0 ? [Roles.GestionnaireDossiers] : roles);
        Registre = new RegistreCorrespondances(Store);
        Traitement = new TraitementEchanges(Store, Registre, Store, RegistreNational, Personnes, Store, Store, Horloge);
        Execution = new ExecutionFlux(Store, Store, Store, Bce, Dimona, RegistreNational, Traitement, Store, Horloge);
        RegistreNational.Identites[Niss] = Identites.Dupont;
    }

    public InMemoryStore Store { get; } = new();

    public FakeClock Horloge { get; } = new(new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero));

    public FakeBce Bce { get; } = new();

    public FakeDimona Dimona { get; } = new();

    public FakeRegistreNational RegistreNational { get; } = new();

    public FakePersonnes Personnes { get; } = new();

    public FakeUser Utilisateur { get; }

    public RegistreCorrespondances Registre { get; }

    public TraitementEchanges Traitement { get; }

    public ExecutionFlux Execution { get; }

    public Guid AffilieConnu(string numeroBce = Employeur)
    {
        var id = Guid.CreateVersion7();
        Store.Correspondances.Add(CorrespondanceIdentifiant.Creer(TypeIdentifiantExterne.NumeroBce, numeroBce, TypeObjetInterne.Affilie, id));
        return id;
    }

    public static DeclarationDimona Entree(string reference = "DIM-001", string employeur = Employeur, string niss = Niss) =>
        new(reference, TypeDeclarationDimona.Entree, niss, employeur, null, TypeTravailleur.Salarie, TypeContrat.DureeIndeterminee, new DateOnly(2026, 9, 1), null);

    public static DeclarationDimona Sortie(string reference = "DIM-001") =>
        Entree(reference) with { Type = TypeDeclarationDimona.Sortie, DateFin = new DateOnly(2026, 12, 31) };

    public Task<Result<EchangeFluxDto>> RelancerAsync(Guid id) =>
        new RelancerEchangeHandler(Store, Traitement, Utilisateur).HandleAsync(new RelancerEchange(id), CancellationToken.None);
}

public sealed class FluxDimonaTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Une_entree_dimona_d_un_affilie_connu_alimente_personnes_et_la_correspondance()
    {
        var ctx = new Contexte();
        var affilie = ctx.AffilieConnu();
        ctx.Dimona.Declarations.Add(Contexte.Entree());

        var rapport = await ctx.Execution.ExecuterAsync(TypeFlux.Dimona, _ct);

        rapport.ShouldBe(new RapportExecutionDto(TypeFlux.Dimona, 1, 0, 1, 0, 0, 0, null));
        var appel = ctx.Personnes.Entrees.ShouldHaveSingleItem();
        appel.AffilieId.ShouldBe(affilie);
        appel.Niss.ShouldBe(Contexte.Niss);
        appel.Identite.ShouldBe(Identites.Dupont);
        var echange = ctx.Store.Journal.ShouldHaveSingleItem();
        echange.Statut.ShouldBe(StatutEchange.Traite);
        echange.ReferenceExterne.ShouldBe("DIM-001");
        echange.CleIdempotence.ShouldNotContain(Contexte.Niss);
        var correspondance = ctx.Store.Correspondances.Single(c => c.TypeExterne == TypeIdentifiantExterne.ReferenceDimona);
        correspondance.IdentifiantInterne.ShouldBe(ctx.Personnes.Occupation("DIM-001").OccupationId);
        ctx.Store.Positions.ShouldHaveSingleItem().Position.ShouldBe("pos-1");
    }

    [Fact]
    public async Task Une_declaration_deja_recue_n_est_ni_rejournalisee_ni_retraitee()
    {
        var ctx = new Contexte();
        ctx.AffilieConnu();
        ctx.Dimona.Declarations.AddRange([Contexte.Entree(), Contexte.Entree(), Contexte.Sortie()]);

        var premier = await ctx.Execution.ExecuterAsync(TypeFlux.Dimona, _ct);
        var second = await ctx.Execution.ExecuterAsync(TypeFlux.Dimona, _ct);

        premier.Recus.ShouldBe(2);
        premier.DejaRecus.ShouldBe(1);
        premier.Traites.ShouldBe(2);
        second.Recus.ShouldBe(0);
        second.DejaRecus.ShouldBe(3);
        ctx.Store.Journal.Count.ShouldBe(2);
        ctx.Personnes.Entrees.Count.ShouldBe(1);
        ctx.Personnes.Sorties.ShouldHaveSingleItem().ShouldBe(("DIM-001", new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public async Task Un_employeur_non_affilie_est_rejete_sans_appel_et_sans_niss_dans_le_message()
    {
        var ctx = new Contexte();
        ctx.Dimona.Declarations.Add(Contexte.Entree());

        var rapport = await ctx.Execution.ExecuterAsync(TypeFlux.Dimona, _ct);

        rapport.Rejetes.ShouldBe(1);
        ctx.Personnes.Entrees.ShouldBeEmpty();
        var echange = ctx.Store.Journal.Single();
        echange.Statut.ShouldBe(StatutEchange.Rejete);
        echange.CodeErreur.ShouldBe("integrations.employeur-inconnu");
        echange.MessageErreur!.ShouldContain("0202.239.951");
        echange.MessageErreur!.ShouldNotContain(Contexte.Niss);
        echange.ChargeUtileDisponible.ShouldBeTrue();
    }

    [Fact]
    public async Task Un_rejet_corrige_est_rejoue_par_la_relance_manuelle()
    {
        var ctx = new Contexte();
        ctx.Dimona.Declarations.Add(Contexte.Entree());
        await ctx.Execution.ExecuterAsync(TypeFlux.Dimona, _ct);
        var echange = ctx.Store.Journal.Single();

        // L'employeur est affilié entre-temps (événement AffilieCree) : la relance aboutit.
        await new AffilieCreeHandler(ctx.Registre, ctx.Store).HandleAsync(new AffilieCree(Guid.CreateVersion7(), Contexte.Employeur, "A"), _ct);
        var relance = await ctx.RelancerAsync(echange.Id);

        relance.Value.Statut.ShouldBe(StatutEchange.Traite);
        relance.Value.Tentatives.ShouldBe(2);
        relance.Value.CodeErreur.ShouldBeNull();
        ctx.Personnes.Entrees.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Une_erreur_technique_est_relancable_et_la_relance_est_idempotente()
    {
        var ctx = new Contexte();
        ctx.AffilieConnu();
        ctx.Dimona.Declarations.Add(Contexte.Entree());
        var pannes = 1;
        ctx.Personnes.ReponseEntree = () => pannes-- > 0 ? ResultatAppel<OccupationDimona>.Erreur("integrations.personnes-indisponible", "Indisponible.") : null;

        var rapport = await ctx.Execution.ExecuterAsync(TypeFlux.Dimona, _ct);
        var echange = ctx.Store.Journal.Single();
        rapport.EnErreur.ShouldBe(1);
        echange.Statut.ShouldBe(StatutEchange.EnErreur);

        // Une nouvelle exécution du flux ne relance pas automatiquement un échange en erreur (relance manuelle, INT-02).
        (await ctx.Execution.ExecuterAsync(TypeFlux.Dimona, _ct)).Traites.ShouldBe(0);

        (await ctx.RelancerAsync(echange.Id)).Value.Statut.ShouldBe(StatutEchange.Traite);
        var rejeu = await ctx.RelancerAsync(echange.Id);

        rejeu.Value.Statut.ShouldBe(StatutEchange.Traite);
        rejeu.Value.Tentatives.ShouldBe(2);
        ctx.Personnes.Entrees.Count.ShouldBe(2);
        ctx.Store.Correspondances.Count(c => c.TypeExterne == TypeIdentifiantExterne.ReferenceDimona).ShouldBe(1);
    }

    [Fact]
    public async Task Un_message_d_erreur_du_service_destinataire_est_masque()
    {
        var ctx = new Contexte();
        ctx.AffilieConnu();
        ctx.Dimona.Declarations.Add(Contexte.Entree());
        ctx.Personnes.ReponseEntree = () => ResultatAppel<OccupationDimona>.Rejet("personne.invalide", $"NISS {Contexte.Niss} refusé");

        await ctx.Execution.ExecuterAsync(TypeFlux.Dimona, _ct);

        var echange = ctx.Store.Journal.Single();
        echange.Statut.ShouldBe(StatutEchange.Rejete);
        echange.MessageErreur.ShouldBe("NISS *********** refusé");
    }

    [Fact]
    public async Task Une_sortie_recue_avant_son_entree_reste_en_erreur_puis_aboutit()
    {
        var ctx = new Contexte();
        ctx.AffilieConnu();
        ctx.Dimona.Declarations.Add(Contexte.Sortie());
        await ctx.Execution.ExecuterAsync(TypeFlux.Dimona, _ct);
        var sortie = ctx.Store.Journal.Single();
        sortie.Statut.ShouldBe(StatutEchange.EnErreur);

        ctx.Dimona.Declarations.Add(Contexte.Entree());
        await ctx.Execution.ExecuterAsync(TypeFlux.Dimona, _ct);

        (await ctx.RelancerAsync(sortie.Id)).Value.Statut.ShouldBe(StatutEchange.Traite);
    }

    [Fact]
    public async Task Une_identite_inconnue_du_registre_national_est_rejetee()
    {
        var ctx = new Contexte();
        ctx.AffilieConnu();
        ctx.Dimona.Declarations.Add(Contexte.Entree(niss: "85073000224"));

        await ctx.Execution.ExecuterAsync(TypeFlux.Dimona, _ct);

        ctx.Store.Journal.Single().CodeErreur.ShouldBe("integrations.identite-introuvable");
        ctx.Personnes.Entrees.ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_reference_illisible_est_journalisee_puis_rejetee_sans_bloquer_le_lot()
    {
        var ctx = new Contexte();
        ctx.AffilieConnu();
        ctx.Dimona.Declarations.AddRange([Contexte.Entree(reference: " "), Contexte.Entree(reference: "DIM-002")]);

        var rapport = await ctx.Execution.ExecuterAsync(TypeFlux.Dimona, _ct);

        rapport.Rejetes.ShouldBe(1);
        rapport.Traites.ShouldBe(1);
        ctx.Store.Journal.Single(e => e.Statut == StatutEchange.Rejete).ReferenceExterne.ShouldBeNull();
    }

    [Fact]
    public async Task Une_panne_de_l_organisme_est_rapportee_sans_avancer_la_position()
    {
        var ctx = new Contexte();
        ctx.Dimona.Panne = new TimeoutException("délai");

        var rapport = await ctx.Execution.ExecuterAsync(TypeFlux.Dimona, _ct);

        rapport.ErreurRecuperation.ShouldBe("Récupération impossible (TimeoutException).");
        ctx.Store.Positions.ShouldBeEmpty();
        ctx.Store.Journal.ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_mutation_du_registre_national_met_a_jour_l_identite_dans_personnes()
    {
        var ctx = new Contexte();
        ctx.RegistreNational.Mutations.Add(new MutationRegistreNational("RN-1", Contexte.Niss, new DateOnly(2026, 9, 1), Identites.Dupont with { Nom = "Martin" }));

        var rapport = await ctx.Execution.ExecuterAsync(TypeFlux.RegistreNational, _ct);

        rapport.Traites.ShouldBe(1);
        ctx.Personnes.Mutations.ShouldHaveSingleItem().ShouldBe(Contexte.Niss);
        ctx.Store.Journal.Single().CleIdempotence.ShouldBe("mutation:RN-1");
    }
}

public sealed class FluxBceTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static DonneesEntreprise Donnees(DateOnly extraction, string denomination = "ACME") => new(
        Contexte.Employeur, denomination, "SRL", "62.010", extraction,
        [new DonneesUniteEtablissement(NumerosBce.AvecControle("20223995"), "Siège", new AdresseBce("Rue de la Loi", "16", null, "1000", "Bruxelles"), null)]);

    [Fact]
    public async Task Une_consultation_bce_publie_les_donnees_vers_affilies_une_seule_fois()
    {
        var ctx = new Contexte();
        var affilie = ctx.AffilieConnu();
        ctx.Bce.Entreprises[Contexte.Employeur] = Donnees(new DateOnly(2026, 10, 5));

        var premiere = await ctx.Execution.ConsulterBceAsync("0202.239.951", _ct);
        var seconde = await ctx.Execution.ConsulterBceAsync("0202239951", _ct);

        premiere.Value.Statut.ShouldBe(StatutEchange.Traite);
        premiere.Value.NombreEnregistrements.ShouldBe(2);
        seconde.Value.Id.ShouldBe(premiere.Value.Id);
        var evenement = ctx.Store.Published.OfType<DonneesBceRecues>().ShouldHaveSingleItem();
        evenement.AffilieId.ShouldBe(affilie);
        evenement.Denomination.ShouldBe("ACME");
        evenement.NumerosUnitesEtablissement.ShouldBe([NumerosBce.AvecControle("20223995")]);
    }

    [Fact]
    public async Task Une_extraction_ulterieure_identique_est_journalisee_sans_publier_et_un_changement_publie()
    {
        var ctx = new Contexte();
        ctx.Bce.Entreprises[Contexte.Employeur] = Donnees(new DateOnly(2026, 10, 5));
        await ctx.Execution.ConsulterBceAsync(Contexte.Employeur, _ct);

        ctx.Bce.Entreprises[Contexte.Employeur] = Donnees(new DateOnly(2026, 10, 6));
        await ctx.Execution.ConsulterBceAsync(Contexte.Employeur, _ct);
        ctx.Bce.Entreprises[Contexte.Employeur] = Donnees(new DateOnly(2026, 10, 7), "ACME Belgium");
        await ctx.Execution.ConsulterBceAsync(Contexte.Employeur, _ct);

        ctx.Store.Journal.Count.ShouldBe(3);
        ctx.Store.Journal.ShouldAllBe(e => e.Statut == StatutEchange.Traite);
        ctx.Store.Published.OfType<DonneesBceRecues>().Select(e => e.Denomination).ShouldBe(["ACME", "ACME Belgium"]);
        ctx.Store.Published.OfType<DonneesBceRecues>().First().AffilieId.ShouldBeNull();
    }

    [Fact]
    public async Task Une_entreprise_inconnue_ou_un_numero_invalide_est_signale()
    {
        var ctx = new Contexte();

        (await ctx.Execution.ConsulterBceAsync(Contexte.Employeur, _ct)).Error!.Kind.ShouldBe(ErrorKind.NotFound);
        (await ctx.Execution.ConsulterBceAsync("0202239952", _ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
        ctx.Store.Journal.ShouldBeEmpty();
    }

    [Fact]
    public async Task Des_donnees_bce_invalides_sont_rejetees()
    {
        var ctx = new Contexte();
        ctx.Bce.Entreprises[Contexte.Employeur] = Donnees(new DateOnly(2026, 10, 5)) with { Denomination = " " };

        var echange = await ctx.Execution.ConsulterBceAsync(Contexte.Employeur, _ct);

        echange.Value.Statut.ShouldBe(StatutEchange.Rejete);
        echange.Value.CodeErreur.ShouldBe("integrations.donnees-invalides");
        ctx.Store.Entreprises.ShouldBeEmpty();
        ctx.Store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task Le_flux_bce_planifie_actualise_tous_les_affilies_connus()
    {
        var ctx = new Contexte();
        ctx.AffilieConnu();
        ctx.AffilieConnu("0403170701");
        ctx.Bce.Entreprises[Contexte.Employeur] = Donnees(new DateOnly(2026, 10, 5));

        var rapport = await ctx.Execution.ExecuterAsync(TypeFlux.Bce, _ct);

        rapport.Recus.ShouldBe(1);
        rapport.Traites.ShouldBe(1);
        rapport.Inconnus.ShouldBe(1);
    }
}

public sealed class SuiviEtAdministrationTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Le_suivi_des_flux_est_reserve_au_gestionnaire_et_a_l_administrateur_fonctionnel()
    {
        foreach (var role in new[] { Roles.Cpmt, Roles.Employeur, Roles.Integrations })
        {
            var ctx = new Contexte(role);
            (await new ListerJournalHandler(ctx.Store, ctx.Utilisateur).HandleAsync(new ListerJournal(null, null, null, null), _ct))
                .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
            (await new LancerFluxHandler(ctx.Execution, ctx.Utilisateur).HandleAsync(new LancerFlux(TypeFlux.Dimona), _ct))
                .Error!.Kind.ShouldBe(ErrorKind.Forbidden);
            (await ctx.RelancerAsync(Guid.CreateVersion7())).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        }

        var admin = new Contexte(Roles.AdministrateurFonctionnel);
        (await new ListerJournalHandler(admin.Store, admin.Utilisateur).HandleAsync(new ListerJournal(null, null, null, null), _ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Les_volumes_sont_comptes_par_flux_et_par_jour()
    {
        var ctx = new Contexte();
        ctx.AffilieConnu();
        ctx.Dimona.Declarations.AddRange([Contexte.Entree(), Contexte.Entree("DIM-002", employeur: "0403170701")]);
        await ctx.Execution.ExecuterAsync(TypeFlux.Dimona, _ct);
        ctx.Horloge.Maintenant = ctx.Horloge.Maintenant.AddDays(1);
        ctx.Dimona.Declarations.Add(Contexte.Sortie());
        await ctx.Execution.ExecuterAsync(TypeFlux.Dimona, _ct);

        var volumes = (await new ObtenirVolumesHandler(ctx.Store, ctx.Utilisateur, ctx.Horloge).HandleAsync(new ObtenirVolumes(null, null), _ct)).Value;

        var total = volumes.Totaux.ShouldHaveSingleItem();
        total.ShouldBe(new VolumeFluxDto(TypeFlux.Dimona, SensFlux.Entrant, null, 3, 0, 2, 1, 0, 3));
        volumes.ParJour.Select(v => (v.Jour, v.Echanges)).ShouldBe([(new DateOnly(2026, 10, 5), 2), (new DateOnly(2026, 10, 6), 1)]);
        (await new ObtenirVolumesHandler(ctx.Store, ctx.Utilisateur, ctx.Horloge).HandleAsync(new ObtenirVolumes(new DateOnly(2026, 1, 1), new DateOnly(2027, 6, 1)), _ct))
            .Error!.Code.ShouldBe("periode.trop-longue");
    }

    [Fact]
    public async Task Le_journal_filtre_les_erreurs_et_rejets()
    {
        var ctx = new Contexte();
        ctx.AffilieConnu();
        ctx.Dimona.Declarations.AddRange([Contexte.Entree(), Contexte.Entree("DIM-002", employeur: "0403170701")]);
        await ctx.Execution.ExecuterAsync(TypeFlux.Dimona, _ct);

        var erreurs = await new ListerJournalHandler(ctx.Store, ctx.Utilisateur).HandleAsync(
            new ListerJournal(TypeFlux.Dimona, new HashSet<StatutEchange> { StatutEchange.Rejete, StatutEchange.EnErreur }, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5)), _ct);

        erreurs.Value.Total.ShouldBe(1);
        erreurs.Value.Elements.Single().ReferenceExterne.ShouldBe("DIM-002");
    }

    [Fact]
    public async Task La_purge_efface_les_charges_utiles_echues_et_empeche_la_relance()
    {
        var ctx = new Contexte();
        ctx.AffilieConnu();
        ctx.Dimona.Declarations.AddRange([Contexte.Entree(), Contexte.Entree("DIM-002", employeur: "0403170701")]);
        await ctx.Execution.ExecuterAsync(TypeFlux.Dimona, _ct);
        var purge = new PurgeChargesUtiles(ctx.Store, ctx.Store, new ConservationChargesUtiles(), ctx.Horloge);

        (await purge.ExecuterAsync(_ct)).ShouldBe(0);
        ctx.Horloge.Maintenant = ctx.Horloge.Maintenant.AddDays(2);
        (await purge.ExecuterAsync(_ct)).ShouldBe(1);
        ctx.Store.Journal.Single(e => e.Statut == StatutEchange.Traite).ChargeUtile.ShouldBeNull();
        var rejete = ctx.Store.Journal.Single(e => e.Statut == StatutEchange.Rejete);
        rejete.ChargeUtileDisponible.ShouldBeTrue();

        ctx.Horloge.Maintenant = ctx.Horloge.Maintenant.AddDays(30);
        (await purge.ExecuterAsync(_ct)).ShouldBe(1);
        (await ctx.RelancerAsync(rejete.Id)).Error!.Code.ShouldBe("echange.charge-utile-purgee");
    }

    [Fact]
    public async Task Les_evenements_affilies_tiennent_la_correspondance_bce_a_jour()
    {
        var ctx = new Contexte();
        var premier = Guid.CreateVersion7();
        var repreneur = Guid.CreateVersion7();

        await new AffilieCreeHandler(ctx.Registre, ctx.Store).HandleAsync(new AffilieCree(premier, Contexte.Employeur, "A"), _ct);
        await new AffilieCreeHandler(ctx.Registre, ctx.Store).HandleAsync(new AffilieCree(premier, Contexte.Employeur, "A"), _ct);
        await new AffilieModifieHandler(ctx.Registre, ctx.Store).HandleAsync(new AffilieModifie(repreneur, Contexte.Employeur, "B", "Actif"), _ct);

        ctx.Store.Correspondances.ShouldHaveSingleItem().IdentifiantInterne.ShouldBe(repreneur);
        var liste = await new ListerCorrespondancesHandler(ctx.Store, ctx.Utilisateur)
            .HandleAsync(new ListerCorrespondances(TypeIdentifiantExterne.NumeroBce, "0202.239.951", null), _ct);
        liste.Value.ShouldHaveSingleItem().IdentifiantInterne.ShouldBe(repreneur);
    }

    [Fact]
    public async Task Une_correspondance_saisie_est_validee()
    {
        var ctx = new Contexte();
        var handler = new DefinirCorrespondanceHandler(ctx.Registre, ctx.Store, ctx.Utilisateur);

        (await handler.HandleAsync(new DefinirCorrespondance(TypeIdentifiantExterne.NumeroBce, "0202239952", TypeObjetInterne.Affilie, Guid.CreateVersion7()), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await handler.HandleAsync(new DefinirCorrespondance(TypeIdentifiantExterne.NumeroBce, Contexte.Employeur, TypeObjetInterne.Affilie, Guid.CreateVersion7()), _ct))
            .IsSuccess.ShouldBeTrue();
    }
}
