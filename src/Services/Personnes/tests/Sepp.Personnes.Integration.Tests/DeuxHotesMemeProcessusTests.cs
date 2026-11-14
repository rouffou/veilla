using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Security;

using Shouldly;

namespace Sepp.Personnes.Integration.Tests;

/// <summary>
/// #442 : le modèle EF Core est mis en cache par processus ; le convertisseur de chiffrement ne doit pas figer le
/// <see cref="FieldEncryptor"/> du premier hôte. Deux hôtes du même processus, avec des clés différentes, doivent chacun
/// écrire et relire leurs propres données.
/// </summary>
public sealed class DeuxHotesMemeProcessusTests(PersonnesApiFixture fixture) : IClassFixture<PersonnesApiFixture>
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deux_hotes_avec_des_cles_differentes_lisent_chacun_leurs_propres_donnees()
    {
        // Hôte A : celui de la fixture (créé en premier, son modèle EF est mis en cache). Hôte B : autre base, autres clés.
        var hoteA = fixture.Factory;
        var baseB = $"personnes_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(fixture.ConnectionString))
        {
            await admin.OpenAsync(_ct);
            await using var creation = new NpgsqlCommand($"CREATE DATABASE {baseB}", admin);
            await creation.ExecuteNonQueryAsync(_ct);
        }

        var connexionB = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = baseB }.ConnectionString;
        await using var hoteB = PersonnesApiFixture.CreerHote(connexionB, new JournalCapture());
        _ = hoteB.Server;

        var chiffreurA = hoteA.Services.GetRequiredService<FieldEncryptor>();
        var chiffreurB = hoteB.Services.GetRequiredService<FieldEncryptor>();
        chiffreurB.ShouldNotBeSameAs(chiffreurA);

        var nissA = NissValide(950);
        var nissB = NissValide(951);
        await Creer(hoteA, nissA);
        await Creer(hoteB, nissB);

        // Chaque base contient le NISS chiffré avec la clé de son hôte : relu avec le chiffreur de cet hôte, jamais avec l'autre.
        var chiffreA = await LireNissChiffre(fixture.ConnectionString);
        var chiffreB = await LireNissChiffre(connexionB);
        chiffreurA.Decrypt(chiffreA).ShouldBe(nissA);
        chiffreurB.Decrypt(chiffreB).ShouldBe(nissB);
        Should.Throw<CryptographicException>(() => chiffreurB.Decrypt(chiffreA));
        Should.Throw<CryptographicException>(() => chiffreurA.Decrypt(chiffreB));

        // Relecture applicative (déchiffrement par le modèle EF) sur chaque hôte.
        foreach (var (hote, niss) in new[] { (hoteA, nissA), (hoteB, nissB) })
        {
            var id = await LireId(hote == hoteA ? fixture.ConnectionString : connexionB);
            var fiche = await PersonnesApiFixture.Client(hote, Roles.Cpmt).GetStringAsync($"/api/v1/personnes/{id}", _ct);
            fiche.ShouldContain($"XX.XX.XX-XXX.{niss[9..]}");
        }
    }

    private async Task Creer(WebApplicationFactory<Program> hote, string niss)
    {
        var reponse = await PersonnesApiFixture.Client(hote, Roles.GestionnaireDossiers).PostAsJsonAsync("/api/v1/personnes", new
        {
            niss,
            identite = new { nom = "Dupont", prenom = "Marie", dateNaissance = "1985-07-30", sexe = "Feminin", langue = "Fr" },
            coordonnees = new
            {
                adresse = new { rue = "Rue de la Loi", numero = "16", codePostal = "1000", localite = "Bruxelles" },
                email = "marie@example.test",
                canalPrefere = "Courrier",
            },
            occupation = new { affilieId = Guid.CreateVersion7(), typeTravailleur = "Salarie", typeContrat = "DureeIndeterminee", dateDebut = "2026-01-01" },
        }, _ct);
        reponse.StatusCode.ShouldBe(HttpStatusCode.Created, await reponse.Content.ReadAsStringAsync(_ct));
    }

    private async Task<string> LireNissChiffre(string connexion)
    {
        await using var c = new NpgsqlConnection(connexion);
        await c.OpenAsync(_ct);
        await using var commande = new NpgsqlCommand("SELECT niss_chiffre FROM personne", c);
        return (string)(await commande.ExecuteScalarAsync(_ct))!;
    }

    private async Task<Guid> LireId(string connexion)
    {
        await using var c = new NpgsqlConnection(connexion);
        await c.OpenAsync(_ct);
        await using var commande = new NpgsqlCommand("SELECT id FROM personne", c);
        return (Guid)(await commande.ExecuteScalarAsync(_ct))!;
    }

    /// <summary>NISS valide (personne née le 30/07/1985) ; numéros d'ordre 9xx réservés à ce test.</summary>
    private static string NissValide(int ordre)
    {
        var corps = long.Parse($"850730{ordre:D3}", CultureInfo.InvariantCulture);
        return $"850730{ordre:D3}{97 - (corps % 97):D2}";
    }
}
