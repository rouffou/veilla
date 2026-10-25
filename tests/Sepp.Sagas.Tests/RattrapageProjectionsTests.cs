using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Affilies.Adapters.Persistence;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Adapters.Persistence;
using Sepp.Documents.Domain.Langues;
using Sepp.Sagas.Tests.Plateforme;

using Shouldly;

namespace Sepp.Sagas.Tests;

/// <summary>
/// Rattrapage des projections de bout en bout (ADR 0011, ARC-31, ARC-34, NF-41) : des affiliés et des personnes existent avant
/// l'abonnement des consommateurs ; l'exploitation republie l'état courant de Personnes et d'Affiliés ; la projection
/// linguistique de Documents et les occupations projetées d'Affiliés se remplissent ; un rejeu ne crée aucun doublon et un
/// instantané livré en retard n'écrase pas un état plus récent.
/// </summary>
public sealed class RattrapageProjectionsTests(PlateformeRattrapage plateforme) : IClassFixture<PlateformeRattrapage>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task La_republication_remplit_les_projections_sans_doublon_ni_ecrasement_d_un_etat_plus_recent()
    {
        // 1. État antérieur à la mise en service : les événements v1 sont publiés mais jamais livrés aux consommateurs.
        var affilie = await CreerAffilieAsync();
        var (personneNl, occupation) = await CreerPersonneAsync(affilie, 101, "Nl");
        var (personneDe, _) = await CreerPersonneAsync(affilie, 102, "De");
        await plateforme.PublierSansLivrerAsync(Ct);
        (await ContextesAsync(affilie, personneNl, personneDe)).ShouldBeEmpty();
        (await OccupationProjeteeAsync(occupation)).ShouldBeNull();

        // 2. L'exploitation republie l'état courant des deux producteurs.
        await RepublierAsync(plateforme.Affilies, "affilies");
        await RepublierAsync(plateforme.Personnes, "personnes", "occupations");

        var contextes = await ContextesAsync(affilie, personneNl, personneDe);
        contextes.Count.ShouldBe(3);
        var contexteAffilie = contextes.Single(c => c.SujetId == affilie);
        contexteAffilie.Regime.ShouldBe(RegimeLinguistique.Neerlandais);
        contexteAffilie.Langue.ShouldBe(Language.Nl);
        contextes.Single(c => c.SujetId == personneNl).Langue.ShouldBe(Language.Nl);
        contextes.Single(c => c.SujetId == personneDe).Langue.ShouldBe(Language.De);
        var projetee = (await OccupationProjeteeAsync(occupation)).ShouldNotBeNull();
        projetee.DateDebut.ShouldBe(new DateOnly(2026, 1, 1));
        projetee.DateFin.ShouldBeNull();

        // Aucun instantané ne transporte d'identité (ARC-06).
        plateforme.Bus.Publies.Where(m => m.Sujet.Contains("-instantane", StringComparison.Ordinal))
            .ShouldAllBe(m => !m.Charge.Contains("Rattrapage") && !m.Charge.Contains("Marie") && !m.Charge.Contains("850730"));

        // 3. Rejeu, livraisons doublées et désordonnées : mêmes lignes, mêmes valeurs.
        var avant = (await ContextesAsync(affilie, personneNl, personneDe)).Select(c => (c.SujetId, c.Regime, c.Langue, c.EvenementDu)).ToList();
        plateforme.Bus.Chaos = true;
        await RepublierAsync(plateforme.Affilies, "affilies");
        await RepublierAsync(plateforme.Personnes, "personnes", "occupations");
        plateforme.Bus.Chaos = false;
        (await ContextesAsync(affilie, personneNl, personneDe)).Select(c => (c.SujetId, c.Regime, c.Langue, c.EvenementDu)).ShouldBe(avant, ignoreOrder: true);
        (await OccupationsProjeteesAsync(occupation)).ShouldBe(1);
        plateforme.Bus.LettresMortes.ShouldBeEmpty();

        // 4. Un instantané lu avant un changement de langue et livré après lui n'écrase pas le nouvel état.
        plateforme.Bus.Retenus.Add("personnes.personne-instantanee.v1");
        await RepublierAsync(plateforme.Personnes, "personnes");
        var identite = await plateforme.Personnes.Client(Roles.GestionnaireDossiers).PutAsJsonAsync(
            $"/api/v1/personnes/{personneNl}/identite",
            new { nom = "Rattrapage", prenom = "Marie", dateNaissance = "1985-07-30", sexe = "Feminin", langue = "Fr" },
            Ct);
        identite.StatusCode.ShouldBe(HttpStatusCode.NoContent, await identite.Content.ReadAsStringAsync(Ct));
        await plateforme.PomperAsync(Ct);
        (await ContextesAsync(personneNl)).Single().Langue.ShouldBe(Language.Fr);

        plateforme.Bus.Relacher();
        await plateforme.PomperAsync(Ct);
        (await ContextesAsync(personneNl)).Single().Langue.ShouldBe(Language.Fr, "l'instantané retardé (langue Nl) est plus ancien que LangueModifiee.");
    }

    [Fact]
    public async Task Seule_l_exploitation_republie_et_elle_ne_lit_aucune_donnee_metier()
    {
        var gestionnaire = plateforme.Personnes.Client(Roles.GestionnaireDossiers);
        (await gestionnaire.PostAsJsonAsync("/api/v1/admin/republications", new { type = "personnes" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await plateforme.Affilies.Client(Roles.AdministrateurFonctionnel).GetAsync("/api/v1/admin/republications", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var exploitant = plateforme.Affilies.Client(Roles.Exploitation);
        (await exploitant.GetAsync("/api/v1/affilies", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await exploitant.GetFromJsonAsync<string[]>("/api/v1/admin/republications/types", Ct)).ShouldBe(["affilies"]);
    }

    private async Task RepublierAsync(HoteService producteur, params string[] types)
    {
        var exploitant = producteur.Client(Roles.Exploitation, "exploitant");
        var suivis = new List<string>();
        foreach (var type in types)
        {
            var reponse = await exploitant.PostAsJsonAsync("/api/v1/admin/republications", new { type }, Ct);
            reponse.StatusCode.ShouldBe(HttpStatusCode.Accepted, await reponse.Content.ReadAsStringAsync(Ct));
            suivis.Add(reponse.Headers.Location!.ToString());
        }

        await plateforme.PomperAsync(Ct);
        foreach (var suivi in suivis)
        {
            var etat = await exploitant.GetFromJsonAsync<JsonElement>(suivi, Json, Ct);
            etat.GetProperty("statut").GetString().ShouldBe("Terminee");
            etat.GetProperty("publies").GetInt32().ShouldBeGreaterThan(0);
            etat.GetProperty("demandeePar").GetString().ShouldBe("exploitant");
        }
    }

    private async Task<Guid> CreerAffilieAsync()
    {
        var reponse = await plateforme.Affilies.Client(Roles.GestionnaireDossiers).PostAsJsonAsync("/api/v1/affilies", new
        {
            numeroBce = "0202.239.951",
            dateAffiliation = "2024-01-01",
            fiche = new
            {
                denomination = "Bakkerij Rattrapage",
                formeJuridique = "BV",
                codeNace = "10.711",
                commissionParitaire = "118.03",
                categorieTarifaire = "B",
                langue = "Nl",
                regimeLinguistique = "Neerlandais",
            },
        }, Ct);
        reponse.StatusCode.ShouldBe(HttpStatusCode.Created, await reponse.Content.ReadAsStringAsync(Ct));
        return (await reponse.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private async Task<(Guid Personne, Guid Occupation)> CreerPersonneAsync(Guid affilie, int ordre, string langue)
    {
        var corps = long.Parse($"850730{ordre:D3}", CultureInfo.InvariantCulture);
        var niss = $"850730{ordre:D3}{97 - (corps % 97):D2}";
        var reponse = await plateforme.Personnes.Client(Roles.GestionnaireDossiers).PostAsJsonAsync("/api/v1/personnes", new
        {
            niss,
            identite = new { nom = "Rattrapage", prenom = "Marie", dateNaissance = "1985-07-30", sexe = "Feminin", langue },
            coordonnees = new { canalPrefere = "Portail" },
            occupation = new { affilieId = affilie, typeTravailleur = "Salarie", typeContrat = "DureeIndeterminee", dateDebut = "2026-01-01" },
        }, Ct);
        reponse.StatusCode.ShouldBe(HttpStatusCode.Created, await reponse.Content.ReadAsStringAsync(Ct));
        var corpsReponse = await reponse.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (corpsReponse.GetProperty("personneId").GetGuid(), corpsReponse.GetProperty("occupationId").GetGuid());
    }

    private async Task<List<ContexteLinguistiqueProjete>> ContextesAsync(params Guid[] sujets)
    {
        await using var scope = plateforme.Documents.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DocumentsDbContext>().ContextesLinguistiques.AsNoTracking()
            .Where(c => sujets.Contains(c.SujetId))
            .ToListAsync(Ct);
    }

    private async Task<Sepp.Affilies.Domain.Occupations.OccupationLocale?> OccupationProjeteeAsync(Guid occupation)
    {
        await using var scope = plateforme.Affilies.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AffiliesDbContext>().Occupations.AsNoTracking()
            .SingleOrDefaultAsync(o => o.OccupationId == occupation, Ct);
    }

    private async Task<int> OccupationsProjeteesAsync(Guid occupation)
    {
        await using var scope = plateforme.Affilies.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AffiliesDbContext>().Occupations.CountAsync(o => o.OccupationId == occupation, Ct);
    }
}
