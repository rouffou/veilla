using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.SurveillanceMedicale.Application.Decisions;
using Sepp.SurveillanceMedicale.Application.Dossiers;
using Sepp.SurveillanceMedicale.Application.Examens;
using Sepp.SurveillanceMedicale.Application.Vaccinations;
using Sepp.SurveillanceMedicale.Domain.Dossiers;
using Sepp.SurveillanceMedicale.Domain.MaladiesProfessionnelles;
using Sepp.SurveillanceMedicale.Domain.Transferts;
using Sepp.SurveillanceMedicale.Domain.Vaccinations;

namespace Sepp.SurveillanceMedicale.Application.Consultation;

public sealed record PosteRisquesDto(Guid PosteId, Guid? AffilieId, DateOnly DateDebut, DateOnly? DateFin, IReadOnlyList<string> CodesRisques);

public sealed record ExamenHistoriqueDto(Guid Id, string TypeExamen, DateOnly Date, string Statut, string ProfessionnelId, string? CategorieDecision, DateOnly? ValideJusquAu);

public sealed record ExamenDuDto(Guid ObligationId, Guid AffilieId, string TypeExamen, DateOnly DateDue, DateOnly? DateLimite, bool EnRetard);

public sealed record AlerteResultatDto(Guid ExamenId, Guid ResultatId, string TypeActe, DateOnly Date, IReadOnlyList<string> MesuresInhabituelles, Guid? PropositionFrequenceId, string? StatutProposition);

/// <summary>
/// SAN-20 : vue « poste de consultation » (modèle de lecture composite, ARC-35). Identité minimale : <c>personne_id</c>
/// uniquement (le nom et le NISS restent dans le service Personnes, DAT-06, et sont affichés par le front via son API).
/// </summary>
public sealed record VueConsultationDto(
    DossierResumeDto Dossier,
    IReadOnlyList<PosteRisquesDto> PostesEtRisques,
    IReadOnlyList<ExamenHistoriqueDto> Historique,
    IReadOnlyList<ExamenDuDto> ExamensDus,
    IReadOnlyList<QuestionnaireDto> Questionnaires,
    IReadOnlyList<ResultatActeDto> Resultats,
    IReadOnlyList<AlerteResultatDto> Alertes,
    IReadOnlyList<RappelVaccinal> Rappels,
    IReadOnlyList<ExpositionDto> Expositions);

public sealed record ObtenirVueConsultation(Guid DossierId, DateOnly? Date);

public sealed class ObtenirVueConsultationHandler(
    GardeDossier garde, IExamenRepository examens, IDecisionRepository decisions, IProjectionRepository projections, IProtocolesRepository protocoles,
    RisquesPersonne risques, TimeProvider horloge)
    : IQueryHandler<ObtenirVueConsultation, VueConsultationDto>
{
    public async Task<Result<VueConsultationDto>> HandleAsync(ObtenirVueConsultation query, CancellationToken cancellationToken)
    {
        var acces = await garde.LireAsync(query.DossierId, PartiesDossier.Consultation, cancellationToken);
        if (!acces.IsSuccess)
        {
            return acces.Error!;
        }

        var dossier = acces.Value;
        var date = query.Date ?? horloge.Aujourdhui();
        var postes = await risques.PostesAsync(dossier.PersonneId, date, cancellationToken);
        var listeExamens = await examens.ListerParDossierAsync(dossier.Id, cancellationToken);
        var listeDecisions = await decisions.ListerParDossierAsync(dossier.Id, cancellationToken);
        var obligations = await projections.ListerObligationsAsync(dossier.PersonneId, cancellationToken);
        var schemas = await protocoles.ListerSchemasVaccinauxAsync(cancellationToken);

        var historique = listeExamens.OrderByDescending(e => e.Date).Select(e =>
        {
            var decision = listeDecisions.FirstOrDefault(d => d.ExamenId == e.Id);
            return new ExamenHistoriqueDto(e.Id, e.TypeExamen, e.Date, e.Statut.ToString(), e.ProfessionnelId,
                decision is null ? null : Domain.Decisions.CodesDecision.Code(decision.Categorie), decision?.ValideJusquAu);
        }).ToList();

        var dus = obligations.Where(o => o.SatisfaiteParExamenId is null).OrderBy(o => o.DateDue)
            .Select(o => new ExamenDuDto(o.ObligationId, o.AffilieId, o.TypeExamen, o.DateDue, o.DateLimite, (o.DateLimite ?? o.DateDue) < date)).ToList();

        var resultats = listeExamens.SelectMany(e => e.Resultats).OrderByDescending(r => r.Date).Select(r => r.Dto()).ToList();
        var alertes = listeExamens.SelectMany(e => e.Resultats.Where(r => r.Inhabituel).Select(r =>
        {
            var proposition = e.PropositionsFrequence.FirstOrDefault(p => p.ResultatActeId == r.Id);
            return new AlerteResultatDto(e.Id, r.Id, r.TypeActe.ToString(), r.Date, [.. r.Mesures.Where(m => m.Inhabituelle).Select(m => m.Code)],
                proposition?.Id, proposition?.Statut.ToString());
        })).OrderByDescending(a => a.Date).ToList();

        var codes = postes.SelectMany(p => p.CodesRisques).ToHashSet(StringComparer.Ordinal);
        return new VueConsultationDto(
            dossier.Resume(),
            [.. postes.Select(p => new PosteRisquesDto(p.PosteId, p.AffilieId, p.DateDebut, p.DateFin, p.CodesRisques))],
            historique,
            dus,
            [.. dossier.Questionnaires.OrderByDescending(q => q.RempliLe).Select(q => q.Dto())],
            resultats,
            alertes,
            CalculRappels.Calculer(schemas, codes, dossier, date),
            [.. dossier.Expositions.OrderByDescending(e => e.PeriodeDebut).Select(e => e.Dto())]);
    }
}

public sealed record DeclarationMpResumeDto(Guid Id, DateOnly Date, StatutDeclarationMp Statut, string? ReferenceFedris, ContenuDeclarationMp Contenu);

public sealed record DossierRecuDto(Guid TransfertId, string Contrepartie, DateOnly DateDemande, StatutTransfert Statut, string? Contenu);

/// <summary>
/// SAN-43 : export structuré du dossier (droit d'accès du travailleur, copie sur demande ; transfert SAN-42),
/// organisé selon les parties des art. I.4-85 à I.4-87. La version PDF sera produite par le service Documents.
/// </summary>
public sealed record ExportDossierDto(
    DateTimeOffset GenereLe,
    DossierResumeDto Identification,
    IReadOnlyList<Guid> AffiliesSuccessifs,
    IReadOnlyList<ExpositionDto> DonneesExposition,
    IReadOnlyList<ExamenDto> EvaluationsSante,
    IReadOnlyList<DecisionDto> Decisions,
    IReadOnlyList<QuestionnaireDto> Questionnaires,
    IReadOnlyList<VaccinationDto> Vaccinations,
    IReadOnlyList<TestTuberculiniqueDto> TestsTuberculiniques,
    IReadOnlyList<PieceJointeDto> PiecesJointes,
    IReadOnlyList<DeclarationMpResumeDto> MaladiesProfessionnelles,
    IReadOnlyList<DossierRecuDto> DossiersRecus);

/// <summary>Assemblage de l'export complet, réutilisé par le transfert (SAN-42) et la preuve de destruction (NF-22).</summary>
public sealed class ExportDossiers(
    IExamenRepository examens, IDecisionRepository decisions, IDeclarationMpRepository declarations, ITransfertRepository transferts, TimeProvider horloge)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ExportDossierDto> ConstruireAsync(DossierSante dossier, CancellationToken cancellationToken)
    {
        var listeExamens = await examens.ListerParDossierAsync(dossier.Id, cancellationToken);
        var listeDecisions = await decisions.ListerParDossierAsync(dossier.Id, cancellationToken);
        var listeDeclarations = await declarations.ListerParDossierAsync(dossier.Id, cancellationToken);
        var listeTransferts = await transferts.ListerParDossierAsync(dossier.Id, cancellationToken);
        var complet = dossier.Complet();
        return new ExportDossierDto(
            horloge.GetUtcNow(),
            complet.Dossier,
            [.. listeExamens.Select(e => e.AffilieId).Concat(dossier.GroupesExposition.Select(g => g.AffilieId)).Distinct()],
            complet.Expositions,
            [.. listeExamens.OrderBy(e => e.Date).Select(e => e.Dto())],
            [.. listeDecisions.OrderBy(d => d.DateExamen).Select(d => d.Complete())],
            complet.Questionnaires,
            complet.Vaccinations,
            complet.TestsTuberculiniques,
            complet.PiecesJointes,
            [.. listeDeclarations.Select(d => new DeclarationMpResumeDto(d.Id, d.Date, d.Statut, d.ReferenceFedris, d.Contenu))],
            [.. listeTransferts.Where(t => t.Direction == DirectionTransfert.Entrant)
                .Select(t => new DossierRecuDto(t.Id, t.Contrepartie, t.DateDemande, t.Statut, t.Statut == StatutTransfert.Integre ? t.Paquet : null))]);
    }

    /// <summary>Nombre d'éléments du dossier (preuve de destruction).</summary>
    public static int CompterElements(ExportDossierDto export) =>
        1 + export.DonneesExposition.Count + export.EvaluationsSante.Count + export.Decisions.Count + export.Questionnaires.Count
        + export.Vaccinations.Count + export.TestsTuberculiniques.Count + export.PiecesJointes.Count + export.MaladiesProfessionnelles.Count
        + export.DossiersRecus.Count;

    public static (string Json, string Empreinte) Serialiser(ExportDossierDto export)
    {
        var json = JsonSerializer.Serialize(export, Json);
        return (json, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
    }
}

public sealed record ExporterDossier(Guid DossierId);

public sealed class ExporterDossierHandler(GardeDossier garde, ExportDossiers export) : IQueryHandler<ExporterDossier, ExportDossierDto>
{
    public async Task<Result<ExportDossierDto>> HandleAsync(ExporterDossier query, CancellationToken cancellationToken)
    {
        var dossier = await garde.EcrireAsync(query.DossierId, PartiesDossier.Export, ActionAudit.Export, cancellationToken);
        return dossier.IsSuccess ? await export.ConstruireAsync(dossier.Value, cancellationToken) : dossier.Error!;
    }
}
