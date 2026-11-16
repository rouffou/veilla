using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Sepp.Affilies.Adapters.Persistence;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Personnes.Adapters.Persistence;
using Sepp.Reintegration.Adapters.Persistence;
using Sepp.SurveillanceMedicale.Adapters.Persistence;

using Shouldly;

namespace Sepp.Pseudonymisation.Tests;

/// <summary>
/// Garde-fou (#499) : le catalogue de pseudonymisation doit couvrir exactement les colonnes chiffrées des modèles EF
/// (celles dont le convertisseur porte <see cref="IConvertisseurChiffrement"/>). Il échoue dans les deux sens : une colonne
/// chiffrée absente du catalogue (elle resterait illisible avec la clé cible dans la copie de formation), ou une entrée du
/// catalogue qui n'est plus chiffrée (elle serait traitée comme un chiffré à tort).
/// </summary>
public sealed class CatalogueChiffrementTests
{
    private static readonly HashSet<Technique> TechniquesChiffrees =
    [
        Technique.ChiffreRechiffre,
        Technique.ChiffreSupprime,
        Technique.ChiffreNul,
        Technique.ChiffreJson,
        Technique.NissChiffre,
    ];

    private static readonly Dictionary<string, Func<IModel>> ModelesParService = new(StringComparer.Ordinal)
    {
        [CatalogueRegles.Personnes] = () => Contexte<PersonnesDbContext>(
            o => new PersonnesDbContext(o, new Utilisateur(), TimeProvider.System, Chiffreur())).Model,
        [CatalogueRegles.Affilies] = () => Contexte<AffiliesDbContext>(
            o => new AffiliesDbContext(o, new Utilisateur(), TimeProvider.System)).Model,
        [CatalogueRegles.SurveillanceMedicale] = () => Contexte<SurveillanceMedicaleDbContext>(
            o => new SurveillanceMedicaleDbContext(
                o, new Utilisateur(), TimeProvider.System, new Sepp.SurveillanceMedicale.Adapters.Persistence.ChiffrementZoneMedicale(Chiffreur()))).Model,
        [CatalogueRegles.Reintegration] = () => Contexte<ReintegrationDbContext>(
            o => new ReintegrationDbContext(
                o, new Utilisateur(), TimeProvider.System, new Sepp.Reintegration.Adapters.Persistence.ChiffrementZoneMedicale(Chiffreur()))).Model,
    };

    public static TheoryData<string> Services => [.. CatalogueRegles.Tous.Select(s => s.Nom)];

    [Fact]
    public void Chaque_service_du_catalogue_a_un_modele_de_reference()
    {
        CatalogueRegles.Tous.Select(s => s.Nom).Order().ShouldBe(ModelesParService.Keys.Order());
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void Les_colonnes_chiffrees_du_modele_sont_exactement_celles_du_catalogue(string service)
    {
        var attendues = ColonnesDuModele(service);
        var declarees = CatalogueRegles.Pour(service).Tables
            .SelectMany(t => t.Colonnes.Where(c => TechniquesChiffrees.Contains(c.Technique)).Select(c => $"{t.Table}.{c.Colonne}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        attendues.Except(declarees).ToList()
            .ShouldBeEmpty($"{service} : colonnes chiffrées du modèle absentes du catalogue (ou déclarées avec une technique non chiffrée).");
        declarees.Except(attendues).ToList()
            .ShouldBeEmpty($"{service} : entrées du catalogue qui ne sont plus des colonnes chiffrées du modèle.");
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void L_indicateur_de_chiffrement_du_service_suit_le_modele(string service)
    {
        CatalogueRegles.Pour(service).Chiffrement.ShouldBe(ColonnesDuModele(service).Count > 0);
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void Toute_colonne_du_catalogue_existe_dans_le_modele(string service)
    {
        var colonnesModele = ModelesParService[service]().GetRelationalModel().Tables
            .SelectMany(t => t.Columns.Select(c => $"{t.Name}.{c.Name}"))
            .ToHashSet(StringComparer.Ordinal);
        var inconnues = CatalogueRegles.Pour(service).Tables
            .SelectMany(t => t.Colonnes.Select(c => $"{t.Table}.{c.Colonne}"))
            .Where(c => !colonnesModele.Contains(c))
            .ToList();

        inconnues.ShouldBeEmpty($"{service} : colonnes du catalogue absentes du modèle EF.");
    }

    private static List<string> ColonnesDuModele(string service) =>
        [.. ColonnesChiffrees.Depuis(ModelesParService[service]()).Select(c => $"{c.Table}.{c.Colonne}").Order(StringComparer.Ordinal)];

    /// <summary>Construit le modèle sans ouvrir de connexion : la chaîne n'est jamais utilisée.</summary>
    private static TContexte Contexte<TContexte>(Func<DbContextOptions<TContexte>, TContexte> fabrique)
        where TContexte : DbContext
    {
        var options = new DbContextOptionsBuilder<TContexte>()
            .UseNpgsql("Host=localhost;Database=modele")
            .UseSnakeCaseNamingConvention()
            .Options;
        return fabrique(options);
    }

    private static FieldEncryptor Chiffreur() => new(new CleJetable());

    private sealed class Utilisateur : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public string UserId => "test";

        public IReadOnlySet<string> Roles => new HashSet<string>();

        public bool HasPermission(string permission) => false;
    }

    private sealed class CleJetable : IFieldKeyProvider
    {
        private readonly byte[] _cle = RandomNumberGenerator.GetBytes(32);

        public string CurrentKeyId => "test";

        public byte[] GetKey(string keyId) => _cle;
    }
}
