using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Personnes.Adapters.Persistence;
using Sepp.Personnes.Application;
using Sepp.Personnes.Application.Personnes;

using Shouldly;

using IndexPersonnes = Sepp.Personnes.Adapters.DependencyInjection;

namespace Sepp.Personnes.Integration.Tests;

/// <summary>
/// Rotation de la clé de l'index aveugle du NISS (#473) sur un vrai PostgreSQL : trois hôtes partagent la base et les DEK.
/// « Avant » ne connaît que l'ancienne clé ; « pendant » écrit avec la nouvelle et lit les deux empreintes ; « après » ne
/// connaît que la nouvelle. Recherche pendant et après le recalcul, doublon refusé pendant la transition, collision, et
/// reprise après interruption. Clés tirées pour le test : jamais de vraie clé.
/// </summary>
public sealed class RotationIndexAveugleTests(PersonnesApiFixture fixture) : IClassFixture<PersonnesApiFixture>
{
    private const string Courante = "BlindIndex:CleEnveloppee";
    private const string Precedente = "BlindIndex:CleEnveloppeePrecedente";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly OptionsRechiffrement Petit = new() { TailleLot = 2, PauseEntreLots = TimeSpan.Zero };

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task La_recherche_par_niss_aboutit_pendant_et_apres_un_recalcul_interrompu_puis_repris()
    {
        await using var hotes = await HotesAsync();
        var personnes = new List<(Guid Id, string Niss)>();
        for (var ordre = 901; ordre <= 905; ordre++)
        {
            var niss = Niss(ordre);
            personnes.Add((await CreerAsync(hotes.Avant, niss), niss));
        }

        // Rotation déployée, rien de recalculé : toutes les personnes sont trouvées par l'ancienne empreinte.
        await ToutesTrouveesAsync(hotes.Pendant, personnes);

        // Recalcul interrompu après le premier lot (2 lignes) : l'index mélange les deux clés.
        await using (var connexion = new NpgsqlConnection(hotes.Connexion))
        {
            await connexion.OpenAsync(_ct);
            using var annulation = CancellationTokenSource.CreateLinkedTokenSource(_ct);
            await Should.ThrowAsync<OperationCanceledException>(() => RecalculIndexAveugle.ExecuterAsync(
                connexion, hotes.Pendant.Services.GetRequiredService<FieldEncryptor>(), IndexNiss(hotes.Pendant), Petit with { PauseEntreLots = TimeSpan.FromMilliseconds(1) },
                NullLogger.Instance, new HorlogeQuiAnnule(annulation), annulation.Token));
        }

        var melange = await VerifierAsync(hotes.Pendant);
        melange.ShouldSatisfyAllConditions(
            v => v.Lignes.ShouldBe(5),
            v => v.Courantes.ShouldBe(2),
            v => v.ClePrecedente.ShouldBe(3),
            v => v.Complet.ShouldBeFalse());
        await ToutesTrouveesAsync(hotes.Pendant, personnes);

        // Reprise : seules les 3 empreintes restantes sont recalculées.
        var reprise = (await RechiffrementHote.RecalculerIndexAveuglesAsync<PersonnesDbContext>(
            hotes.Pendant.Services, s => s.GetRequiredService<FieldEncryptor>(), IndexDuService, Petit, NullLogger.Instance, _ct)).ShouldHaveSingleItem();
        reprise.ShouldSatisfyAllConditions(
            r => r.Colonne.ShouldBe("personne.niss_hash"),
            r => r.Recalculees.ShouldBe(3),
            r => r.AJour.ShouldBe(2),
            r => r.Collisions.ShouldBe(0),
            r => r.Complet.ShouldBeTrue());
        await ToutesTrouveesAsync(hotes.Pendant, personnes);

        // Ancienne clé retirée : le service ne connaît plus que la nouvelle clé, et tout est trouvé.
        await ToutesTrouveesAsync(hotes.Apres, personnes);
        (await VerifierAsync(hotes.Apres)).Complet.ShouldBeTrue();

        // Rejeu : rien n'est réécrit.
        var rejeu = (await RechiffrementHote.RecalculerIndexAveuglesAsync<PersonnesDbContext>(
            hotes.Apres.Services, s => s.GetRequiredService<FieldEncryptor>(), IndexDuService, Petit, NullLogger.Instance, _ct)).ShouldHaveSingleItem();
        rejeu.Recalculees.ShouldBe(0);
        rejeu.AJour.ShouldBe(5);
    }

    [Fact]
    public async Task Un_doublon_est_refuse_pendant_la_transition()
    {
        await using var hotes = await HotesAsync();
        var niss = Niss(911);
        var id = await CreerAsync(hotes.Avant, niss);

        // Même NISS, même identité : la personne existante est reprise ; autre identité : conflit. Jamais de seconde ligne.
        (await CreerAsync(hotes.Pendant, niss)).ShouldBe(id);
        var conflit = await PersonnesApiFixture.Client(hotes.Pendant, Roles.GestionnaireDossiers).PostAsJsonAsync("/api/v1/personnes", Corps(niss, "Durand"), _ct);
        conflit.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await NombreDePersonnesAsync(hotes.Connexion)).ShouldBe(1);

        // Identification pour Intégrations (SAN-60) par l'ancienne empreinte.
        var identification = await PersonnesApiFixture.Client(hotes.Pendant, Roles.Integrations).PostAsJsonAsync("/api/v1/identifications", new { niss }, _ct);
        identification.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await identification.Content.ReadAsStringAsync(_ct)).ShouldContain(id.ToString());
    }

    [Fact]
    public async Task Une_collision_sur_l_index_unique_est_comptee_en_erreur_sans_rien_ecraser()
    {
        await using var hotes = await HotesAsync();
        var niss = Niss(921);
        var ancienne = await CreerAsync(hotes.Avant, niss);

        // Doublon créé hors de la procédure (une révision qui ne connaissait que la nouvelle clé) : deux lignes, même NISS.
        var doublon = await CreerAsync(hotes.Apres, niss);
        doublon.ShouldNotBe(ancienne);
        var avant = await EmpreintesAsync(hotes.Connexion);

        var rapport = (await RechiffrementHote.RecalculerIndexAveuglesAsync<PersonnesDbContext>(
            hotes.Pendant.Services, s => s.GetRequiredService<FieldEncryptor>(), IndexDuService, Petit, NullLogger.Instance, _ct)).ShouldHaveSingleItem();

        rapport.ShouldSatisfyAllConditions(
            r => r.Collisions.ShouldBe(1),
            r => r.Erreurs.ShouldBe(1),
            r => r.Recalculees.ShouldBe(0),
            r => r.Complet.ShouldBeFalse(),
            r => r.Verification.ClePrecedente.ShouldBe(1));
        rapport.ErreursDetaillees.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Cle.ShouldBe(ancienne.ToString()),
            e => e.Message.ShouldContain("23505"));
        (await EmpreintesAsync(hotes.Connexion)).ShouldBe(avant);
        (await NombreDePersonnesAsync(hotes.Connexion)).ShouldBe(2);
    }

    [Fact]
    public async Task Un_doublon_trouve_sous_les_deux_empreintes_est_journalise_et_compte_sans_niss_ni_empreinte()
    {
        await using var hotes = await HotesAsync();
        var niss = Niss(931);
        var ancienne = await CreerAsync(hotes.Avant, niss);
        var nouvelle = await CreerAsync(hotes.Apres, niss);
        var empreintes = await EmpreintesAsync(hotes.Connexion);

        // Compteur du seul hôte « pendant » : son meter est créé par sa propre fabrique (portée).
        var fabrique = hotes.Pendant.Services.GetRequiredService<IMeterFactory>();
        long doublons = 0;
        using var ecoute = new MeterListener();
        ecoute.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == AlerteDoublonsNiss.NomMeter && instrument.Name == AlerteDoublonsNiss.NomCompteur
                && ReferenceEquals(instrument.Meter.Scope, fabrique))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        ecoute.SetMeasurementEventCallback<long>((_, mesure, _, _) => Interlocked.Add(ref doublons, mesure));
        ecoute.Start();

        // La personne de la clé courante (« pendant » écrit avec la nouvelle clé) est retenue.
        var recherche = await PersonnesApiFixture.Client(hotes.Pendant, Roles.Cpmt).PostAsJsonAsync("/api/v1/personnes/recherche", new { niss }, _ct);
        recherche.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await recherche.Content.ReadFromJsonAsync<PersonneResumeDto>(Json, _ct))!.Id.ShouldBe(nouvelle);

        Interlocked.Read(ref doublons).ShouldBe(1);
        var avertissement = hotes.JournalPendant.Entrees.Where(e => e.Contains("Doublon de NISS", StringComparison.Ordinal)).ShouldHaveSingleItem();
        avertissement.ShouldContain(nouvelle.ToString());
        avertissement.ShouldContain(ancienne.ToString());
        hotes.JournalPendant.Entrees.ShouldAllBe(e => !e.Contains(niss, StringComparison.Ordinal));
        foreach (var (_, empreinte) in empreintes)
        {
            hotes.JournalPendant.Entrees.ShouldAllBe(e => !e.Contains(empreinte, StringComparison.OrdinalIgnoreCase));
        }

        // Une seule empreinte connue (hôte « après ») : aucun doublon vu, aucune alerte.
        (await PersonnesApiFixture.Client(hotes.Apres, Roles.Cpmt).PostAsJsonAsync("/api/v1/personnes/recherche", new { niss }, _ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        hotes.JournalApres.Entrees.ShouldNotContain(e => e.Contains("Doublon de NISS", StringComparison.Ordinal));
    }

    [Fact]
    public async Task La_kcv_de_chaque_cle_de_l_index_est_journalisee_au_demarrage()
    {
        await using var hotes = await HotesAsync();
        var pendant = hotes.Pendant.Services.GetRequiredService<BlindIndex>();
        var apres = hotes.Apres.Services.GetRequiredService<BlindIndex>();
        var avant = hotes.Avant.Services.GetRequiredService<BlindIndex>();

        pendant.Kcv.ShouldBe(apres.Kcv);
        pendant.KcvPrecedente.ShouldBe(avant.Kcv);
        pendant.Kcv.ShouldNotBe(avant.Kcv);
        Kcvs(hotes.JournalPendant).ShouldBe([$"clé courante, KCV {pendant.Kcv}.", $"clé précédente acceptée en lecture, KCV {pendant.KcvPrecedente}."]);
        Kcvs(hotes.JournalApres).ShouldBe([$"clé courante, KCV {apres.Kcv}."]);
        Kcvs(hotes.JournalAvant).ShouldBe([$"clé courante, KCV {avant.Kcv}."]);

        static List<string> Kcvs(JournalCapture journal) =>
        [
            .. journal.Entrees
                .Select(e => System.Text.RegularExpressions.Regex.Match(e, "Index aveugle personne\\.niss_hash : (.*?KCV [0-9a-f]{8}\\.)"))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value),
        ];
    }

    private static IReadOnlyList<IndexAveugleChiffre> IndexDuService(IServiceProvider services, Microsoft.EntityFrameworkCore.Metadata.IModel modele) =>
        [IndexPersonnes.IndexAveugleNiss(modele, services.GetRequiredService<BlindIndex>())];

    private static IndexAveugleChiffre IndexNiss(WebApplicationFactory<Program> hote)
    {
        using var scope = hote.Services.CreateScope();
        return IndexDuService(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<PersonnesDbContext>().Model)[0];
    }

    private async Task<RapportVerificationIndexAveugle> VerifierAsync(WebApplicationFactory<Program> hote) =>
        (await RechiffrementHote.VerifierIndexAveuglesAsync<PersonnesDbContext>(
            hote.Services, s => s.GetRequiredService<FieldEncryptor>(), IndexDuService, Petit, NullLogger.Instance, _ct)).ShouldHaveSingleItem();

    private async Task ToutesTrouveesAsync(WebApplicationFactory<Program> hote, IEnumerable<(Guid Id, string Niss)> personnes)
    {
        foreach (var (id, niss) in personnes)
        {
            var recherche = await PersonnesApiFixture.Client(hote, Roles.Cpmt).PostAsJsonAsync("/api/v1/personnes/recherche", new { niss }, _ct);
            recherche.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await recherche.Content.ReadFromJsonAsync<PersonneResumeDto>(Json, _ct))!.Id.ShouldBe(id);
        }
    }

    private async Task<Guid> CreerAsync(WebApplicationFactory<Program> hote, string niss)
    {
        var reponse = await PersonnesApiFixture.Client(hote, Roles.GestionnaireDossiers).PostAsJsonAsync("/api/v1/personnes", Corps(niss), _ct);
        reponse.StatusCode.ShouldBe(HttpStatusCode.Created, await reponse.Content.ReadAsStringAsync(_ct));
        return (await reponse.Content.ReadFromJsonAsync<PersonneEnregistreeDto>(Json, _ct))!.PersonneId;
    }

    private static object Corps(string niss, string nom = "Dupont") => new
    {
        niss,
        identite = new { nom, prenom = "Marie", dateNaissance = "1985-07-30", sexe = "Feminin", langue = "Fr" },
        occupation = new { affilieId = Guid.CreateVersion7(), typeTravailleur = "Salarie", typeContrat = "DureeIndeterminee", dateDebut = "2026-01-01" },
    };

    private async Task<int> NombreDePersonnesAsync(string connexion)
    {
        await using var c = new NpgsqlConnection(connexion);
        await c.OpenAsync(_ct);
        await using var commande = new NpgsqlCommand("SELECT count(*) FROM personne", c);
        return Convert.ToInt32(await commande.ExecuteScalarAsync(_ct), CultureInfo.InvariantCulture);
    }

    private async Task<List<(Guid Id, string Empreinte)>> EmpreintesAsync(string connexion)
    {
        await using var c = new NpgsqlConnection(connexion);
        await c.OpenAsync(_ct);
        await using var commande = new NpgsqlCommand("SELECT id, niss_hash FROM personne ORDER BY id", c);
        await using var lecteur = await commande.ExecuteReaderAsync(_ct);
        var empreintes = new List<(Guid, string)>();
        while (await lecteur.ReadAsync(_ct))
        {
            empreintes.Add((lecteur.GetGuid(0), lecteur.GetString(1)));
        }

        return empreintes;
    }

    /// <summary>NISS valide (personne née le 30/07/1985) ; numéros d'ordre 901 à 929 réservés à ces tests, chacun dans sa base.</summary>
    private static string Niss(int ordre)
    {
        var corps = long.Parse($"850730{ordre:D3}", CultureInfo.InvariantCulture);
        return $"850730{ordre:D3}{97 - (corps % 97):D2}";
    }

    /// <summary>
    /// Base propre au test et trois hôtes qui partagent ses DEK : l'ancienne et la nouvelle clé de l'index sont enveloppées
    /// par la même clé racine locale, comme en Azure par <c>personnes-fields-kek</c>.
    /// </summary>
    private async Task<Hotes> HotesAsync()
    {
        var nom = $"rotation_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(fixture.ConnectionString))
        {
            await admin.OpenAsync(_ct);
            await using var creation = new NpgsqlCommand($"CREATE DATABASE {nom}", admin);
            await creation.ExecuteNonQueryAsync(_ct);
        }

        var connexion = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = nom }.ConnectionString;
        var pendant = ChiffrementChamps.ConfigurationLocale("Encryption", "test", new Dictionary<string, byte[]>
        {
            [Courante] = RandomNumberGenerator.GetBytes(32),
            [Precedente] = RandomNumberGenerator.GetBytes(32),
        });
        var avant = pendant.Where(c => c.Key is not (Courante or Precedente)).ToDictionary(c => c.Key, c => c.Value);
        avant[Courante] = pendant[Precedente];
        var apres = pendant.Where(c => c.Key != Precedente).ToDictionary(c => c.Key, c => c.Value);

        var journalAvant = new JournalCapture();
        var journalPendant = new JournalCapture();
        var journalApres = new JournalCapture();
        var hotes = new Hotes(
            connexion,
            PersonnesApiFixture.CreerHote(connexion, journalAvant, avant),
            PersonnesApiFixture.CreerHote(connexion, journalPendant, pendant),
            PersonnesApiFixture.CreerHote(connexion, journalApres, apres))
        {
            JournalAvant = journalAvant,
            JournalPendant = journalPendant,
            JournalApres = journalApres,
        };
        _ = hotes.Avant.Server;
        _ = hotes.Pendant.Server;
        _ = hotes.Apres.Server;
        return hotes;
    }

    private sealed record Hotes(string Connexion, WebApplicationFactory<Program> Avant, WebApplicationFactory<Program> Pendant, WebApplicationFactory<Program> Apres)
        : IAsyncDisposable
    {
        public required JournalCapture JournalAvant { get; init; }

        public required JournalCapture JournalPendant { get; init; }

        public required JournalCapture JournalApres { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Avant.DisposeAsync();
            await Pendant.DisposeAsync();
            await Apres.DisposeAsync();
        }
    }

    /// <summary>Horloge qui annule le traitement dès qu'il demande une pause (interruption entre deux lots).</summary>
    private sealed class HorlogeQuiAnnule(CancellationTokenSource annulation) : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            annulation.Cancel();
            return System.CreateTimer(callback, state, dueTime, period);
        }
    }
}
