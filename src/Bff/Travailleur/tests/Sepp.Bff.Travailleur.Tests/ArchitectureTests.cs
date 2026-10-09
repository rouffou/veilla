using System.Xml.Linq;

using Sepp.Bff.Travailleur.Ecrans;

using Shouldly;

namespace Sepp.Bff.Travailleur.Tests;

/// <summary>ARC-43 : le BFF compose des écrans à partir des API des services, sans dépendre de leur code (ARC-02, ADR 0002).</summary>
public sealed class ArchitectureTests
{
    private static readonly string[] Autorises = ["Sepp.Bff.Travailleur", "Sepp.BuildingBlocks", "Sepp.Contracts"];

    private static string RacineDepot()
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
        var references = typeof(EcransRendezVous).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Sepp.", StringComparison.Ordinal))
            .ToList();

        references.Where(n => !Autorises.Any(p => n.StartsWith(p, StringComparison.Ordinal)))
            .ShouldBeEmpty("Le BFF ne parle aux services que par HTTP.");
    }

    [Fact]
    public void Le_projet_du_BFF_ne_reference_aucun_projet_de_service()
    {
        var projet = XDocument.Load(Path.Combine(RacineDepot(), "src", "Bff", "Travailleur", "src", "Sepp.Bff.Travailleur", "Sepp.Bff.Travailleur.csproj"));
        projet.Descendants("ProjectReference")
            .Select(r => r.Attribute("Include")!.Value.Replace('\\', '/'))
            .Where(chemin => chemin.Contains("/Services/", StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty();
    }

    /// <summary>
    /// §3.3 : le travailleur n'obtient son dossier médical que « sur demande » (SAN-43), jamais en lecture directe par le portail.
    /// Les clients HTTP du BFF ne déclarent donc aucune route de dossier, de décision complète ni d'export.
    /// </summary>
    [Theory]
    [InlineData("api/v1/dossiers")]
    [InlineData("/complete")]
    [InlineData("/export")]
    [InlineData("/pre-rempli\"")]
    [InlineData("api/v1/decisions")]
    public void Les_clients_aval_n_appellent_aucune_route_de_dossier_medical(string fragment)
    {
        var fichier = Path.Combine(RacineDepot(), "src", "Bff", "Travailleur", "src", "Sepp.Bff.Travailleur", "Aval", "ApisAval.cs");

        File.ReadAllText(fichier).ShouldNotContain(fragment, Case.Insensitive);
    }
}
