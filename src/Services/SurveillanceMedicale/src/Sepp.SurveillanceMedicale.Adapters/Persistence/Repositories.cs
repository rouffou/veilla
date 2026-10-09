using Microsoft.EntityFrameworkCore;

using Sepp.SurveillanceMedicale.Application;
using Sepp.SurveillanceMedicale.Domain.Conservation;
using Sepp.SurveillanceMedicale.Domain.Decisions;
using Sepp.SurveillanceMedicale.Domain.Dossiers;
using Sepp.SurveillanceMedicale.Domain.Examens;
using Sepp.SurveillanceMedicale.Domain.MaladiesProfessionnelles;
using Sepp.SurveillanceMedicale.Domain.Projections;
using Sepp.SurveillanceMedicale.Domain.Protocoles;
using Sepp.SurveillanceMedicale.Domain.Transferts;
using Sepp.SurveillanceMedicale.Domain.Vaccinations;

namespace Sepp.SurveillanceMedicale.Adapters.Persistence;

internal sealed class DossierSanteRepository(SurveillanceMedicaleDbContext db) : IDossierSanteRepository
{
    public Task<DossierSante?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Dossiers.AsSplitQuery().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<DossierSante?> GetParPersonneAsync(Guid personneId, CancellationToken cancellationToken) =>
        db.Dossiers.AsSplitQuery().SingleOrDefaultAsync(d => d.PersonneId == personneId, cancellationToken);

    public async Task<IReadOnlyList<DossierSante>> ListerRattachesAuGroupeAsync(Guid groupeExpositionId, CancellationToken cancellationToken) =>
        await db.Dossiers.AsSplitQuery().Where(d => d.GroupesExposition.Any(g => g.GroupeExpositionId == groupeExpositionId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DossierSante>> ListerParStatutAsync(StatutArchivage statut, CancellationToken cancellationToken) =>
        await db.Dossiers.AsSplitQuery().Where(d => d.StatutArchivage == statut).ToListAsync(cancellationToken);

    public void Add(DossierSante dossier) => db.Dossiers.Add(dossier);
}

internal sealed class ExamenRepository(SurveillanceMedicaleDbContext db) : IExamenRepository
{
    public Task<Examen?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Examens.AsSplitQuery().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Examen>> ListerParDossierAsync(Guid dossierId, CancellationToken cancellationToken) =>
        await db.Examens.AsSplitQuery().Where(e => e.DossierId == dossierId).ToListAsync(cancellationToken);

    public Task<bool> ExisteExamenDuProfessionnelAsync(Guid dossierId, string professionnelId, CancellationToken cancellationToken) =>
        db.Examens.IgnoreAutoIncludes().AnyAsync(e => e.DossierId == dossierId && e.ProfessionnelId == professionnelId, cancellationToken);

    public void Add(Examen examen) => db.Examens.Add(examen);
}

internal sealed class DecisionRepository(SurveillanceMedicaleDbContext db) : IDecisionRepository
{
    public Task<Decision?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Decisions.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<Decision?> GetParExamenAsync(Guid examenId, CancellationToken cancellationToken) =>
        db.Decisions.SingleOrDefaultAsync(d => d.ExamenId == examenId, cancellationToken);

    public async Task<IReadOnlyList<Decision>> ListerParDossierAsync(Guid dossierId, CancellationToken cancellationToken) =>
        await db.Decisions.Where(d => d.DossierId == dossierId).ToListAsync(cancellationToken);

    public void Add(Decision decision) => db.Decisions.Add(decision);
}

internal sealed class ProtocolesRepository(SurveillanceMedicaleDbContext db) : IProtocolesRepository
{
    public async Task<IReadOnlyList<ValeurReference>> ListerValeursReferenceAsync(CancellationToken cancellationToken) =>
        await db.ValeursReference.ToListAsync(cancellationToken);

    public Task<ValeurReference?> GetValeurReferenceAsync(Guid id, CancellationToken cancellationToken) =>
        db.ValeursReference.SingleOrDefaultAsync(v => v.Id == id, cancellationToken);

    public void Add(ValeurReference valeur) => db.ValeursReference.Add(valeur);

    public async Task<IReadOnlyList<SchemaVaccinal>> ListerSchemasVaccinauxAsync(CancellationToken cancellationToken) =>
        await db.SchemasVaccinaux.ToListAsync(cancellationToken);

    public void Add(SchemaVaccinal schema) => db.SchemasVaccinaux.Add(schema);

    public Task<ModeleQuestionnaire?> GetModeleQuestionnaireAsync(string code, CancellationToken cancellationToken) =>
        db.ModelesQuestionnaire.Where(m => m.Code == code).OrderByDescending(m => m.Version).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ModeleQuestionnaire>> ListerModelesQuestionnaireAsync(CancellationToken cancellationToken) =>
        await db.ModelesQuestionnaire.ToListAsync(cancellationToken);

    public void Add(ModeleQuestionnaire modele) => db.ModelesQuestionnaire.Add(modele);

    public async Task<IReadOnlyList<ModeleTexte>> ListerModelesTexteAsync(string cpmtId, CancellationToken cancellationToken) =>
        await db.ModelesTexte.Where(m => m.CpmtId == cpmtId).ToListAsync(cancellationToken);

    public Task<ModeleTexte?> GetModeleTexteAsync(Guid id, CancellationToken cancellationToken) =>
        db.ModelesTexte.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);

    public void Add(ModeleTexte modele) => db.ModelesTexte.Add(modele);

    public void Remove(ModeleTexte modele) => db.ModelesTexte.Remove(modele);

    public async Task<IReadOnlyList<DureeConservationExposition>> ListerDureesConservationAsync(CancellationToken cancellationToken) =>
        await db.DureesConservation.ToListAsync(cancellationToken);

    public void Add(DureeConservationExposition duree) => db.DureesConservation.Add(duree);
}

internal sealed class LotVaccinRepository(SurveillanceMedicaleDbContext db) : ILotVaccinRepository
{
    public Task<LotVaccin?> GetAsync(Guid id, CancellationToken cancellationToken) => db.LotsVaccins.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);

    public async Task<IReadOnlyList<LotVaccin>> ListerParCentreAsync(Guid centreId, CancellationToken cancellationToken) =>
        await db.LotsVaccins.Where(l => l.CentreId == centreId).ToListAsync(cancellationToken);

    public void Add(LotVaccin lot) => db.LotsVaccins.Add(lot);
}

internal sealed class DeclarationMpRepository(SurveillanceMedicaleDbContext db) : IDeclarationMpRepository
{
    public Task<DeclarationMaladieProfessionnelle?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.DeclarationsMp.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<DeclarationMaladieProfessionnelle>> ListerParDossierAsync(Guid dossierId, CancellationToken cancellationToken) =>
        await db.DeclarationsMp.Where(d => d.DossierId == dossierId).ToListAsync(cancellationToken);

    public void Add(DeclarationMaladieProfessionnelle declaration) => db.DeclarationsMp.Add(declaration);
}

internal sealed class TransfertRepository(SurveillanceMedicaleDbContext db) : ITransfertRepository
{
    public Task<TransfertDossier?> GetAsync(Guid id, CancellationToken cancellationToken) => db.Transferts.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TransfertDossier>> ListerParDossierAsync(Guid dossierId, CancellationToken cancellationToken) =>
        await db.Transferts.Where(t => t.DossierId == dossierId).ToListAsync(cancellationToken);

    public void Add(TransfertDossier transfert) => db.Transferts.Add(transfert);
}

/// <summary>
/// NF-22, DAT-05 : purge légale, seule suppression physique du service. Dans une transaction : preuve, traces et
/// événements en attente (outbox), puis suppression du dossier ; les examens, décisions, déclarations, transferts et
/// parties du dossier suivent par les clés étrangères en cascade.
/// </summary>
internal sealed class PurgeDossiers(SurveillanceMedicaleDbContext db) : IPurgeDossiers
{
    public async Task DetruireAsync(DossierSante dossier, PreuveDestruction preuve, CancellationToken cancellationToken)
    {
        var strategie = db.Database.CreateExecutionStrategy();
        await strategie.ExecuteAsync(async ct =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.PreuvesDestruction.Add(preuve);
            await db.SaveChangesAsync(ct);
            await db.Dossiers.IgnoreQueryFilters().Where(d => d.Id == dossier.Id).ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);
        }, cancellationToken);
        db.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<PreuveDestruction>> ListerPreuvesAsync(CancellationToken cancellationToken) =>
        await db.PreuvesDestruction.ToListAsync(cancellationToken);
}

internal sealed class ProjectionRepository(SurveillanceMedicaleDbContext db) : IProjectionRepository
{
    public Task<ObligationDue?> GetObligationAsync(Guid obligationId, CancellationToken cancellationToken) =>
        db.Obligations.SingleOrDefaultAsync(o => o.ObligationId == obligationId, cancellationToken);

    public async Task<IReadOnlyList<ObligationDue>> ListerObligationsAsync(Guid personneId, CancellationToken cancellationToken) =>
        await db.Obligations.Where(o => o.PersonneId == personneId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ObligationDue>> ListerObligationsParIdsAsync(IReadOnlyCollection<Guid> obligationIds, CancellationToken cancellationToken) =>
        obligationIds.Count == 0 ? [] : await db.Obligations.Where(o => obligationIds.Contains(o.ObligationId)).ToListAsync(cancellationToken);

    public void Add(ObligationDue obligation) => db.Obligations.Add(obligation);

    public Task<RendezVousPrevu?> GetRendezVousAsync(Guid rendezVousId, CancellationToken cancellationToken) =>
        db.RendezVous.SingleOrDefaultAsync(r => r.RendezVousId == rendezVousId, cancellationToken);

    public void Add(RendezVousPrevu rendezVous) => db.RendezVous.Add(rendezVous);

    public Task<AffectationPersonne?> GetAffectationAsync(Guid affectationId, CancellationToken cancellationToken) =>
        db.Affectations.SingleOrDefaultAsync(a => a.AffectationId == affectationId, cancellationToken);

    public async Task<IReadOnlyList<AffectationPersonne>> ListerAffectationsAsync(Guid personneId, CancellationToken cancellationToken) =>
        await db.Affectations.Where(a => a.PersonneId == personneId).ToListAsync(cancellationToken);

    public void Add(AffectationPersonne affectation) => db.Affectations.Add(affectation);

    public Task<ProfilRisquePoste?> GetProfilAsync(Guid posteId, DateOnly valideDu, CancellationToken cancellationToken) =>
        db.ProfilsRisques.SingleOrDefaultAsync(p => p.PosteId == posteId && p.ValideDu == valideDu, cancellationToken);

    public async Task<IReadOnlyList<ProfilRisquePoste>> ListerProfilsAsync(IReadOnlyCollection<Guid> posteIds, CancellationToken cancellationToken) =>
        posteIds.Count == 0 ? [] : await db.ProfilsRisques.Where(p => posteIds.Contains(p.PosteId)).ToListAsync(cancellationToken);

    public void Add(ProfilRisquePoste profil) => db.ProfilsRisques.Add(profil);

    public Task<MesurageExposition?> GetMesurageAsync(Guid mesurageId, CancellationToken cancellationToken) =>
        db.Mesurages.SingleOrDefaultAsync(m => m.MesurageId == mesurageId, cancellationToken);

    public async Task<IReadOnlyList<MesurageExposition>> ListerMesuragesAsync(Guid groupeExpositionId, CancellationToken cancellationToken) =>
        await db.Mesurages.Where(m => m.GroupeExpositionId == groupeExpositionId).ToListAsync(cancellationToken);

    public void Add(MesurageExposition mesurage) => db.Mesurages.Add(mesurage);

    public Task<ParametreLegalLocal?> GetParametreAsync(string code, DateOnly valideDu, CancellationToken cancellationToken) =>
        db.Parametres.SingleOrDefaultAsync(p => p.Code == code && p.ValideDu == valideDu, cancellationToken);

    public Task<ParametreLegalLocal?> ParametreApplicableAsync(string code, DateOnly date, CancellationToken cancellationToken) =>
        db.Parametres.Where(p => p.Code == code && p.ValideDu <= date && (p.ValideJusquAu == null || date < p.ValideJusquAu))
            .OrderByDescending(p => p.ValideDu).FirstOrDefaultAsync(cancellationToken);

    public void Add(ParametreLegalLocal parametre) => db.Parametres.Add(parametre);

    public Task<CalendrierLocal?> GetCalendrierAsync(int annee, CancellationToken cancellationToken) =>
        db.Calendriers.SingleOrDefaultAsync(c => c.Annee == annee, cancellationToken);

    public async Task<IReadOnlyList<CalendrierLocal>> ListerCalendriersAsync(int anneeDebut, int anneeFin, CancellationToken cancellationToken) =>
        await db.Calendriers.Where(c => c.Annee >= anneeDebut && c.Annee <= anneeFin).ToListAsync(cancellationToken);

    public void Add(CalendrierLocal calendrier) => db.Calendriers.Add(calendrier);
}
