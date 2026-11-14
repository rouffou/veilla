using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Personnes.Adapters.Persistence;
using Sepp.Personnes.Application;

using Shouldly;

namespace Sepp.Personnes.Integration.Tests;

/// <summary>
/// Rechiffrement des champs (#396, ADR 0012) sur la base du service : NISS au format v1 (ancienne clé AES), avec une
/// ancienne DEK et avec la DEK courante, mélangés ; après rechiffrement, le service relit tout avec sa seule DEK courante
/// (aucune ancienne clé configurée) et la recherche par NISS fonctionne toujours (l'index aveugle ne change pas).
/// </summary>
public sealed partial class PersonnesApiTests
{
    [Fact]
    public async Task Le_rechiffrement_passe_v1_et_l_ancienne_DEK_a_la_DEK_courante_sans_casser_la_recherche_par_niss()
    {
        var personnes = new List<(Guid Id, string Niss)>();
        for (var i = 0; i < 5; i++)
        {
            var niss = NouveauNiss();
            personnes.Add(((await Creer(niss)).PersonneId, niss));
        }

        // Données antérieures, écrites comme avant l'ADR 0012 (v1) ou avant une rotation (ancienne DEK) ; l'index aveugle,
        // calculé sur le NISS en clair, reste celui de la création.
        var anciennes = new ClesAnciennes(fixture.Factory.Services.GetRequiredService<IFieldKeyProvider>());
        await EcrireNissChiffreAsync(personnes[0].Id, anciennes.ChiffrerV1(personnes[0].Niss));
        await EcrireNissChiffreAsync(personnes[1].Id, anciennes.ChiffrerV1(personnes[1].Niss));
        await EcrireNissChiffreAsync(personnes[2].Id, anciennes.ChiffrerAncienneDek(personnes[2].Niss));
        await EcrireNissChiffreAsync(personnes[3].Id, anciennes.ChiffrerAncienneDek(personnes[3].Niss));

        // Le traitement reçoit, en plus des clés du service, les anciennes clés (comme encryption_legacy_key_ids et une
        // ancienne DEK conservée dans encryption_key_ids) ; il écrit avec la DEK courante du service.
        var options = new OptionsRechiffrement { TailleLot = 2, PauseEntreLots = TimeSpan.Zero };
        var rapport = await RechiffrementHote.ExecuterAsync<PersonnesDbContext>(
            fixture.Factory.Services, _ => new FieldEncryptor(anciennes), options, NullLogger.Instance, _ct);

        rapport.Complet.ShouldBeTrue();
        rapport.RestantesV1.ShouldBe(0);
        rapport.Colonnes.Select(c => c.Colonne).ShouldBe(
            ["etat_particulier.type_chiffre", "mutation_registre_national.apres_chiffre", "mutation_registre_national.avant_chiffre", "personne.niss_chiffre"]);
        rapport.Colonnes.Single(c => c.Colonne == "personne.niss_chiffre").Rechiffrees.ShouldBe(4);

        // Relecture par le service, qui ne connaît que sa DEK courante, et recherche par NISS.
        await using var connexion = new NpgsqlConnection(fixture.ConnectionString);
        await connexion.OpenAsync(_ct);
        foreach (var (id, niss) in personnes)
        {
            (await NissChiffreAsync(connexion, id)).ShouldStartWith("v2:test:");
            var recherche = await fixture.Client(Roles.Cpmt).PostAsJsonAsync("/api/v1/personnes/recherche", new { niss }, _ct);
            recherche.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await recherche.Content.ReadFromJsonAsync<PersonneResumeDto>(Json, _ct))!.Id.ShouldBe(id);
            (await fixture.Client(Roles.Cpmt).GetStringAsync($"/api/v1/personnes/{id}", _ct)).ShouldContain($"XX.XX.XX-XXX.{niss[9..]}");
        }

        // Rejeu : rien n'est relu ni réécrit.
        var avant = await NissChiffreAsync(connexion, personnes[0].Id);
        var rejeu = await RechiffrementHote.ExecuterAsync<PersonnesDbContext>(
            fixture.Factory.Services, _ => new FieldEncryptor(anciennes), options, NullLogger.Instance, _ct);
        rejeu.Complet.ShouldBeTrue();
        rejeu.Rechiffrees.ShouldBe(0);
        rejeu.Colonnes.ShouldAllBe(c => c.Examinees == 0);
        (await NissChiffreAsync(connexion, personnes[0].Id)).ShouldBe(avant);
    }

    private async Task EcrireNissChiffreAsync(Guid id, string valeur)
    {
        await using var connexion = new NpgsqlConnection(fixture.ConnectionString);
        await connexion.OpenAsync(_ct);
        await using var commande = new NpgsqlCommand("UPDATE personne SET niss_chiffre = @v WHERE id = @id", connexion);
        commande.Parameters.AddWithValue("v", valeur);
        commande.Parameters.AddWithValue("id", id);
        (await commande.ExecuteNonQueryAsync(_ct)).ShouldBe(1);
    }

    private async Task<string> NissChiffreAsync(NpgsqlConnection connexion, Guid id)
    {
        await using var commande = new NpgsqlCommand("SELECT niss_chiffre FROM personne WHERE id = @id", connexion);
        commande.Parameters.AddWithValue("id", id);
        return (string)(await commande.ExecuteScalarAsync(_ct))!;
    }

    /// <summary>
    /// Clés du service (DEK courante) complétées d'une ancienne DEK et d'une ancienne clé AES v1, tirées pour le test :
    /// jamais de vraie clé.
    /// </summary>
    private sealed class ClesAnciennes(IFieldKeyProvider service) : IFieldKeyProvider
    {
        private const string AncienneDek = "ancienne";
        private const string CleV1 = "k1";
        private readonly byte[] _ancienneDek = RandomNumberGenerator.GetBytes(32);
        private readonly byte[] _cleV1 = RandomNumberGenerator.GetBytes(32);

        public string CurrentKeyId => service.CurrentKeyId;

        public byte[] GetKey(string keyId) => keyId == AncienneDek ? _ancienneDek : service.GetKey(keyId);

        public byte[] GetLegacyKey(string keyId) => keyId == CleV1 ? _cleV1 : service.GetLegacyKey(keyId);

        public string ChiffrerAncienneDek(string clair) => new FieldEncryptor(new Seulement(AncienneDek, _ancienneDek)).Encrypt(clair);

        /// <summary>Format historique v1 : clé AES en clair, données associées = identifiant de clé.</summary>
        public string ChiffrerV1(string clair)
        {
            var data = Encoding.UTF8.GetBytes(clair);
            var sortie = new byte[12 + data.Length + 16];
            RandomNumberGenerator.Fill(sortie.AsSpan(0, 12));
            using var aes = new AesGcm(_cleV1, 16);
            aes.Encrypt(sortie.AsSpan(0, 12), data, sortie.AsSpan(12, data.Length), sortie.AsSpan(12 + data.Length), Encoding.UTF8.GetBytes(CleV1));
            return $"v1:{CleV1}:{Convert.ToBase64String(sortie)}";
        }

        private sealed class Seulement(string id, byte[] cle) : IFieldKeyProvider
        {
            public string CurrentKeyId => id;

            public byte[] GetKey(string keyId) => keyId == id ? cle : throw new CryptographicException($"Clé de données '{keyId}' inconnue.");
        }
    }
}
