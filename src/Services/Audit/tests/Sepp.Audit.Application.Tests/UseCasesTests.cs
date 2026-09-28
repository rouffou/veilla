using Sepp.Audit.Application.Journal;
using Sepp.Audit.Domain.Journal;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Audit;

using Shouldly;

namespace Sepp.Audit.Application.Tests;

public class UseCasesTests
{
    private readonly InMemoryJournal _journal = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static AccesDonneeSensible Acces(string zone = "medicale", string? motif = null, bool brisDeGlace = false, string utilisateur = "cpmt-42") =>
        new("surveillance-medicale", zone, utilisateur, "cpmt", "lecture", "dossier-sante", Guid.CreateVersion7(), motif, brisDeGlace);

    private async Task Recevoir(params AccesDonneeSensible[] acces)
    {
        var handler = new EnregistrerAccesHandler(_journal, _journal);
        foreach (var a in acces)
        {
            await handler.HandleAsync(a, _ct);
        }
    }

    [Fact]
    public async Task Les_acces_recus_sont_chaines_par_zone()
    {
        await Recevoir(Acces("medicale"), Acces("psychosociale"), Acces("medicale"));

        _journal.Entrees.Where(e => e.Zone == Zone.Medicale).Select(e => e.Numero).ShouldBe([1L, 2L]);
        _journal.Entrees.Where(e => e.Zone == Zone.Psychosociale).Select(e => e.Numero).ShouldBe([1L]);
        var medicales = _journal.Entrees.Where(e => e.Zone == Zone.Medicale).ToList();
        medicales[1].EmpreintePrecedente.ShouldBe(medicales[0].Empreinte);
    }

    [Fact]
    public async Task Un_evenement_deja_journalise_n_est_pas_rechaine()
    {
        var acces = Acces();

        await Recevoir(acces, acces);

        _journal.Entrees.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Un_bris_de_glace_produit_une_alerte_sans_le_motif()
    {
        await Recevoir(Acces(motif: "Remplacement du médecin titulaire", brisDeGlace: true));

        var entree = _journal.Entrees.ShouldHaveSingleItem();
        var alerte = _journal.Evenements.ShouldHaveSingleItem().ShouldBeOfType<BrisDeGlaceSignale>();
        alerte.EntreeAuditId.ShouldBe(entree.Id);
        alerte.Zone.ShouldBe("medicale");
    }

    [Fact]
    public async Task Un_bris_de_glace_sans_motif_est_refuse()
    {
        await Should.ThrowAsync<DomainException>(() => Recevoir(Acces(brisDeGlace: true)));

        _journal.Entrees.ShouldBeEmpty();
        _journal.Evenements.ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_zone_inconnue_est_refusee() =>
        await Should.ThrowAsync<DomainException>(() => Recevoir(Acces(zone: "inconnue")));

    [Fact]
    public async Task Le_dpo_voit_toutes_les_zones()
    {
        await Recevoir(Acces("standard"), Acces("medicale"), Acces("psychosociale"));

        var page = (await new RechercherEntreesHandler(_journal, new FakeUser(Roles.Dpo)).HandleAsync(new RechercherEntrees(), _ct)).Value;

        page.Total.ShouldBe(3);
    }

    [Fact]
    public async Task Le_cpap_dirigeant_ne_voit_que_la_zone_psychosociale()
    {
        await Recevoir(Acces("standard"), Acces("medicale"), Acces("psychosociale"));
        var handler = new RechercherEntreesHandler(_journal, new FakeUser(Roles.CpapDirigeant));

        var page = (await handler.HandleAsync(new RechercherEntrees(), _ct)).Value;
        page.Elements.ShouldHaveSingleItem().Zone.ShouldBe("psychosociale");

        (await handler.HandleAsync(new RechercherEntrees(Zone: "medicale"), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Le_cpmt_dirigeant_ne_voit_que_la_zone_medicale()
    {
        await Recevoir(Acces("medicale"), Acces("psychosociale"));
        var handler = new RechercherEntreesHandler(_journal, new FakeUser(Roles.CpmtDirigeant));

        (await handler.HandleAsync(new RechercherEntrees(), _ct)).Value.Elements.ShouldHaveSingleItem().Zone.ShouldBe("medicale");
        (await handler.HandleAsync(new RechercherEntrees(Zone: "psychosociale"), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Theory]
    [InlineData(Roles.Cpmt)]
    [InlineData(Roles.Cpap)]
    [InlineData(Roles.AdministrateurFonctionnel)]
    public async Task Les_autres_roles_ne_consultent_pas_le_journal(string role) =>
        (await new RechercherEntreesHandler(_journal, new FakeUser(role)).HandleAsync(new RechercherEntrees(), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);

    [Fact]
    public async Task La_recherche_filtre_par_utilisateur_et_bris_de_glace()
    {
        await Recevoir(Acces(utilisateur: "a"), Acces(utilisateur: "b"), Acces(utilisateur: "b", motif: "Urgence", brisDeGlace: true));
        var handler = new RechercherEntreesHandler(_journal, new FakeUser(Roles.Dpo));

        (await handler.HandleAsync(new RechercherEntrees(UtilisateurId: "b"), _ct)).Value.Total.ShouldBe(2);
        (await handler.HandleAsync(new RechercherEntrees(BrisDeGlace: true), _ct)).Value.Elements.ShouldHaveSingleItem().Motif.ShouldBe("Urgence");
    }

    [Fact]
    public async Task Une_pagination_ou_une_periode_invalide_est_refusee()
    {
        var handler = new RechercherEntreesHandler(_journal, new FakeUser(Roles.Dpo));

        (await handler.HandleAsync(new RechercherEntrees(Taille: 1000), _ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await handler.HandleAsync(new RechercherEntrees(Du: DateTimeOffset.UtcNow, Au: DateTimeOffset.UtcNow.AddDays(-1)), _ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await handler.HandleAsync(new RechercherEntrees(Zone: "lune"), _ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task Une_entree_hors_perimetre_est_presentee_comme_inexistante()
    {
        await Recevoir(Acces("medicale"));
        var id = _journal.Entrees.Single().Id;

        (await new ObtenirEntreeHandler(_journal, new FakeUser(Roles.CpapDirigeant)).HandleAsync(new ObtenirEntree(id), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.NotFound);
        (await new ObtenirEntreeHandler(_journal, new FakeUser(Roles.CpmtDirigeant)).HandleAsync(new ObtenirEntree(id), _ct))
            .Value.Id.ShouldBe(id);
    }

    [Fact]
    public async Task L_integrite_d_une_chaine_intacte_est_confirmee()
    {
        await Recevoir(Acces(), Acces(), Acces());

        var integrite = (await new VerifierIntegriteHandler(_journal, new FakeUser(Roles.CpmtDirigeant)).HandleAsync(new VerifierIntegrite("medicale"), _ct)).Value;

        integrite.Integre.ShouldBeTrue();
        integrite.EntreesVerifiees.ShouldBe(3);
        integrite.DernierNumero.ShouldBe(3);
        integrite.DerniereEmpreinte.ShouldBe(_journal.Entrees[^1].Empreinte);
    }

    [Fact]
    public async Task Une_suppression_dans_la_chaine_est_signalee()
    {
        await Recevoir(Acces(), Acces(), Acces());
        _journal.Entrees.RemoveAt(1);

        var integrite = (await new VerifierIntegriteHandler(_journal, new FakeUser(Roles.Dpo)).HandleAsync(new VerifierIntegrite("medicale"), _ct)).Value;

        integrite.Integre.ShouldBeFalse();
        integrite.Anomalie!.Numero.ShouldBe(2);
        integrite.Anomalie.Type.ShouldBe(nameof(TypeAnomalie.EntreeManquante));
    }

    [Fact]
    public async Task La_verification_d_une_zone_non_visible_est_refusee() =>
        (await new VerifierIntegriteHandler(_journal, new FakeUser(Roles.CpapDirigeant)).HandleAsync(new VerifierIntegrite("medicale"), _ct))
            .Error!.Kind.ShouldBe(ErrorKind.Forbidden);

    [Fact]
    public async Task La_purge_respecte_la_conservation_minimale()
    {
        var maintenant = new DateTimeOffset(2036, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var handler = new PurgerJournalHandler(_journal, new FixedClock(maintenant));

        (await handler.HandleAsync(new PurgerJournal(5), _ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
        _journal.Purges.ShouldBeEmpty();

        (await handler.HandleAsync(new PurgerJournal(12), _ct)).IsSuccess.ShouldBeTrue();
        _journal.Purges.Count.ShouldBe(3);
        _journal.Purges.ShouldAllBe(p => p.Limite == maintenant.AddYears(-12));
    }
}
