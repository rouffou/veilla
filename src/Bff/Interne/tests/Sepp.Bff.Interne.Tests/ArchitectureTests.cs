using System.Xml.Linq;

using Sepp.Bff.Interne.Ecrans;
using Sepp.Bff.Interne.Relais;
using Sepp.Bff.Interne.Securite;
using Sepp.BuildingBlocks.Application.Security;

using Shouldly;

namespace Sepp.Bff.Interne.Tests;

/// <summary>ARC-43, ADR 0013 : le BFF interne parle aux services par HTTP seulement, sur une liste fermée, aux seuls profils internes.</summary>
public sealed class ArchitectureTests
{
    private static readonly string[] Autorises = ["Sepp.Bff.Interne", "Sepp.BuildingBlocks", "Sepp.Contracts"];

    private static string Racine()
    {
        var racine = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(racine!.FullName, "Sepp.slnx")))
        {
            racine = racine.Parent;
        }

        return racine.FullName;
    }

    [Fact]
    public void Le_BFF_ne_reference_aucun_assembly_de_service()
    {
        var references = typeof(Annuaire).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Sepp.", StringComparison.Ordinal))
            .ToList();

        references.Where(n => !Autorises.Any(p => n.StartsWith(p, StringComparison.Ordinal)))
            .ShouldBeEmpty("Le BFF ne parle aux services que par HTTP.");
    }

    [Fact]
    public void Le_projet_du_BFF_ne_reference_aucun_projet_de_service()
    {
        var projet = XDocument.Load(Path.Combine(Racine(), "src", "Bff", "Interne", "src", "Sepp.Bff.Interne", "Sepp.Bff.Interne.csproj"));
        projet.Descendants("ProjectReference")
            .Select(r => r.Attribute("Include")!.Value.Replace('\\', '/'))
            .Where(chemin => chemin.Contains("/Services/", StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty();
    }

    [Fact]
    public void La_liste_fermee_couvre_exactement_les_douze_services_metier_du_catalogue_terraform()
    {
        ServicesRelayes.Tous.Select(s => s.Segment).ShouldBe(
        [
            "referentiels", "audit", "affilies", "personnes", "postes-risques", "obligations", "integrations",
            "surveillance-medicale", "planification", "documents", "communications", "reintegration",
        ], ignoreOrder: true);

        var catalogue = File.ReadAllText(Path.Combine(Racine(), "infra", "variables.tf"));
        foreach (var service in ServicesRelayes.Tous)
        {
            catalogue.ShouldContain($"\"{service.Segment}\"", Case.Sensitive);
        }
    }

    [Fact]
    public void Chaque_route_du_relais_exige_la_politique_interne_et_reste_sous_api_v1()
    {
        var routes = RelaisServices.Routes();
        routes.Count.ShouldBe(ServicesRelayes.Tous.Count);
        foreach (var route in routes)
        {
            route.AuthorizationPolicy.ShouldBe(PerimetreInterne.Politique);
            route.Match.Path.ShouldBe($"/{route.ClusterId}/api/v1/{{**reste}}");
            route.Transforms!.ShouldContain(t => t.ContainsKey("PathPattern") && t["PathPattern"] == "/api/v1/{**reste}");
        }
    }

    [Fact]
    public void Les_roles_internes_excluent_les_profils_externes_et_les_comptes_techniques()
    {
        string[] interdits =
        [
            Roles.Employeur, Roles.Sipp, Roles.Travailleur, Roles.Integrations, Roles.Documents, Roles.DocumentsMedicale,
            Roles.Communications, Roles.Affilies, Roles.Exploitation,
        ];
        PerimetreInterne.RolesInternes.Intersect(interdits).ShouldBeEmpty();
        PerimetreInterne.RolesInternes.Length.ShouldBe(15);
    }

    [Fact]
    public void Aucun_en_tete_d_identite_ni_cookie_ni_host_n_est_transmis()
    {
        RelaisServices.EnTetesTransmis.ShouldBe(["Authorization", "Accept", "Accept-Language", "X-Motif-Acces", "X-Bris-De-Glace"], ignoreOrder: true);
    }

    [Fact]
    public void Chaque_ecran_composite_a_son_fichier()
    {
        var dossier = Path.Combine(Racine(), "src", "Bff", "Interne", "src", "Sepp.Bff.Interne", "Ecrans");
        foreach (var ecran in new[] { "Recherche", "DossierTravailleur", "Affilie", "Agenda", "Consultation", "TableauDeBord" })
        {
            File.Exists(Path.Combine(dossier, $"Ecran{ecran}.cs")).ShouldBeTrue($"Ecran{ecran}.cs absent.");
        }
    }
}
