using Microsoft.EntityFrameworkCore;

using Sepp.Obligations.Application;
using Sepp.Obligations.Domain.Demandes;
using Sepp.Obligations.Domain.Obligations;
using Sepp.Obligations.Domain.Projections;

namespace Sepp.Obligations.Adapters.Persistence;

internal sealed class ObligationRepository(ObligationsDbContext db) : IObligationRepository
{
    private static readonly StatutObligation[] Ouverts = [.. Enum.GetValues<StatutObligation>().Where(MachineEtatsObligation.EstOuvert)];

    public Task<Obligation?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Obligations.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Obligation>> ListParPersonneAsync(Guid personneId, CancellationToken cancellationToken) =>
        await db.Obligations.Where(o => o.PersonneId == personneId).AsSplitQuery().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Obligation>> ListParAffilieAsync(Guid affilieId, bool ouvertesSeulement, CancellationToken cancellationToken) =>
        await db.Obligations.IgnoreAutoIncludes()
            .Where(o => o.AffilieId == affilieId && (!ouvertesSeulement || Ouverts.Contains(o.Statut)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> PersonnesAvecObligationsOuvertesAsync(CancellationToken cancellationToken) =>
        await db.Obligations.IgnoreAutoIncludes().Where(o => Ouverts.Contains(o.Statut)).Select(o => o.PersonneId).Distinct().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Obligation>> ListEchuesNonSignaleesAsync(DateOnly aujourdHui, int nombreMaximum, CancellationToken cancellationToken) =>
        await db.Obligations.IgnoreAutoIncludes()
            .Where(o => Ouverts.Contains(o.Statut) && o.DateLimite != null && o.DateLimite < aujourdHui && o.EchueSignaleeLe == null)
            .OrderBy(o => o.DateLimite)
            .ThenBy(o => o.Id)
            .Take(nombreMaximum)
            .ToListAsync(cancellationToken);

    public void Add(Obligation obligation) => db.Obligations.Add(obligation);
}

internal sealed class DemandeRepository(ObligationsDbContext db) : IDemandeRepository
{
    public async Task<IReadOnlyList<DemandeTravailleur>> ListParPersonneAsync(Guid personneId, CancellationToken cancellationToken)
    {
        var enregistrees = await db.Demandes.Where(d => d.PersonneId == personneId).ToListAsync(cancellationToken);

        // Une demande ajoutée dans la transaction en cours compte tout de suite (un seul enregistrement, atomique).
        return [.. enregistrees.Concat(db.Demandes.Local.Where(d => d.PersonneId == personneId && !enregistrees.Contains(d)))];
    }

    public async Task<IReadOnlyList<Guid>> PersonnesAsync(CancellationToken cancellationToken) =>
        await db.Demandes.Select(d => d.PersonneId).Distinct().ToListAsync(cancellationToken);

    public void Add(DemandeTravailleur demande) => db.Demandes.Add(demande);
}

internal sealed class ProjectionRepository(ObligationsDbContext db) : IProjectionRepository
{
    public Task<AffectationLocale?> GetAffectationAsync(Guid affectationId, CancellationToken cancellationToken) =>
        db.Affectations.SingleOrDefaultAsync(a => a.AffectationId == affectationId, cancellationToken);

    public async Task<IReadOnlyList<AffectationLocale>> AffectationsDeAsync(Guid personneId, CancellationToken cancellationToken) =>
        await db.Affectations.Where(a => a.PersonneId == personneId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AffectationLocale>> AffectationsActivesAsync(
        IReadOnlyCollection<Guid> personneIds, DateOnly date, CancellationToken cancellationToken) =>
        await db.Affectations
            .Where(a => personneIds.Contains(a.PersonneId) && a.DateDebut <= date && (a.DateFin == null || date < a.DateFin))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> PersonnesAffecteesAsync(IReadOnlyCollection<Guid> posteIds, CancellationToken cancellationToken) =>
        await db.Affectations.Where(a => posteIds.Contains(a.PosteId)).Select(a => a.PersonneId).Distinct().ToListAsync(cancellationToken);

    public void Add(AffectationLocale affectation) => db.Affectations.Add(affectation);

    public Task<ProfilRisquePosteLocal?> GetProfilAsync(Guid posteId, DateOnly valideDu, CancellationToken cancellationToken) =>
        db.Profils.SingleOrDefaultAsync(p => p.PosteId == posteId && p.ValideDu == valideDu, cancellationToken);

    public async Task<IReadOnlyList<ProfilRisquePosteLocal>> ProfilsDesPostesAsync(IReadOnlyCollection<Guid> posteIds, CancellationToken cancellationToken) =>
        await db.Profils.Where(p => posteIds.Contains(p.PosteId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> PostesExposesAuRisqueAsync(string codeRisque, CancellationToken cancellationToken) =>
        await db.Profils.Where(p => p.CodesRisques.Contains(codeRisque)).Select(p => p.PosteId).Distinct().ToListAsync(cancellationToken);

    public void Add(ProfilRisquePosteLocal profil) => db.Profils.Add(profil);

    public Task<RegleSurveillanceLocale?> GetRegleAsync(string codeRisque, int version, CancellationToken cancellationToken) =>
        db.Regles.SingleOrDefaultAsync(r => r.CodeRisque == codeRisque && r.Version == version, cancellationToken);

    public async Task<IReadOnlyList<RegleSurveillanceLocale>> ReglesAsync(IReadOnlyCollection<string> codesRisques, CancellationToken cancellationToken) =>
        await db.Regles.Where(r => codesRisques.Contains(r.CodeRisque)).ToListAsync(cancellationToken);

    public void Add(RegleSurveillanceLocale regle) => db.Regles.Add(regle);

    public Task<SurchargeFrequenceLocale?> GetSurchargeAsync(Guid surchargeId, CancellationToken cancellationToken) =>
        db.Surcharges.SingleOrDefaultAsync(s => s.SurchargeId == surchargeId, cancellationToken);

    public async Task<IReadOnlyList<SurchargeFrequenceLocale>> SurchargesAsync(
        Guid personneId, IReadOnlyCollection<Guid> posteIds, CancellationToken cancellationToken) =>
        await db.Surcharges.Where(s => s.CibleId == personneId || posteIds.Contains(s.CibleId)).ToListAsync(cancellationToken);

    public void Add(SurchargeFrequenceLocale surcharge) => db.Surcharges.Add(surcharge);

    public Task<OccupationLocale?> GetOccupationAsync(Guid occupationId, CancellationToken cancellationToken) =>
        db.Occupations.SingleOrDefaultAsync(o => o.OccupationId == occupationId, cancellationToken);

    public async Task<IReadOnlyList<OccupationLocale>> OccupationsDeAsync(Guid personneId, CancellationToken cancellationToken) =>
        await db.Occupations.Where(o => o.PersonneId == personneId).ToListAsync(cancellationToken);

    public async Task<bool> OccupationActiveAsync(Guid personneId, Guid affilieId, DateOnly date, CancellationToken cancellationToken) =>
        await db.Occupations.AnyAsync(
            o => o.PersonneId == personneId && o.AffilieId == affilieId && (o.DateDebut == null || o.DateDebut <= date) && (o.DateFin == null || o.DateFin >= date),
            cancellationToken);

    public async Task<IReadOnlyList<Guid>> PersonnesOccupeesAsync(Guid affilieId, DateOnly date, CancellationToken cancellationToken) =>
        await db.Occupations
            .Where(o => o.AffilieId == affilieId && (o.DateDebut == null || o.DateDebut <= date) && (o.DateFin == null || o.DateFin >= date))
            .Select(o => o.PersonneId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public void Add(OccupationLocale occupation) => db.Occupations.Add(occupation);

    public Task<EtatParticulierLocal?> GetEtatParticulierAsync(Guid etatParticulierId, CancellationToken cancellationToken) =>
        db.EtatsParticuliers.SingleOrDefaultAsync(e => e.EtatParticulierId == etatParticulierId, cancellationToken);

    public async Task<IReadOnlyList<EtatParticulierLocal>> EtatsParticuliersDeAsync(Guid personneId, CancellationToken cancellationToken) =>
        await db.EtatsParticuliers.Where(e => e.PersonneId == personneId).ToListAsync(cancellationToken);

    public void Add(EtatParticulierLocal etat) => db.EtatsParticuliers.Add(etat);

    public Task<ExamenLocal?> GetExamenAsync(Guid examenId, CancellationToken cancellationToken) =>
        db.Examens.SingleOrDefaultAsync(e => e.ExamenId == examenId, cancellationToken);

    public async Task<IReadOnlyList<ExamenLocal>> ExamensDeAsync(Guid personneId, CancellationToken cancellationToken) =>
        await db.Examens.Where(e => e.PersonneId == personneId).ToListAsync(cancellationToken);

    public void Add(ExamenLocal examen) => db.Examens.Add(examen);

    public Task<ParametreLegalLocal?> GetParametreAsync(string code, DateOnly valideDu, CancellationToken cancellationToken) =>
        db.Parametres.SingleOrDefaultAsync(p => p.Code == code && p.ValideDu == valideDu, cancellationToken);

    public async Task<IReadOnlyList<ParametreLegalLocal>> ParametresAsync(CancellationToken cancellationToken) =>
        await db.Parametres.ToListAsync(cancellationToken);

    public void Add(ParametreLegalLocal parametre) => db.Parametres.Add(parametre);

    public Task<CalendrierLocal?> GetCalendrierAsync(int annee, CancellationToken cancellationToken) =>
        db.Calendriers.SingleOrDefaultAsync(c => c.Annee == annee, cancellationToken);

    public async Task<IReadOnlyList<CalendrierLocal>> CalendriersAsync(CancellationToken cancellationToken) =>
        await db.Calendriers.ToListAsync(cancellationToken);

    public void Add(CalendrierLocal calendrier) => db.Calendriers.Add(calendrier);

    public Task<RendezVousLocal?> GetRendezVousAsync(Guid rendezVousId, CancellationToken cancellationToken) =>
        db.RendezVous.SingleOrDefaultAsync(r => r.RendezVousId == rendezVousId, cancellationToken);

    public async Task<IReadOnlyList<RendezVousLocal>> RendezVousDeAsync(Guid personneId, CancellationToken cancellationToken) =>
        await db.RendezVous.Where(r => r.PersonneId == personneId).ToListAsync(cancellationToken);

    public void Add(RendezVousLocal rendezVous) => db.RendezVous.Add(rendezVous);

    public Task<RepriseLocale?> GetRepriseAsync(Guid personneId, Guid affilieId, DateOnly dateReprise, CancellationToken cancellationToken) =>
        db.Reprises.SingleOrDefaultAsync(r => r.PersonneId == personneId && r.AffilieId == affilieId && r.DateReprise == dateReprise, cancellationToken);

    public async Task<IReadOnlyList<RepriseLocale>> ReprisesDeAsync(Guid personneId, CancellationToken cancellationToken) =>
        await db.Reprises.Where(r => r.PersonneId == personneId).ToListAsync(cancellationToken);

    public void Add(RepriseLocale reprise) => db.Reprises.Add(reprise);

    public Task<IncapaciteLocale?> GetIncapaciteAsync(Guid incapaciteId, CancellationToken cancellationToken) =>
        db.Incapacites.SingleOrDefaultAsync(i => i.IncapaciteId == incapaciteId, cancellationToken);

    public async Task<IReadOnlyList<IncapaciteLocale>> IncapacitesDeAsync(Guid personneId, CancellationToken cancellationToken) =>
        await db.Incapacites.Where(i => i.PersonneId == personneId).ToListAsync(cancellationToken);

    public void Add(IncapaciteLocale incapacite) => db.Incapacites.Add(incapacite);

    public Task<TrajetLocal?> GetTrajetAsync(Guid trajetId, CancellationToken cancellationToken) =>
        db.Trajets.SingleOrDefaultAsync(t => t.TrajetId == trajetId, cancellationToken);

    public async Task<IReadOnlyList<TrajetLocal>> TrajetsDeAsync(Guid personneId, CancellationToken cancellationToken) =>
        await db.Trajets.Where(t => t.PersonneId == personneId).ToListAsync(cancellationToken);

    public void Add(TrajetLocal trajet) => db.Trajets.Add(trajet);

    public async Task<IReadOnlyList<Guid>> PersonnesAvecEvenementsAsync(CancellationToken cancellationToken) =>
        await db.Reprises.Select(r => r.PersonneId)
            .Union(db.Incapacites.Select(i => i.PersonneId))
            .Union(db.Trajets.Select(t => t.PersonneId))
            .ToListAsync(cancellationToken);

    public Task<ListeNominativeLocale?> GetListeNominativeAsync(Guid listeNominativeId, CancellationToken cancellationToken) =>
        db.ListesNominatives.SingleOrDefaultAsync(l => l.ListeNominativeId == listeNominativeId, cancellationToken);

    public async Task<IReadOnlyList<ListeNominativeLocale>> ListesNominativesAsync(Guid affilieId, CancellationToken cancellationToken) =>
        await db.ListesNominatives.Where(l => l.AffilieId == affilieId).ToListAsync(cancellationToken);

    public void Add(ListeNominativeLocale liste) => db.ListesNominatives.Add(liste);
}
