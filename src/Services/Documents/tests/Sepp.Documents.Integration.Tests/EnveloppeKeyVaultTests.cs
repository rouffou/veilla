using System.Security.Cryptography;
using System.Text;

using Azure;

using Microsoft.Extensions.Configuration;

using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Documents.Adapters.Chiffrement;
using Sepp.Documents.Application;
using Sepp.Documents.Domain.Commun;

using Shouldly;

namespace Sepp.Documents.Integration.Tests;

/// <summary>
/// ARC-44, ADR 0009 : adaptateur Key Vault / Managed HSM derrière un coffre simulé (RSA-OAEP-256, 3072 bits), sans appel
/// Azure : version courante à l'enveloppe, kid versionné conservé, rotation, kid étranger refusé avant tout appel.
/// </summary>
public class EnveloppeKeyVaultTests
{
    private const string CleMedicale = "https://kv-sepp-med.vault.azure.net/keys/documents-kek";
    private const string CleHsm = "https://hsm-sepp-psy.managedhsm.azure.net/keys/documents-kek";
    private static readonly byte[] Clair = "carnet de vaccination"u8.ToArray();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    /// <summary>
    /// Coffre simulé : une clé RSA 3072 par version, la dernière version enveloppe, toutes déballent. Il refuse comme
    /// Azure Key Vault : 404 pour une version supprimée, 400 pour une clé enveloppée invalide, 403 si l'accès est retiré.
    /// </summary>
    private sealed class CoffreSimule : IOperationsCleRacine
    {
        private readonly Dictionary<string, RSA> _versions = new(StringComparer.Ordinal);

        public CoffreSimule() => Tourner();

        public string Courante { get; private set; } = string.Empty;

        public List<Uri> Deballages { get; } = [];

        /// <summary>Droit <c>unwrapKey</c> retiré à l'identité de l'instance : tout déballage répond 403.</summary>
        public bool AccesRetire { get; set; }

        public string Tourner()
        {
            Courante = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            _versions[Courante] = RSA.Create(3072);
            return Courante;
        }

        /// <summary>Suppression (ou purge) d'une version de la clé racine.</summary>
        public void Supprimer(string version) => _versions.Remove(version);

        public Task<CleDonneesEnveloppee> EnvelopperAsync(Uri cleSansVersion, byte[] cleDonnees, CancellationToken cancellationToken) =>
            Task.FromResult(new CleDonneesEnveloppee($"{cleSansVersion.AbsoluteUri}/{Courante}", _versions[Courante].Encrypt(cleDonnees, RSAEncryptionPadding.OaepSHA256)));

        public Task<byte[]> DesenvelopperAsync(Uri kid, byte[] cleEnveloppee, CancellationToken cancellationToken)
        {
            Deballages.Add(kid);
            if (AccesRetire)
            {
                throw new RequestFailedException(403, "Forbidden", "Forbidden", null);
            }

            if (!_versions.TryGetValue(kid.Segments[^1], out var rsa))
            {
                throw new RequestFailedException(404, "Key not found", "KeyNotFound", null);
            }

            try
            {
                return Task.FromResult(rsa.Decrypt(cleEnveloppee, RSAEncryptionPadding.OaepSHA256));
            }
            catch (CryptographicException ex)
            {
                throw new RequestFailedException(400, "Bad request", "BadParameter", ex);
            }
        }
    }

    private sealed class ZonesFixes(params ZoneDocument[] zones) : IZonesServies
    {
        public IReadOnlyCollection<ZoneDocument> Zones { get; } = zones;

        public bool Sert(ZoneDocument zone) => Zones.Contains(zone);
    }

    private static ChiffrementParZone Chiffrement(IEnveloppeCles enveloppe, params ZoneDocument[] zones) =>
        new(enveloppe, new ConfigurationBuilder().Build(), new ZonesFixes(zones));

    [Fact]
    public async Task L_enveloppe_utilise_la_version_courante_et_conserve_le_kid_versionne()
    {
        var coffre = new CoffreSimule();
        var chiffrement = Chiffrement(new EnveloppeKeyVault(new Dictionary<ZoneDocument, Uri> { [ZoneDocument.Medicale] = new(CleMedicale) }, coffre), ZoneDocument.Medicale);
        var id = Guid.CreateVersion7();

        var chiffre = await chiffrement.ChiffrerAsync(ZoneDocument.Medicale, id, Clair, _ct);

        chiffre.CleId.ShouldBe($"{CleMedicale}/{coffre.Courante}");
        Encoding.UTF8.GetString(chiffre.Octets).ShouldContain(chiffre.CleId);
        (await chiffrement.DechiffrerAsync(ZoneDocument.Medicale, id, chiffre.Octets, _ct)).ShouldBe(Clair);
        coffre.Deballages.ShouldHaveSingleItem().AbsoluteUri.ShouldBe(chiffre.CleId);
    }

    [Fact]
    public async Task Apres_rotation_de_la_cle_racine_un_document_enveloppe_par_l_ancienne_version_reste_lisible()
    {
        var coffre = new CoffreSimule();
        var chiffrement = Chiffrement(new EnveloppeKeyVault(new Dictionary<ZoneDocument, Uri> { [ZoneDocument.Psychosociale] = new(CleHsm) }, coffre), ZoneDocument.Psychosociale);
        var id = Guid.CreateVersion7();
        var ancienneVersion = coffre.Courante;
        var ancien = await chiffrement.ChiffrerAsync(ZoneDocument.Psychosociale, id, Clair, _ct);

        var nouvelleVersion = coffre.Tourner();
        var nouveau = await chiffrement.ChiffrerAsync(ZoneDocument.Psychosociale, id, Clair, _ct);

        ancien.CleId.ShouldEndWith(ancienneVersion);
        nouveau.CleId.ShouldEndWith(nouvelleVersion);
        (await chiffrement.DechiffrerAsync(ZoneDocument.Psychosociale, id, ancien.Octets, _ct)).ShouldBe(Clair);
        (await chiffrement.DechiffrerAsync(ZoneDocument.Psychosociale, id, nouveau.Octets, _ct)).ShouldBe(Clair);
    }

    [Theory]
    [InlineData("https://kv-pirate.vault.azure.net/keys/documents-kek/")]
    [InlineData("https://kv-sepp-med.vault.azure.net/keys/autre-cle/")]
    [InlineData("https://kv-sepp-med.vault.azure.net/keys/documents-kek/../../secrets/x/")]
    public async Task Un_kid_etranger_a_la_cle_racine_de_la_zone_est_refuse_sans_appel_au_coffre(string prefixe)
    {
        var coffre = new CoffreSimule();
        var enveloppe = new EnveloppeKeyVault(new Dictionary<ZoneDocument, Uri> { [ZoneDocument.Medicale] = new(CleMedicale) }, coffre);
        var cle = await enveloppe.EnvelopperAsync(ZoneDocument.Medicale, RandomNumberGenerator.GetBytes(32), _ct);

        await Should.ThrowAsync<ContenuAltereException>(() => enveloppe.DesenvelopperAsync(ZoneDocument.Medicale, prefixe + coffre.Courante, cle.Octets, _ct));
        await Should.ThrowAsync<ContenuAltereException>(() => enveloppe.DesenvelopperAsync(ZoneDocument.Medicale, cle.Kid + "?api-version=1", cle.Octets, _ct));
        coffre.Deballages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_cle_enveloppee_alteree_ou_un_objet_copie_vers_une_autre_zone_est_refuse()
    {
        var coffre = new CoffreSimule();
        var enveloppe = new EnveloppeKeyVault(new Dictionary<ZoneDocument, Uri>
        {
            [ZoneDocument.Medicale] = new(CleMedicale),
            [ZoneDocument.Psychosociale] = new(CleHsm),
        }, coffre);
        var chiffrement = Chiffrement(enveloppe, ZoneDocument.Medicale, ZoneDocument.Psychosociale);
        var id = Guid.CreateVersion7();
        var chiffre = (await chiffrement.ChiffrerAsync(ZoneDocument.Medicale, id, Clair, _ct)).Octets;

        // Le kid médical n'appartient pas à la clé racine psychosociale : refus avant tout appel au coffre.
        await Should.ThrowAsync<ContenuAltereException>(() => chiffrement.DechiffrerAsync(ZoneDocument.Psychosociale, id, chiffre, _ct));
        coffre.Deballages.ShouldBeEmpty();

        var longueurKid = (chiffre[8] << 8) | chiffre[9];
        var altere = (byte[])chiffre.Clone();
        altere[8 + 2 + longueurKid + 2 + 10] ^= 0x01;
        await Should.ThrowAsync<ContenuAltereException>(() => chiffrement.DechiffrerAsync(ZoneDocument.Medicale, id, altere, _ct));
        await Should.ThrowAsync<ContenuAltereException>(() => chiffrement.DechiffrerAsync(ZoneDocument.Medicale, Guid.CreateVersion7(), chiffre, _ct));
    }

    [Fact]
    public async Task Une_version_de_cle_racine_supprimee_n_est_pas_classee_comme_alteration()
    {
        // 404 du coffre (#377) : version supprimée ou purgée par une rotation mal conduite ; l'archive n'est pas en cause.
        var coffre = new CoffreSimule();
        var chiffrement = Chiffrement(new EnveloppeKeyVault(new Dictionary<ZoneDocument, Uri> { [ZoneDocument.Medicale] = new(CleMedicale) }, coffre), ZoneDocument.Medicale);
        var id = Guid.CreateVersion7();
        var ancienneVersion = coffre.Courante;
        var ancien = await chiffrement.ChiffrerAsync(ZoneDocument.Medicale, id, Clair, _ct);
        coffre.Tourner();
        coffre.Supprimer(ancienneVersion);

        var erreur = await Should.ThrowAsync<CleRacineIndisponibleException>(() => chiffrement.DechiffrerAsync(ZoneDocument.Medicale, id, ancien.Octets, _ct));

        erreur.Cause.ShouldBe(CauseCleRacineIndisponible.Introuvable);
        erreur.Kid.ShouldBe(ancien.CleId);
        erreur.InnerException.ShouldBeOfType<RequestFailedException>().Status.ShouldBe(404);
    }

    [Fact]
    public async Task Une_cle_enveloppee_refusee_par_le_coffre_reste_une_alteration()
    {
        // 400 du coffre : clé enveloppée invalide pour une version existante.
        var coffre = new CoffreSimule();
        var enveloppe = new EnveloppeKeyVault(new Dictionary<ZoneDocument, Uri> { [ZoneDocument.Medicale] = new(CleMedicale) }, coffre);
        var cle = await enveloppe.EnvelopperAsync(ZoneDocument.Medicale, RandomNumberGenerator.GetBytes(32), _ct);
        var alteree = (byte[])cle.Octets.Clone();
        alteree[10] ^= 0x01;

        var erreur = await Should.ThrowAsync<ContenuAltereException>(() => enveloppe.DesenvelopperAsync(ZoneDocument.Medicale, cle.Kid, alteree, _ct));

        erreur.InnerException.ShouldBeOfType<RequestFailedException>().Status.ShouldBe(400);
        coffre.Deballages.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Un_acces_refuse_par_le_coffre_est_une_erreur_d_exploitation_et_non_une_alteration()
    {
        // 403 du coffre : rôle retiré à l'identité de l'instance ou version désactivée.
        var coffre = new CoffreSimule();
        var chiffrement = Chiffrement(new EnveloppeKeyVault(new Dictionary<ZoneDocument, Uri> { [ZoneDocument.Psychosociale] = new(CleHsm) }, coffre), ZoneDocument.Psychosociale);
        var id = Guid.CreateVersion7();
        var chiffre = await chiffrement.ChiffrerAsync(ZoneDocument.Psychosociale, id, Clair, _ct);
        coffre.AccesRetire = true;

        var erreur = await Should.ThrowAsync<CleRacineIndisponibleException>(() => chiffrement.DechiffrerAsync(ZoneDocument.Psychosociale, id, chiffre.Octets, _ct));

        erreur.Cause.ShouldBe(CauseCleRacineIndisponible.AccesRefuse);
        erreur.Kid.ShouldBe(chiffre.CleId);

        // L'accès rétabli, l'archive se relit : elle n'a jamais été altérée.
        coffre.AccesRetire = false;
        (await chiffrement.DechiffrerAsync(ZoneDocument.Psychosociale, id, chiffre.Octets, _ct)).ShouldBe(Clair);
    }

    [Fact]
    public async Task Un_kid_renvoye_par_le_coffre_etranger_a_la_cle_configuree_est_refuse()
    {
        var enveloppe = new EnveloppeKeyVault(new Dictionary<ZoneDocument, Uri> { [ZoneDocument.Medicale] = new(CleMedicale) }, new CoffreDetourne());

        await Should.ThrowAsync<InvalidOperationException>(() => enveloppe.EnvelopperAsync(ZoneDocument.Medicale, RandomNumberGenerator.GetBytes(32), _ct));
    }

    [Fact]
    public void Une_meme_cle_racine_pour_deux_zones_est_refusee()
    {
        Should.Throw<InvalidOperationException>(() => new EnveloppeKeyVault(new Dictionary<ZoneDocument, Uri>
        {
            [ZoneDocument.Medicale] = new(CleMedicale),
            [ZoneDocument.Psychosociale] = new(CleMedicale),
        }, new CoffreSimule())).Message.ShouldContain("une clé par zone");
    }

    private sealed class CoffreDetourne : IOperationsCleRacine
    {
        public Task<CleDonneesEnveloppee> EnvelopperAsync(Uri cleSansVersion, byte[] cleDonnees, CancellationToken cancellationToken) =>
            Task.FromResult(new CleDonneesEnveloppee("https://kv-autre.vault.azure.net/keys/documents-kek/abc", [1, 2, 3]));

        public Task<byte[]> DesenvelopperAsync(Uri kid, byte[] cleEnveloppee, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
