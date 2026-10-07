using Microsoft.EntityFrameworkCore;

using Sepp.Planification.Application;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Convocations;
using Sepp.Planification.Domain.Projections;
using Sepp.Planification.Domain.Ressources;
using Sepp.Planification.Domain.Sessions;

namespace Sepp.Planification.Adapters.Persistence;

internal sealed class LieuRepository(PlanificationDbContext db) : ILieuRepository
{
    public Task<Lieu?> GetAsync(Guid id, CancellationToken cancellationToken) => db.Lieux.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Lieu>> ListAsync(CancellationToken cancellationToken) => await db.Lieux.ToListAsync(cancellationToken);

    public void Add(Lieu lieu) => db.Lieux.Add(lieu);
}

internal sealed class RessourceRepository(PlanificationDbContext db) : IRessourceRepository
{
    public Task<Ressource?> GetAsync(Guid id, CancellationToken cancellationToken) => db.Ressources.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Ressource>> ListAsync(TypeRessource? type, CancellationToken cancellationToken) =>
        await db.Ressources.Where(r => type == null || r.Type == type).ToListAsync(cancellationToken);

    public void Add(Ressource ressource) => db.Ressources.Add(ressource);
}

internal sealed class AbsenceRepository(PlanificationDbContext db) : IAbsenceRepository
{
    public Task<Absence?> GetParReferenceAsync(SourceAbsence source, string referenceExterne, CancellationToken cancellationToken) =>
        db.Absences.SingleOrDefaultAsync(a => a.Source == source && a.ReferenceExterne == referenceExterne, cancellationToken);

    public async Task<IReadOnlyList<Absence>> ListAsync(Guid? ressourceId, DateTimeOffset du, DateTimeOffset au, CancellationToken cancellationToken) =>
        await db.Absences.Where(a => (ressourceId == null || a.RessourceId == ressourceId) && a.Debut < au.ToUniversalTime() && a.Fin > du.ToUniversalTime()).ToListAsync(cancellationToken);

    public void Add(Absence absence) => db.Absences.Add(absence);
}

internal sealed class ModeleAgendaRepository(PlanificationDbContext db) : IModeleAgendaRepository
{
    public Task<ModeleAgenda?> GetAsync(Guid id, CancellationToken cancellationToken) => db.ModelesAgenda.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ModeleAgenda>> ListAsync(Guid? ressourceId, CancellationToken cancellationToken) =>
        await db.ModelesAgenda.Where(m => ressourceId == null || m.RessourceId == ressourceId).ToListAsync(cancellationToken);

    public void Add(ModeleAgenda modele) => db.ModelesAgenda.Add(modele);
}

internal sealed class DureeStandardRepository(PlanificationDbContext db) : IDureeStandardRepository
{
    public async Task<IReadOnlyList<DureeStandard>> ListAsync(string? typeActe, CancellationToken cancellationToken) =>
        await db.DureesStandard.Where(d => typeActe == null || d.TypeActe == typeActe).ToListAsync(cancellationToken);

    public void Add(DureeStandard duree) => db.DureesStandard.Add(duree);
}

internal sealed class CreneauRepository(PlanificationDbContext db) : ICreneauRepository
{
    public Task<Creneau?> GetAsync(Guid id, CancellationToken cancellationToken) => db.Creneaux.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Creneau>> RechercherAsync(CritereCreneaux critere, CancellationToken cancellationToken)
    {
        var (du, au) = (critere.Du.ToUniversalTime(), critere.Au.ToUniversalTime());
        var requete = db.Creneaux.Where(c => c.Debut >= du && c.Debut < au);
        if (critere.Ressource is { } ressource)
        {
            requete = requete.Where(c => c.Occupations.Any(o => o.RessourceId == ressource));
        }

        if (critere.LieuId is { } lieu)
        {
            requete = requete.Where(c => c.LieuId == lieu);
        }

        if (critere.SessionId is { } session)
        {
            requete = requete.Where(c => c.SessionId == session);
        }

        if (critere.TypeActe is { } typeActe)
        {
            requete = requete.Where(c => c.TypeActe == typeActe);
        }

        if (critere.Statut is { } statut)
        {
            requete = requete.Where(c => c.Statut == statut);
        }

        if (critere.ReserveUrgence is { } urgence)
        {
            requete = requete.Where(c => c.ReserveUrgence == urgence);
        }

        if (critere.OuvertEnLigne is { } enLigne)
        {
            requete = requete.Where(c => c.OuvertEnLigne == enLigne);
        }

        return await requete.OrderBy(c => c.Debut).ThenBy(c => c.Id).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Creneau>> ChevauchantsAsync(IReadOnlyCollection<Guid> ressources, DateTimeOffset du, DateTimeOffset au, CancellationToken cancellationToken)
    {
        var ids = ressources.ToArray();
        (du, au) = (du.ToUniversalTime(), au.ToUniversalTime());
        return await db.Creneaux
            .Where(c => c.Occupations.Any(o => ids.Contains(o.RessourceId) && o.Debut < au && o.Fin > du))
            .ToListAsync(cancellationToken);
    }

    public void Add(Creneau creneau) => db.Creneaux.Add(creneau);

    public void Remove(Creneau creneau) => db.Creneaux.Remove(creneau);
}

internal sealed class RendezVousRepository(PlanificationDbContext db) : IRendezVousRepository
{
    public Task<RendezVous?> GetAsync(Guid id, CancellationToken cancellationToken) => db.RendezVous.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<RendezVous>> RechercherAsync(CritereRendezVous critere, CancellationToken cancellationToken)
    {
        var requete = db.RendezVous.AsQueryable();
        if (critere.PersonneId is { } personne)
        {
            requete = requete.Where(r => r.PersonneId == personne);
        }

        if (critere.AffilieId is { } affilie)
        {
            requete = requete.Where(r => r.AffilieId == affilie);
        }

        if (critere.RessourceId is { } ressource)
        {
            requete = requete.Where(r => r.RessourceId == ressource);
        }

        if (critere.LieuId is { } lieu)
        {
            requete = requete.Where(r => r.LieuId == lieu);
        }

        if (critere.Du?.ToUniversalTime() is { } du)
        {
            requete = requete.Where(r => r.Debut >= du);
        }

        if (critere.Au?.ToUniversalTime() is { } au)
        {
            requete = requete.Where(r => r.Debut < au);
        }

        if (critere.Statuts is { } statuts)
        {
            var liste = statuts.ToArray();
            requete = requete.Where(r => liste.Contains(r.Statut));
        }

        if (critere.CreneauIds is { } creneaux)
        {
            var liste = creneaux.ToArray();
            requete = requete.Where(r => liste.Contains(r.CreneauId));
        }

        return await requete.OrderBy(r => r.Debut).ThenBy(r => r.Id).ToListAsync(cancellationToken);
    }

    public void Add(RendezVous rendezVous) => db.RendezVous.Add(rendezVous);
}

internal sealed class ConvocationRepository(PlanificationDbContext db) : IConvocationRepository
{
    public async Task<IReadOnlyList<Convocation>> ListAsync(Guid rendezVousId, CancellationToken cancellationToken) =>
        db.Convocations.Local.Where(c => c.RendezVousId == rendezVousId)
            .Union(await db.Convocations.Where(c => c.RendezVousId == rendezVousId).ToListAsync(cancellationToken))
            .ToList();

    public void Add(Convocation convocation) => db.Convocations.Add(convocation);
}

internal sealed class PreferenceConvocationRepository(PlanificationDbContext db) : IPreferenceConvocationRepository
{
    public Task<PreferenceConvocation?> GetAsync(Guid affilieId, CancellationToken cancellationToken) =>
        db.PreferencesConvocation.SingleOrDefaultAsync(p => p.AffilieId == affilieId, cancellationToken);

    public void Add(PreferenceConvocation preference) => db.PreferencesConvocation.Add(preference);
}

internal sealed class SessionRepository(PlanificationDbContext db) : ISessionRepository
{
    public Task<Session?> GetAsync(Guid id, CancellationToken cancellationToken) => db.Sessions.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Session>> ListAsync(DateOnly du, DateOnly au, CancellationToken cancellationToken) =>
        await db.Sessions.Where(s => s.Date >= du && s.Date <= au).ToListAsync(cancellationToken);

    public void Add(Session session) => db.Sessions.Add(session);
}

internal sealed class ObligationRepository(PlanificationDbContext db) : IObligationRepository
{
    public async Task<ObligationAPlanifier?> GetAsync(Guid obligationId, CancellationToken cancellationToken) =>
        await db.Obligations.FindAsync([obligationId], cancellationToken);

    /// <summary>Obligations suivies (y compris celles ajoutées dans la transaction en cours, pas encore enregistrées).</summary>
    public async Task<IReadOnlyList<ObligationAPlanifier>> ListAsync(IReadOnlyCollection<Guid> obligationIds, CancellationToken cancellationToken)
    {
        var ids = obligationIds.Distinct().ToArray();
        var enBase = await db.Obligations.Where(o => ids.Contains(o.ObligationId)).ToListAsync(cancellationToken);
        return db.Obligations.Local.Where(o => ids.Contains(o.ObligationId)).Union(enBase).ToList();
    }

    public async Task<IReadOnlyList<ObligationAPlanifier>> ListerAPlanifierAsync(IReadOnlyCollection<Guid>? affilieIds, DateOnly horizon, CancellationToken cancellationToken)
    {
        var affilies = affilieIds?.ToArray();
        return await db.Obligations
            .Where(o => o.RendezVousId == null && o.DateDue <= horizon && (affilies == null || affilies.Contains(o.AffilieId)))
            .OrderBy(o => o.DateDue).ThenBy(o => o.ObligationId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ObligationAPlanifier>> ListerParPersonneAsync(Guid personneId, CancellationToken cancellationToken) =>
        await db.Obligations.Where(o => o.PersonneId == personneId).ToListAsync(cancellationToken);

    public void Add(ObligationAPlanifier obligation) => db.Obligations.Add(obligation);
}

internal sealed class ParametreLocalRepository(PlanificationDbContext db) : IParametreLocalRepository
{
    public async Task<ParametreLegalLocal?> GetAsync(string code, DateOnly valideDu, CancellationToken cancellationToken) =>
        await db.ParametresLegaux.FindAsync([code, valideDu], cancellationToken);

    public Task<ParametreLegalLocal?> ApplicableAsync(string code, DateOnly date, CancellationToken cancellationToken) =>
        db.ParametresLegaux
            .Where(p => p.Code == code && p.ValideDu <= date && (p.ValideJusquAu == null || date < p.ValideJusquAu))
            .OrderByDescending(p => p.ValideDu)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(ParametreLegalLocal parametre) => db.ParametresLegaux.Add(parametre);
}
