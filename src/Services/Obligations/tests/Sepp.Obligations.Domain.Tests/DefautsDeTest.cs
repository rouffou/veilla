using Sepp.Contracts.Referentiels;
using Sepp.Obligations.Domain.Calcul;

namespace Sepp.Obligations.Domain.Tests;

/// <summary>Valeurs par défaut injectées dans les politiques légales : celles du contrat Référentiels, comme en production.</summary>
internal static class DefautsDeTest
{
    public static IReadOnlyDictionary<string, DureeParDefaut> Valeurs { get; } = ParametresLegauxParDefaut.Toutes
        .Where(v => Enum.TryParse<UniteDuree>(v.Unite, out _))
        .ToDictionary(v => v.Code, v => new DureeParDefaut(v.Valeur, Enum.Parse<UniteDuree>(v.Unite), v.BaseLegale), StringComparer.Ordinal);
}
