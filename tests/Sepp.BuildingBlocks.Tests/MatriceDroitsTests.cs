using System.Reflection;

using Sepp.BuildingBlocks.Application.Security;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>
/// Non-régression de la matrice des droits (§3.3, NF-02) : <see cref="RolePermissions"/> est comparé à la source de vérité
/// versionnée <c>docs/securite/matrice-droits.csv</c>. Toute permission en trop ou en moins, tout rôle inconnu ou oublié
/// fait échouer le test : une modification de droits impose de modifier la matrice dans la même demande de fusion.
/// </summary>
public class MatriceDroitsTests
{
    private static readonly Lazy<IReadOnlyList<(string Role, string Permission, string Origine)>> Lignes = new(Lire);

    private static readonly string[] Origines = ["3.3", "hors-3.3"];

    private static IReadOnlyList<(string Role, string Permission, string Origine)> Lire()
    {
        var chemin = Path.Combine(RacineDepot(), "docs", "securite", "matrice-droits.csv");
        var lignes = File.ReadAllLines(chemin).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        lignes[0].ShouldBe("role;permission;origine", "en-tête de la matrice des droits");
        return [.. lignes.Skip(1).Select(l =>
        {
            var colonnes = l.Split(';');
            colonnes.Length.ShouldBe(3, $"ligne invalide : {l}");
            return (colonnes[0], colonnes[1], colonnes[2]);
        })];
    }

    private static string RacineDepot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Sepp.slnx")) && Directory.Exists(Path.Combine(dir.FullName, "docs")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Racine du dépôt introuvable (Sepp.slnx, docs).");
    }

    private static string[] Constantes(Type type) =>
        [.. type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!)];

    private static string[] TousLesRoles => [.. Constantes(typeof(Roles)).Union(Lignes.Value.Select(l => l.Role)).Order(StringComparer.Ordinal)];

    private static string[] ToutesLesPermissions =>
        [.. Constantes(typeof(Permissions)).Union(Lignes.Value.Select(l => l.Permission)).Order(StringComparer.Ordinal)];

    /// <summary>Produit cartésien rôle × permission, avec la valeur attendue d'après la matrice.</summary>
    public static IEnumerable<TheoryDataRow<string, string, bool>> RoleParPermission()
    {
        var accordees = Lignes.Value.Select(l => (l.Role, l.Permission)).ToHashSet();
        foreach (var role in TousLesRoles)
        {
            foreach (var permission in ToutesLesPermissions)
            {
                yield return new TheoryDataRow<string, string, bool>(role, permission, accordees.Contains((role, permission)));
            }
        }
    }

    public static IEnumerable<TheoryDataRow<string>> Roles_() => TousLesRoles.Select(r => new TheoryDataRow<string>(r));

    [Theory]
    [MemberData(nameof(RoleParPermission))]
    public void Chaque_couple_role_permission_est_conforme_a_la_matrice(string role, string permission, bool attendu) =>
        RolePermissions.For([role]).Contains(permission).ShouldBe(
            attendu,
            attendu
                ? $"{role} devrait avoir {permission} d'après docs/securite/matrice-droits.csv"
                : $"{role} a {permission}, absent de docs/securite/matrice-droits.csv");

    [Theory]
    [MemberData(nameof(Roles_))]
    public void Les_permissions_de_chaque_role_sont_exactement_celles_de_la_matrice(string role)
    {
        var attendues = Lignes.Value.Where(l => l.Role == role).Select(l => l.Permission).Order(StringComparer.Ordinal).ToArray();

        RolePermissions.For([role]).Order(StringComparer.Ordinal).ToArray().ShouldBe(attendues);
    }

    [Fact]
    public void Les_roles_de_la_matrice_sont_exactement_les_roles_connus_du_code()
    {
        var dansMatrice = Lignes.Value.Select(l => l.Role).Distinct().Order(StringComparer.Ordinal).ToArray();

        dansMatrice.ShouldBe([.. Constantes(typeof(Roles)).Order(StringComparer.Ordinal)]);
        RolePermissions.Matrice().Keys.Order(StringComparer.Ordinal).ToArray().ShouldBe(dansMatrice);
    }

    [Fact]
    public void Un_role_inconnu_n_a_aucune_permission() =>
        RolePermissions.For(["role-inconnu", "ADMIN", string.Empty]).ShouldBeEmpty();

    [Fact]
    public void Les_permissions_de_la_matrice_sont_celles_declarees_et_toutes_attribuees()
    {
        var declarees = Constantes(typeof(Permissions)).Order(StringComparer.Ordinal).ToArray();
        var attribuees = Lignes.Value.Select(l => l.Permission).Distinct().Order(StringComparer.Ordinal).ToArray();

        attribuees.ShouldBe(declarees);
    }

    [Fact]
    public void La_matrice_ne_contient_ni_doublon_ni_origine_invalide()
    {
        Lignes.Value.GroupBy(l => (l.Role, l.Permission)).Where(g => g.Count() > 1).ShouldBeEmpty();
        Lignes.Value.Where(l => !Origines.Contains(l.Origine)).ShouldBeEmpty();
    }

    /// <summary>
    /// Le manifeste des rôles d'application Entra (<c>docs/securite/entra-app-roles.json</c>) déclare tous les rôles du code,
    /// sauf les profils externes (employeur, SIPP, travailleur), portés par la fédération CSAM et non par l'annuaire interne.
    /// </summary>
    [Fact]
    public void Le_manifeste_des_roles_entra_couvre_les_roles_internes_et_techniques()
    {
        var externes = new[] { Roles.Employeur, Roles.Sipp, Roles.Travailleur };
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(RacineDepot(), "docs", "securite", "entra-app-roles.json")));
        var declares = doc.RootElement.GetProperty("appRoles").EnumerateArray().ToList();

        declares.Select(r => r.GetProperty("value").GetString()!).Order(StringComparer.Ordinal).ToArray()
            .ShouldBe([.. Constantes(typeof(Roles)).Except(externes).Order(StringComparer.Ordinal)]);
        declares.Select(r => r.GetProperty("id").GetString()).Distinct().Count().ShouldBe(declares.Count, "identifiants de rôle uniques");

        string[] techniques = [Roles.Integrations, Roles.Documents, Roles.DocumentsMedicale, Roles.Communications, Roles.Affilies];
        foreach (var role in declares)
        {
            var valeur = role.GetProperty("value").GetString()!;
            role.GetProperty("allowedMemberTypes")[0].GetString().ShouldBe(techniques.Contains(valeur) ? "Application" : "User", valeur);
        }
    }

    [Fact]
    public void La_matrice_est_triee_pour_des_diff_lisibles() =>
        Lignes.Value.ShouldBe([.. Lignes.Value.OrderBy(l => l.Role, StringComparer.Ordinal).ThenBy(l => l.Permission, StringComparer.Ordinal)]);
}
