using Sepp.BuildingBlocks.Domain;

namespace Sepp.SurveillanceMedicale.Domain.Conservation;

/// <summary>
/// SAN-44, NF-20 : durée de conservation propre à un type d'exposition (amiante, agents cancérigènes, rayonnements
/// ionisants…), plus longue que le minimum légal. Le code s'applique aux agents égaux ou commençant par
/// <c>CODE.</c> (par ex. <c>CANCERIGENE</c> couvre <c>CANCERIGENE.BENZENE</c>). Table à valider par le département médical (§2.1).
/// </summary>
public sealed class DureeConservationExposition : AggregateRoot
{
    private DureeConservationExposition()
    {
    }

    private DureeConservationExposition(Guid id, string codeAgent, int annees, string baseLegale) : base(id)
    {
        CodeAgent = Garde.Code(codeAgent, "agent");
        Modifier(annees, baseLegale);
    }

    public string CodeAgent { get; private set; } = string.Empty;

    public int Annees { get; private set; }

    public string BaseLegale { get; private set; } = string.Empty;

    public static DureeConservationExposition Definir(string codeAgent, int annees, string baseLegale) =>
        new(NewId(), codeAgent, annees, baseLegale);

    public void Modifier(int annees, string baseLegale)
    {
        if (annees is < PolitiqueConservationDossier.PlancherAnnees or > 100)
        {
            throw new DomainException($"La durée de conservation est comprise entre {PolitiqueConservationDossier.PlancherAnnees} et 100 ans.");
        }

        Annees = annees;
        BaseLegale = Garde.Requis(baseLegale, "base légale", 300);
    }

    public bool Couvre(string agent) =>
        string.Equals(agent, CodeAgent, StringComparison.Ordinal) || agent.StartsWith(CodeAgent + ".", StringComparison.Ordinal);
}

/// <summary>
/// SAN-44, §2.1 : le dossier de santé est conservé au moins le minimum légal (paramètre
/// <c>SANTE.DOSSIER.CONSERVATION_MINIMUM</c>, jamais moins de 15 ans) après la dernière activité, ou plus longtemps
/// pour certaines expositions (<see cref="DureeConservationExposition"/>).
/// </summary>
public static class PolitiqueConservationDossier
{
    public const string CodeParametreMinimum = "SANTE.DOSSIER.CONSERVATION_MINIMUM";

    /// <summary>Plancher légal (§2.1) : un paramètre plus court est ignoré.</summary>
    public const int PlancherAnnees = 15;

    public static int DureeAnnees(int minimumLegalAnnees, IEnumerable<string> agentsExposes, IEnumerable<DureeConservationExposition> durees)
    {
        var listeDurees = durees.ToList();
        var minimum = Math.Max(minimumLegalAnnees, PlancherAnnees);
        var parExposition = agentsExposes
            .SelectMany(agent => listeDurees.Where(d => d.Couvre(agent)).Select(d => d.Annees))
            .DefaultIfEmpty(0)
            .Max();
        return Math.Max(minimum, parExposition);
    }

    public static DateOnly DatePurge(DateOnly derniereActivite, int minimumLegalAnnees, IEnumerable<string> agentsExposes, IEnumerable<DureeConservationExposition> durees) =>
        derniereActivite.AddYears(DureeAnnees(minimumLegalAnnees, agentsExposes, durees));
}

/// <summary>
/// NF-22 : preuve de destruction d'un dossier de santé après validation humaine. Conservée après la purge :
/// identifiants, dates, auteurs, volume et empreinte SHA-256 du contenu détruit — jamais le contenu.
/// </summary>
public sealed class PreuveDestruction : AggregateRoot
{
    private PreuveDestruction()
    {
    }

    private PreuveDestruction(Guid id, Guid dossierId, Guid personneId, DateOnly datePurgePrevue, DateTimeOffset detruitLe, string valideePar,
        string motifValidation, int nombreElements, string empreinte)
        : base(id)
    {
        DossierId = dossierId;
        PersonneId = personneId;
        DatePurgePrevue = datePurgePrevue;
        DetruitLe = detruitLe;
        ValideePar = Garde.Requis(valideePar, "validée par", 100);
        MotifValidation = Garde.Requis(motifValidation, "motif de validation", 300);
        NombreElements = nombreElements;
        Empreinte = Garde.Requis(empreinte, "empreinte", 64);
    }

    public Guid DossierId { get; private set; }

    public Guid PersonneId { get; private set; }

    public DateOnly DatePurgePrevue { get; private set; }

    public DateTimeOffset DetruitLe { get; private set; }

    /// <summary>Responsable du traitement (CPMT dirigeant) qui a validé la destruction.</summary>
    public string ValideePar { get; private set; } = string.Empty;

    public string MotifValidation { get; private set; } = string.Empty;

    /// <summary>Nombre d'éléments détruits (examens, décisions, pièces jointes…).</summary>
    public int NombreElements { get; private set; }

    /// <summary>SHA-256 (hexadécimal) de l'export complet du dossier au moment de la destruction.</summary>
    public string Empreinte { get; private set; } = string.Empty;

    public static PreuveDestruction Etablir(Guid dossierId, Guid personneId, DateOnly datePurgePrevue, DateTimeOffset detruitLe, string valideePar,
        string motifValidation, int nombreElements, string empreinte)
    {
        if (string.Equals(valideePar, "system", StringComparison.Ordinal))
        {
            throw new DomainException("La destruction d'un dossier de santé exige une validation humaine.");
        }

        return new(NewId(), dossierId, personneId, datePurgePrevue, detruitLe, valideePar, motifValidation, nombreElements, empreinte);
    }
}
