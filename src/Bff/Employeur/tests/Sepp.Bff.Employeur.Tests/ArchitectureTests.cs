using System.Xml.Linq;

using Sepp.Bff.Employeur.Ecrans;

using Shouldly;

namespace Sepp.Bff.Employeur.Tests;

/// <summary>ARC-43 : le BFF compose des écrans à partir des API des services, sans dépendre de leur code (ARC-02, ADR 0002).</summary>
public sealed class ArchitectureTests
{
    private static readonly string[] Autorises = ["Sepp.Bff.Employeur", "Sepp.BuildingBlocks", "Sepp.Contracts"];

    [Fact]
    public void Le_BFF_ne_reference_aucun_assembly_de_service()
    {
        var references = typeof(EcransEmployeur).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Sepp.", StringComparison.Ordinal))
            .ToList();

        references.Where(n => !Autorises.Any(p => n.StartsWith(p, StringComparison.Ordinal)))
            .ShouldBeEmpty("Le BFF ne parle aux services que par HTTP.");
    }

    [Fact]
    public void Le_projet_du_BFF_ne_reference_aucun_projet_de_service()
    {
        var racine = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(racine!.FullName, "Sepp.slnx")))
        {
            racine = racine.Parent;
        }

        var projet = XDocument.Load(Path.Combine(racine.FullName, "src", "Bff", "Employeur", "src", "Sepp.Bff.Employeur", "Sepp.Bff.Employeur.csproj"));
        projet.Descendants("ProjectReference")
            .Select(r => r.Attribute("Include")!.Value.Replace('\\', '/'))
            .Where(chemin => chemin.Contains("/Services/", StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty();
    }
}
