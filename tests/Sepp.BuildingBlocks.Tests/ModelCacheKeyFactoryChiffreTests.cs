using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;

using Sepp.BuildingBlocks.Infrastructure.Security;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>
/// #442 : le modèle EF Core est mis en cache par processus. Sa clé doit intégrer l'identité du chiffreur capturé par les
/// convertisseurs, sans quoi un second hôte du processus réutiliserait le chiffreur (et les clés) du premier.
/// </summary>
public class ModelCacheKeyFactoryChiffreTests
{
    private readonly ModelCacheKeyFactoryChiffre _fabrique = new();

    [Fact]
    public void Deux_chiffreurs_distincts_donnent_deux_cles_de_modele()
    {
        using var a = new ContexteDeTest(Chiffreur());
        using var b = new ContexteDeTest(Chiffreur());

        _fabrique.Create(a, designTime: false).ShouldNotBe(_fabrique.Create(b, designTime: false));
    }

    [Fact]
    public void Le_meme_chiffreur_donne_la_meme_cle_de_modele_donc_un_seul_modele_par_processus_en_production()
    {
        var chiffreur = Chiffreur();
        using var a = new ContexteDeTest(chiffreur);
        using var b = new ContexteDeTest(chiffreur);

        _fabrique.Create(a, designTime: false).ShouldBe(_fabrique.Create(b, designTime: false));
    }

    [Fact]
    public void La_cle_distingue_le_mode_conception_et_le_type_de_contexte()
    {
        var chiffreur = Chiffreur();
        using var a = new ContexteDeTest(chiffreur);
        using var autre = new AutreContexteDeTest(chiffreur);

        _fabrique.Create(a, designTime: false).ShouldNotBe(_fabrique.Create(a, designTime: true));
        _fabrique.Create(a, designTime: false).ShouldNotBe(_fabrique.Create(autre, designTime: false));
    }

    [Fact]
    public void Un_contexte_sans_chiffrement_garde_une_cle_stable()
    {
        using var a = new ContexteSansChiffrement();
        using var b = new ContexteSansChiffrement();

        _fabrique.Create(a, designTime: false).ShouldBe(_fabrique.Create(b, designTime: false));
    }

    private static FieldEncryptor Chiffreur() => new(new CleJetable());

    private sealed class CleJetable : IFieldKeyProvider
    {
        private readonly byte[] _cle = RandomNumberGenerator.GetBytes(32);

        public string CurrentKeyId => "test";

        public byte[] GetKey(string keyId) => _cle;
    }

    private sealed class ContexteDeTest(FieldEncryptor chiffreur) : DbContext, IContexteChiffre
    {
        public FieldEncryptor Chiffreur { get; } = chiffreur;
    }

    private sealed class AutreContexteDeTest(FieldEncryptor chiffreur) : DbContext, IContexteChiffre
    {
        public FieldEncryptor Chiffreur { get; } = chiffreur;
    }

    private sealed class ContexteSansChiffrement : DbContext;
}
