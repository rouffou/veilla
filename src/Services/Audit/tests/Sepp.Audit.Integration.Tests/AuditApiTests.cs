using System.Net;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Audit.Adapters.Persistence;
using Sepp.Audit.Application.Journal;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Infrastructure.Messaging;

using Shouldly;

namespace Sepp.Audit.Integration.Tests;

/// <summary>Consommation des traces, consultation du journal et droits par zone (NF-04, PSY-20, §3.3).</summary>
public sealed class AuditApiTests : AuditServiceTestBase
{
    [Fact]
    public async Task Les_sondes_de_sante_repondent_sans_authentification()
    {
        var client = Factory.CreateClient();
        (await client.GetAsync("/health/live", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Une_requete_anonyme_est_refusee() =>
        (await Factory.CreateClient().GetAsync("/api/v1/audit/entrees", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Une_trace_recue_deux_fois_n_est_journalisee_qu_une_fois()
    {
        var acces = Acces();

        (await Recevoir(acces)).ShouldBe(1);
        (await Recevoir(acces)).ShouldBe(0);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        (await db.Entrees.CountAsync(e => e.EvenementSourceId == acces.EventId, Ct)).ShouldBe(1);
        (await db.InboxMessages.CountAsync(m => m.MessageId == acces.EventId, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Les_traces_recues_en_parallele_forment_une_chaine_continue()
    {
        await Task.WhenAll(Enumerable.Range(0, 30).Select(i => Recevoir(Acces(i % 3 == 0 ? "psychosociale" : "medicale"))));

        var medicale = await Client(Roles.Dpo).GetFromJsonAsync<IntegriteDto>("/api/v1/audit/integrite/medicale", Ct);
        var psy = await Client(Roles.Dpo).GetFromJsonAsync<IntegriteDto>("/api/v1/audit/integrite/psychosociale", Ct);

        medicale!.Integre.ShouldBeTrue();
        medicale.EntreesVerifiees.ShouldBe(20);
        psy!.Integre.ShouldBeTrue();
        psy.EntreesVerifiees.ShouldBe(10);
    }

    [Fact]
    public async Task Le_cpap_dirigeant_ne_voit_que_le_journal_psychosocial()
    {
        await RecevoirTous([Acces("standard"), Acces("medicale"), Acces("psychosociale")]);
        var client = Client(Roles.CpapDirigeant);

        var page = await client.GetFromJsonAsync<PageDto<EntreeAuditDto>>("/api/v1/audit/entrees", Ct);
        page!.Total.ShouldBe(1);
        page.Elements.ShouldHaveSingleItem().Zone.ShouldBe("psychosociale");

        (await client.GetAsync("/api/v1/audit/entrees?zone=medicale", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/v1/audit/integrite/medicale", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Le_cpmt_dirigeant_ne_voit_que_le_journal_medical_et_le_dpo_voit_tout()
    {
        await RecevoirTous([Acces("standard"), Acces("medicale"), Acces("psychosociale")]);

        var medical = await Client(Roles.CpmtDirigeant).GetFromJsonAsync<PageDto<EntreeAuditDto>>("/api/v1/audit/entrees", Ct);
        medical!.Elements.ShouldHaveSingleItem().Zone.ShouldBe("medicale");
        (await Client(Roles.CpmtDirigeant).GetAsync("/api/v1/audit/entrees?zone=psychosociale", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var tout = await Client(Roles.Dpo).GetFromJsonAsync<PageDto<EntreeAuditDto>>("/api/v1/audit/entrees", Ct);
        tout!.Total.ShouldBe(3);
    }

    [Theory]
    [InlineData(Roles.Cpmt)]
    [InlineData(Roles.Cpap)]
    [InlineData(Roles.AdministrateurFonctionnel)]
    [InlineData(Roles.Employeur)]
    public async Task Les_autres_profils_n_accedent_pas_au_journal(string role) =>
        (await Client(role).GetAsync("/api/v1/audit/entrees", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

    [Fact]
    public async Task La_recherche_filtre_par_utilisateur_objet_et_periode()
    {
        var objet = Guid.CreateVersion7();
        var hier = DateTimeOffset.UtcNow.AddDays(-1);
        await RecevoirTous(
        [
            Acces(utilisateur: "cpmt-1", objetId: objet, horodatage: hier.AddDays(-10)),
            Acces(utilisateur: "cpmt-1", objetId: objet, horodatage: hier),
            Acces(utilisateur: "cpmt-2", objetId: objet, horodatage: hier),
            Acces(utilisateur: "cpmt-1"),
        ]);
        var client = Client(Roles.Dpo);

        var parUtilisateur = await client.GetFromJsonAsync<PageDto<EntreeAuditDto>>("/api/v1/audit/entrees?utilisateurId=cpmt-1", Ct);
        parUtilisateur!.Total.ShouldBe(3);

        var du = Uri.EscapeDataString(hier.AddHours(-1).ToString("O"));
        var parObjetEtPeriode = await client.GetFromJsonAsync<PageDto<EntreeAuditDto>>(
            $"/api/v1/audit/entrees?objetType=dossier-sante&objetId={objet}&du={du}", Ct);
        parObjetEtPeriode!.Total.ShouldBe(2);

        var pagine = await client.GetFromJsonAsync<PageDto<EntreeAuditDto>>("/api/v1/audit/entrees?taille=2&page=2", Ct);
        pagine!.Elements.Count.ShouldBe(2);
        pagine.Total.ShouldBe(4);
    }

    [Fact]
    public async Task Un_bris_de_glace_est_journalise_signale_et_consultable_par_le_cpmt_dirigeant()
    {
        await RecevoirTous([Acces(), Acces(motif: "Remplacement du médecin du travail titulaire", brisDeGlace: true)]);

        var alertes = await Client(Roles.CpmtDirigeant).GetFromJsonAsync<PageDto<EntreeAuditDto>>("/api/v1/audit/bris-de-glace", Ct);
        var alerte = alertes!.Elements.ShouldHaveSingleItem();
        alerte.BrisDeGlace.ShouldBeTrue();
        alerte.Motif.ShouldBe("Remplacement du médecin du travail titulaire");

        (await Client(Roles.CpapDirigeant).GetFromJsonAsync<PageDto<EntreeAuditDto>>("/api/v1/audit/bris-de-glace", Ct))!.Total.ShouldBe(0);

        var publisher = (InMemoryMessagePublisher)Factory.Services.GetRequiredService<IMessagePublisher>();
        await Eventually(() => publisher.Published.Any(m => m.EventType == "audit.bris-de-glace-signale.v1" && m.Payload.Contains(alerte.Id.ToString())));
    }

    [Fact]
    public async Task Un_bris_de_glace_sans_motif_est_refuse_et_non_journalise()
    {
        await Should.ThrowAsync<DomainException>(() => Recevoir(Acces(brisDeGlace: true)));

        (await Client(Roles.Dpo).GetFromJsonAsync<PageDto<EntreeAuditDto>>("/api/v1/audit/entrees", Ct))!.Total.ShouldBe(0);
    }

    [Fact]
    public async Task Une_entree_se_consulte_par_son_identifiant_dans_le_perimetre()
    {
        await Recevoir(Acces("psychosociale"));
        var entree = (await Client(Roles.Dpo).GetFromJsonAsync<PageDto<EntreeAuditDto>>("/api/v1/audit/entrees", Ct))!.Elements.Single();

        (await Client(Roles.CpapDirigeant).GetFromJsonAsync<EntreeAuditDto>($"/api/v1/audit/entrees/{entree.Id}", Ct))!.Empreinte.ShouldBe(entree.Empreinte);
        (await Client(Roles.CpmtDirigeant).GetAsync($"/api/v1/audit/entrees/{entree.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
