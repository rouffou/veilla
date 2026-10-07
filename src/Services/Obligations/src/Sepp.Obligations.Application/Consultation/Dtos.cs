using Sepp.BuildingBlocks.Domain;
using Sepp.Obligations.Domain.Calcul;
using Sepp.Obligations.Domain.Obligations;

namespace Sepp.Obligations.Application.Consultation;

/// <summary>Obligation exposée par l'API : types, dates et statuts, jamais de contenu médical.</summary>
public sealed record ObligationDto(
    Guid Id,
    Guid PersonneId,
    Guid AffilieId,
    string Type,
    string TypeLibelle,
    string Origine,
    IReadOnlyList<string> CodesRisques,
    DateOnly DateDue,
    DateOnly? DateLimite,
    string Statut,
    string? Categorie,
    bool EnRetard,
    Guid? RendezVousId,
    DateTimeOffset? DateRendezVous,
    DateOnly? DateRealisation,
    DateOnly? DateReport,
    string? MotifAnnulation,
    IReadOnlyList<string> StatutsSuivants)
{
    public static ObligationDto De(Obligation o, DateOnly aujourdHui, int horizonDuesJours, Language langue) =>
        new(
            o.Id,
            o.PersonneId,
            o.AffilieId,
            o.Type.Code(),
            o.Type.Libelle().In(langue),
            o.Origine.ToString(),
            o.CodesRisques,
            o.DateDue,
            o.DateLimite,
            o.Statut.ToString(),
            Classement.Classer(o, aujourdHui, horizonDuesJours)?.ToString(),
            o.EstEnRetardAu(aujourdHui),
            o.RendezVousId,
            o.DateRendezVous,
            o.DateRealisation,
            o.DateReport,
            o.MotifAnnulation?.ToString(),
            MachineEtatsObligation.Suivants(o.Statut).Select(s => s.ToString()).ToList());
}

/// <summary>Une ligne de la trace de calcul (§15.3 trace_calcul) : règle ou paramètre appliqué, entrées et explication.</summary>
public sealed record TraceCalculDto(
    int Numero, DateTimeOffset DateCalcul, string Regle, int? RegleVersion, string Explication, IReadOnlyList<EntreeCalculDto> Entrees);

public sealed record EntreeCalculDto(string Cle, string Valeur);

public sealed record TraceObligationDto(ObligationDto Obligation, IReadOnlyList<TraceCalculDto> Traces);

/// <summary>Synthèse du tableau de bord de l'employeur (POR-02) : décomptes par catégorie.</summary>
public sealed record SyntheseObligationsDto(
    Guid AffilieId, DateOnly Date, int EnRetard, int Planifiees, int Dues, int AVenir, IReadOnlyDictionary<string, int> ParType);

public sealed record PropositionRendezVousDto(
    Guid PersonneId,
    Guid AffilieId,
    DateOnly DateProposee,
    DateOnly DateAuPlusTard,
    bool EstRegroupement,
    IReadOnlyList<ObligationDto> Obligations);

public sealed record AlerteDto(
    string Type,
    Guid AffilieId,
    Guid? PersonneId,
    Guid? PosteId,
    string? TypeListe,
    DateOnly? Depuis,
    string Message,
    IReadOnlyList<Guid> ObligationIds)
{
    public static AlerteDto De(Alerte a) =>
        new(a.Type.ToString(), a.AffilieId, a.PersonneId, a.PosteId, a.TypeListe, a.Depuis, a.Message, a.ObligationIds);
}
