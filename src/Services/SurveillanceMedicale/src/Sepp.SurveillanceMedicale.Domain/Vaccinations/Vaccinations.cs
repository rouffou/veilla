using Sepp.BuildingBlocks.Domain;
using Sepp.SurveillanceMedicale.Domain.Dossiers;

namespace Sepp.SurveillanceMedicale.Domain.Vaccinations;

/// <summary>Code réservé au « vaccin » des tests tuberculiniques dans les schémas (périodicité des tests, SAN-50).</summary>
public static class CodesVaccination
{
    public const string TestTuberculinique = "TEST_TUBERCULINIQUE";
}

/// <summary>
/// SAN-50 : schéma vaccinal par risque (hépatite B, tétanos, grippe…) ou périodicité des tests tuberculiniques.
/// <see cref="IntervallesMois"/> donne l'intervalle depuis la dose précédente pour les doses 2..n du schéma de base ;
/// <see cref="RappelMois"/> la périodicité des rappels une fois le schéma complet. Protocole médical, sans donnée personnelle.
/// </summary>
public sealed class SchemaVaccinal : AggregateRoot
{
    private readonly List<string> _codesRisques = [];
    private readonly List<int> _intervallesMois = [];

    private SchemaVaccinal()
    {
    }

    private SchemaVaccinal(Guid id, string vaccinCode, LocalizedLabel libelle, IEnumerable<string> codesRisques, int nombreDoses, IEnumerable<int> intervallesMois, int? rappelMois)
        : base(id)
    {
        VaccinCode = Garde.Code(vaccinCode, "vaccin");
        Libelle = libelle;
        _codesRisques.AddRange(codesRisques.Select(c => Garde.Code(c, "risque")).Distinct(StringComparer.Ordinal));
        if (_codesRisques.Count == 0)
        {
            throw new DomainException("Un schéma vaccinal est lié à au moins un risque.");
        }

        if (nombreDoses < 1)
        {
            throw new DomainException("Un schéma vaccinal comporte au moins une dose.");
        }

        _intervallesMois.AddRange(intervallesMois);
        if (_intervallesMois.Count != nombreDoses - 1 || _intervallesMois.Exists(i => i < 0))
        {
            throw new DomainException("Il faut un intervalle (en mois, positif) pour chaque dose après la première.");
        }

        if (rappelMois is < 1)
        {
            throw new DomainException("La périodicité du rappel est d'au moins un mois.");
        }

        NombreDoses = nombreDoses;
        RappelMois = rappelMois;
    }

    public string VaccinCode { get; private set; } = string.Empty;

    public LocalizedLabel Libelle { get; private set; } = null!;

    /// <summary>Risques (codes de l'annexe I.4-5, service Postes et risques) pour lesquels le schéma s'applique.</summary>
    public IReadOnlyList<string> CodesRisques => _codesRisques.AsReadOnly();

    public int NombreDoses { get; private set; }

    public IReadOnlyList<int> IntervallesMois => _intervallesMois.AsReadOnly();

    public int? RappelMois { get; private set; }

    public static SchemaVaccinal Definir(string vaccinCode, LocalizedLabel libelle, IEnumerable<string> codesRisques, int nombreDoses, IEnumerable<int> intervallesMois, int? rappelMois) =>
        new(NewId(), vaccinCode, libelle, codesRisques, nombreDoses, intervallesMois, rappelMois);

    public bool ConcerneRisques(IEnumerable<string> codesRisques) => codesRisques.Any(_codesRisques.Contains);
}

/// <summary>Prochaine dose ou prochain rappel dû (SAN-50).</summary>
public sealed record RappelVaccinal(string VaccinCode, int DoseSuivante, DateOnly DateDue, bool EnRetard, bool SchemaDeBase);

/// <summary>
/// SAN-50 : calcul des doses et rappels dus à partir des schémas applicables (risques actuels de la personne, ou
/// vaccin déjà commencé) et des vaccinations du dossier. Pour les tests tuberculiniques, la date du dernier test posé fait foi.
/// </summary>
public static class CalculRappels
{
    public static IReadOnlyList<RappelVaccinal> Calculer(
        IEnumerable<SchemaVaccinal> schemas, IReadOnlyCollection<string> risquesActuels, DossierSante dossier, DateOnly aujourdhui)
    {
        var rappels = new List<RappelVaccinal>();
        foreach (var schema in schemas)
        {
            if (schema.VaccinCode == CodesVaccination.TestTuberculinique)
            {
                if (schema.ConcerneRisques(risquesActuels) && schema.RappelMois is { } periode)
                {
                    var dernier = dossier.TestsTuberculiniques.Select(t => (DateOnly?)t.DatePose).Max();
                    var due = dernier?.AddMonths(periode) ?? aujourdhui;
                    rappels.Add(new RappelVaccinal(schema.VaccinCode, dossier.TestsTuberculiniques.Count + 1, due, due < aujourdhui, false));
                }

                continue;
            }

            var doses = dossier.Vaccinations.Where(v => v.VaccinCode == schema.VaccinCode).OrderBy(v => v.Date).ToList();
            if (!schema.ConcerneRisques(risquesActuels) && doses.Count == 0)
            {
                continue;
            }

            if (doses.Count == 0)
            {
                rappels.Add(new RappelVaccinal(schema.VaccinCode, 1, aujourdhui, false, true));
                continue;
            }

            var derniere = doses[^1];
            var prochaine = Math.Max(doses.Max(d => d.Dose), doses.Count) + 1;
            if (prochaine <= schema.NombreDoses)
            {
                var due = derniere.Date.AddMonths(schema.IntervallesMois[prochaine - 2]);
                rappels.Add(new RappelVaccinal(schema.VaccinCode, prochaine, due, due < aujourdhui, true));
            }
            else if (schema.RappelMois is { } rappel)
            {
                var due = derniere.Date.AddMonths(rappel);
                rappels.Add(new RappelVaccinal(schema.VaccinCode, prochaine, due, due < aujourdhui, false));
            }
        }

        return [.. rappels.OrderBy(r => r.DateDue)];
    }
}

/// <summary>
/// SAN-51 : lot de vaccins en stock dans un centre (§15.3 <c>lot_vaccin</c>) : numéro de lot, péremption, quantité.
/// Un lot périmé ou épuisé ne peut plus être administré.
/// </summary>
public sealed class LotVaccin : AggregateRoot
{
    private LotVaccin()
    {
    }

    private LotVaccin(Guid id, Guid centreId, string vaccinCode, string numeroLot, DateOnly peremption, int quantite) : base(id)
    {
        if (quantite < 1)
        {
            throw new DomainException("La quantité réceptionnée est d'au moins une dose.");
        }

        CentreId = Garde.Identifiant(centreId, "centre");
        VaccinCode = Garde.Code(vaccinCode, "vaccin");
        NumeroLot = Garde.Requis(numeroLot, "numéro de lot", 50).ToUpperInvariant();
        Peremption = peremption;
        QuantiteInitiale = quantite;
        QuantiteRestante = quantite;
    }

    public Guid CentreId { get; private set; }

    public string VaccinCode { get; private set; } = string.Empty;

    public string NumeroLot { get; private set; } = string.Empty;

    public DateOnly Peremption { get; private set; }

    public int QuantiteInitiale { get; private set; }

    public int QuantiteRestante { get; private set; }

    public static LotVaccin Receptionner(Guid centreId, string vaccinCode, string numeroLot, DateOnly peremption, int quantite, DateOnly aujourdhui) =>
        peremption <= aujourdhui
            ? throw new DomainException("Un lot déjà périmé ne peut pas être réceptionné.")
            : new(NewId(), centreId, vaccinCode, numeroLot, peremption, quantite);

    public bool EstPerimeAu(DateOnly date) => date >= Peremption;

    /// <summary>Une dose administrée : le lot doit correspondre au vaccin, être en stock et non périmé à la date d'administration.</summary>
    public void Consommer(string vaccinCode, DateOnly date)
    {
        if (!string.Equals(VaccinCode, vaccinCode, StringComparison.Ordinal))
        {
            throw new DomainException($"Le lot {NumeroLot} ne contient pas le vaccin {vaccinCode}.");
        }

        if (EstPerimeAu(date))
        {
            throw new DomainException($"Le lot {NumeroLot} est périmé depuis le {Peremption:dd/MM/yyyy}.");
        }

        if (QuantiteRestante < 1)
        {
            throw new DomainException($"Le lot {NumeroLot} est épuisé.");
        }

        QuantiteRestante--;
    }

    /// <summary>Correction d'inventaire (casse, perte, retour) : le stock ne devient jamais négatif ni supérieur à la réception.</summary>
    public void Ajuster(int delta)
    {
        var nouvelle = QuantiteRestante + delta;
        if (delta == 0 || nouvelle < 0 || nouvelle > QuantiteInitiale)
        {
            throw new DomainException("Ajustement de stock invalide.");
        }

        QuantiteRestante = nouvelle;
    }
}
