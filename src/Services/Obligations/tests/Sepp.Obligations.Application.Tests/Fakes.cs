using Microsoft.Extensions.Time.Testing;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts;
using Sepp.Obligations.Application;
using Sepp.Obligations.Application.Calcul;
using Sepp.Obligations.Application.Reprises;
using Sepp.Obligations.Domain.Calcul;
using Sepp.Obligations.Domain.Demandes;
using Sepp.Obligations.Domain.Obligations;
using Sepp.Obligations.Domain.Projections;
using Sepp.Obligations.Domain.Reprises;

namespace Sepp.Obligations.Application.Tests;

/// <summary>Adaptateurs en mémoire : la couche application est testée sans infrastructure (ARC-23).</summary>
internal sealed class InMemoryStore : IUnitOfWork, IIntegrationEventOutbox, IObligationRepository, IDemandeRepository, IProjectionRepository, IProcessusRepriseRepository, IDecisionRecueRepository
{
    private readonly List<IntegrationEvent> _pending = [];

    public List<Obligation> Obligations { get; } = [];

    public List<DemandeTravailleur> Demandes { get; } = [];

    public List<AffectationLocale> Affectations { get; } = [];

    public List<ProfilRisquePosteLocal> Profils { get; } = [];

    public List<RegleSurveillanceLocale> Regles { get; } = [];

    public List<SurchargeFrequenceLocale> Surcharges { get; } = [];

    public List<OccupationLocale> Occupations { get; } = [];

    public List<EtatParticulierLocal> Etats { get; } = [];

    public List<ExamenLocal> Examens { get; } = [];

    public List<ParametreLegalLocal> Parametres { get; } = [];

    public List<CalendrierLocal> Calendriers { get; } = [];

    public List<RendezVousLocal> RendezVous { get; } = [];

    public List<RepriseLocale> Reprises { get; } = [];

    public List<IncapaciteLocale> Incapacites { get; } = [];

    public List<TrajetLocal> Trajets { get; } = [];

    public List<ListeNominativeLocale> Listes { get; } = [];

    public List<IntegrationEvent> Published { get; } = [];

    public int Saves { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        Published.AddRange(_pending);
        _pending.Clear();
        return Task.CompletedTask;
    }

    public void Add(IntegrationEvent integrationEvent) => _pending.Add(integrationEvent);

    public IEnumerable<T> Publies<T>() => Published.OfType<T>();

    // Obligations
    Task<Obligation?> IObligationRepository.GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Obligations.SingleOrDefault(o => o.Id == id));

    public Task<IReadOnlyList<Obligation>> ListParPersonneAsync(Guid personneId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Obligation>>(Obligations.Where(o => o.PersonneId == personneId).ToList());

    public Task<IReadOnlyList<Obligation>> ListParAffilieAsync(Guid affilieId, bool ouvertesSeulement, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Obligation>>(Obligations.Where(o => o.AffilieId == affilieId && (!ouvertesSeulement || o.EstOuverte)).ToList());

    public Task<IReadOnlyList<Guid>> PersonnesAvecObligationsOuvertesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Obligations.Where(o => o.EstOuverte).Select(o => o.PersonneId).Distinct().ToList());

    public Task<IReadOnlyList<Obligation>> ListEchuesNonSignaleesAsync(DateOnly aujourdHui, int nombreMaximum, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Obligation>>(Obligations
            .Where(o => o.EstEnRetardAu(aujourdHui) && o.EchueSignaleeLe is null)
            .OrderBy(o => o.DateLimite)
            .Take(nombreMaximum)
            .ToList());

    public void Add(Obligation obligation) => Obligations.Add(obligation);

    // Demandes
    Task<IReadOnlyList<DemandeTravailleur>> IDemandeRepository.ListParPersonneAsync(Guid personneId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DemandeTravailleur>>(Demandes.Where(d => d.PersonneId == personneId).ToList());

    public Task<IReadOnlyList<Guid>> PersonnesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Demandes.Select(d => d.PersonneId).Distinct().ToList());

    public void Add(DemandeTravailleur demande) => Demandes.Add(demande);

    // Projections
    public Task<AffectationLocale?> GetAffectationAsync(Guid affectationId, CancellationToken cancellationToken) =>
        Task.FromResult(Affectations.SingleOrDefault(a => a.AffectationId == affectationId));

    public Task<IReadOnlyList<AffectationLocale>> AffectationsDeAsync(Guid personneId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AffectationLocale>>(Affectations.Where(a => a.PersonneId == personneId).ToList());

    public Task<IReadOnlyList<AffectationLocale>> AffectationsActivesAsync(IReadOnlyCollection<Guid> personneIds, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AffectationLocale>>(Affectations.Where(a => personneIds.Contains(a.PersonneId) && a.EstActiveAu(date)).ToList());

    public Task<IReadOnlyList<Guid>> PersonnesAffecteesAsync(IReadOnlyCollection<Guid> posteIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Affectations.Where(a => posteIds.Contains(a.PosteId)).Select(a => a.PersonneId).Distinct().ToList());

    public void Add(AffectationLocale affectation) => Affectations.Add(affectation);

    public Task<ProfilRisquePosteLocal?> GetProfilAsync(Guid posteId, DateOnly valideDu, CancellationToken cancellationToken) =>
        Task.FromResult(Profils.SingleOrDefault(p => p.PosteId == posteId && p.ValideDu == valideDu));

    public Task<IReadOnlyList<ProfilRisquePosteLocal>> ProfilsDesPostesAsync(IReadOnlyCollection<Guid> posteIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProfilRisquePosteLocal>>(Profils.Where(p => posteIds.Contains(p.PosteId)).ToList());

    public Task<IReadOnlyList<Guid>> PostesExposesAuRisqueAsync(string codeRisque, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Profils.Where(p => p.CodesRisques.Contains(codeRisque)).Select(p => p.PosteId).Distinct().ToList());

    public void Add(ProfilRisquePosteLocal profil) => Profils.Add(profil);

    public Task<RegleSurveillanceLocale?> GetRegleAsync(string codeRisque, int version, CancellationToken cancellationToken) =>
        Task.FromResult(Regles.SingleOrDefault(r => r.CodeRisque == codeRisque && r.Version == version));

    public Task<IReadOnlyList<RegleSurveillanceLocale>> ReglesAsync(IReadOnlyCollection<string> codesRisques, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RegleSurveillanceLocale>>(Regles.Where(r => codesRisques.Contains(r.CodeRisque)).ToList());

    public void Add(RegleSurveillanceLocale regle) => Regles.Add(regle);

    public Task<SurchargeFrequenceLocale?> GetSurchargeAsync(Guid surchargeId, CancellationToken cancellationToken) =>
        Task.FromResult(Surcharges.SingleOrDefault(s => s.SurchargeId == surchargeId));

    public Task<IReadOnlyList<SurchargeFrequenceLocale>> SurchargesAsync(Guid personneId, IReadOnlyCollection<Guid> posteIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SurchargeFrequenceLocale>>(Surcharges.Where(s => s.CibleId == personneId || posteIds.Contains(s.CibleId)).ToList());

    public void Add(SurchargeFrequenceLocale surcharge) => Surcharges.Add(surcharge);

    public Task<OccupationLocale?> GetOccupationAsync(Guid occupationId, CancellationToken cancellationToken) =>
        Task.FromResult(Occupations.SingleOrDefault(o => o.OccupationId == occupationId));

    public Task<IReadOnlyList<OccupationLocale>> OccupationsDeAsync(Guid personneId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OccupationLocale>>(Occupations.Where(o => o.PersonneId == personneId).ToList());

    public Task<IReadOnlyList<Guid>> PersonnesOccupeesAsync(Guid affilieId, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Occupations
            .Where(o => o.AffilieId == affilieId && (o.DateDebut is null || o.DateDebut <= date) && o.EstEnCoursOuAVenirAu(date))
            .Select(o => o.PersonneId)
            .Distinct()
            .ToList());

    public Task<bool> OccupationActiveAsync(Guid personneId, Guid affilieId, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult(Occupations.Any(o => o.PersonneId == personneId && o.AffilieId == affilieId && (o.DateDebut is null || o.DateDebut <= date) && o.EstEnCoursOuAVenirAu(date)));

    public void Add(OccupationLocale occupation) => Occupations.Add(occupation);

    public Task<EtatParticulierLocal?> GetEtatParticulierAsync(Guid etatParticulierId, CancellationToken cancellationToken) =>
        Task.FromResult(Etats.SingleOrDefault(e => e.EtatParticulierId == etatParticulierId));

    public Task<IReadOnlyList<EtatParticulierLocal>> EtatsParticuliersDeAsync(Guid personneId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<EtatParticulierLocal>>(Etats.Where(e => e.PersonneId == personneId).ToList());

    public void Add(EtatParticulierLocal etat) => Etats.Add(etat);

    public Task<ExamenLocal?> GetExamenAsync(Guid examenId, CancellationToken cancellationToken) =>
        Task.FromResult(Examens.SingleOrDefault(e => e.ExamenId == examenId));

    public Task<IReadOnlyList<ExamenLocal>> ExamensDeAsync(Guid personneId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ExamenLocal>>(Examens.Where(e => e.PersonneId == personneId).ToList());

    public void Add(ExamenLocal examen) => Examens.Add(examen);

    public Task<ParametreLegalLocal?> GetParametreAsync(string code, DateOnly valideDu, CancellationToken cancellationToken) =>
        Task.FromResult(Parametres.SingleOrDefault(p => p.Code == code && p.ValideDu == valideDu));

    public Task<IReadOnlyList<ParametreLegalLocal>> ParametresAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ParametreLegalLocal>>(Parametres.ToList());

    public void Add(ParametreLegalLocal parametre) => Parametres.Add(parametre);

    public Task<CalendrierLocal?> GetCalendrierAsync(int annee, CancellationToken cancellationToken) =>
        Task.FromResult(Calendriers.SingleOrDefault(c => c.Annee == annee));

    public Task<IReadOnlyList<CalendrierLocal>> CalendriersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CalendrierLocal>>(Calendriers.ToList());

    public void Add(CalendrierLocal calendrier) => Calendriers.Add(calendrier);

    public Task<RendezVousLocal?> GetRendezVousAsync(Guid rendezVousId, CancellationToken cancellationToken) =>
        Task.FromResult(RendezVous.SingleOrDefault(r => r.RendezVousId == rendezVousId));

    public Task<IReadOnlyList<RendezVousLocal>> RendezVousDeAsync(Guid personneId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RendezVousLocal>>(RendezVous.Where(r => r.PersonneId == personneId).ToList());

    public void Add(RendezVousLocal rendezVous) => RendezVous.Add(rendezVous);

    public Task<RepriseLocale?> GetRepriseAsync(Guid personneId, Guid affilieId, DateOnly dateReprise, CancellationToken cancellationToken) =>
        Task.FromResult(Reprises.SingleOrDefault(r => r.PersonneId == personneId && r.AffilieId == affilieId && r.DateReprise == dateReprise));

    public Task<IReadOnlyList<RepriseLocale>> ReprisesDeAsync(Guid personneId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RepriseLocale>>(Reprises.Where(r => r.PersonneId == personneId).ToList());

    public void Add(RepriseLocale reprise) => Reprises.Add(reprise);

    public Task<IncapaciteLocale?> GetIncapaciteAsync(Guid incapaciteId, CancellationToken cancellationToken) =>
        Task.FromResult(Incapacites.SingleOrDefault(i => i.IncapaciteId == incapaciteId));

    public Task<IReadOnlyList<IncapaciteLocale>> IncapacitesDeAsync(Guid personneId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<IncapaciteLocale>>(Incapacites.Where(i => i.PersonneId == personneId).ToList());

    public void Add(IncapaciteLocale incapacite) => Incapacites.Add(incapacite);

    public Task<TrajetLocal?> GetTrajetAsync(Guid trajetId, CancellationToken cancellationToken) =>
        Task.FromResult(Trajets.SingleOrDefault(t => t.TrajetId == trajetId));

    public Task<IReadOnlyList<TrajetLocal>> TrajetsDeAsync(Guid personneId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TrajetLocal>>(Trajets.Where(t => t.PersonneId == personneId).ToList());

    public void Add(TrajetLocal trajet) => Trajets.Add(trajet);

    public Task<IReadOnlyList<Guid>> PersonnesAvecEvenementsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Reprises.Select(r => r.PersonneId).Concat(Incapacites.Select(i => i.PersonneId)).Concat(Trajets.Select(t => t.PersonneId)).Distinct().ToList());

    public Task<ListeNominativeLocale?> GetListeNominativeAsync(Guid listeNominativeId, CancellationToken cancellationToken) =>
        Task.FromResult(Listes.SingleOrDefault(l => l.ListeNominativeId == listeNominativeId));

    public Task<IReadOnlyList<ListeNominativeLocale>> ListesNominativesAsync(Guid affilieId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ListeNominativeLocale>>(Listes.Where(l => l.AffilieId == affilieId).ToList());

    public void Add(ListeNominativeLocale liste) => Listes.Add(liste);

    // Processus de reprise
    public List<ProcessusReprise> Processus { get; } = [];

    public List<DecisionRecue> Decisions { get; } = [];

    Task<ProcessusReprise?> IProcessusRepriseRepository.GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Processus.SingleOrDefault(p => p.Id == id));

    public Task<ProcessusReprise?> GetActifAsync(Guid personneId, Guid affilieId, DateOnly dateReprise, CancellationToken cancellationToken) =>
        Task.FromResult(Processus.SingleOrDefault(p => p.PersonneId == personneId && p.AffilieId == affilieId && p.DateReprise == dateReprise && !p.EstAnnulee));

    public Task<ProcessusReprise?> GetParObligationAsync(Guid obligationId, CancellationToken cancellationToken) =>
        Task.FromResult(Processus.FirstOrDefault(p => p.ObligationId == obligationId && !p.EstAnnulee));

    public Task<ProcessusReprise?> GetParExamenAsync(Guid examenId, CancellationToken cancellationToken) =>
        Task.FromResult(Processus.FirstOrDefault(p => p.ExamenId == examenId));

    public Task<ProcessusReprise?> GetParDecisionAsync(Guid decisionId, CancellationToken cancellationToken) =>
        Task.FromResult(Processus.FirstOrDefault(p => p.DecisionId == decisionId));

    Task<IReadOnlyList<ProcessusReprise>> IProcessusRepriseRepository.ListParPersonneAsync(Guid personneId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProcessusReprise>>(Processus.Where(p => p.PersonneId == personneId).ToList());

    public Task<IReadOnlyList<ProcessusReprise>> ListAsync(
        Guid? affilieId, StatutReprise? statut, DateOnly? echeanceAvant, int nombreMaximum, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProcessusReprise>>(Processus
            .Where(p => (affilieId is null || p.AffilieId == affilieId) && (statut is null || p.Statut == statut)
                        && (echeanceAvant is null || (p.DateLimite is { } l && l <= echeanceAvant)))
            .Take(nombreMaximum)
            .ToList());

    public Task<IReadOnlyList<ProcessusReprise>> ListActifsParAffilieAsync(Guid affilieId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProcessusReprise>>(Processus.Where(p => p.AffilieId == affilieId && p.EstActif).ToList());

    public Task<IReadOnlyList<ProcessusReprise>> ReserverEchusAsync(DateOnly aujourdHui, int nombreMaximum, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProcessusReprise>>(Processus
            .Where(p => !p.EstAnnulee && p.ProchaineEcheance is { } e && e <= aujourdHui)
            .OrderBy(p => p.ProchaineEcheance)
            .Take(nombreMaximum)
            .ToList());

    public Task<IReadOnlyList<Guid>> PersonnesNonSynchroniseesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Processus.Where(p => p.Statut == StatutReprise.Annoncee && !p.EstAnnulee).Select(p => p.PersonneId).Distinct().ToList());

    public void Add(ProcessusReprise processus) => Processus.Add(processus);

    public void AbandonnerChangements()
    {
    }

    Task<DecisionRecue?> IDecisionRecueRepository.GetAsync(Guid examenId, CancellationToken cancellationToken) =>
        Task.FromResult(Decisions.SingleOrDefault(d => d.ExamenId == examenId));

    public void Add(DecisionRecue decision) => Decisions.Add(decision);
}

internal sealed class FakeUser(string userId, params string[] roles) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public string UserId => userId;

    public IReadOnlySet<string> Roles { get; } = roles.ToHashSet();

    public bool HasPermission(string permission) => RolePermissions.For(Roles).Contains(permission);
}

/// <summary>Périmètre de test : interne (tous les affiliés) ou externe limité à une liste d'affiliés.</summary>
internal sealed class FakePerimetre(bool externe, params Guid[] affilies) : IPerimetreAffilies
{
    public static FakePerimetre Interne { get; } = new(false);

    public bool EstExterne => externe;

    public bool PeutAcceder(Guid affilieId) => !externe || affilies.Contains(affilieId);
}

/// <summary>Assemble les cas d'usage sur un magasin en mémoire et une horloge fixe.</summary>
internal sealed class Banc
{
    public static readonly DateTimeOffset Midi = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    public Banc(DateTimeOffset? maintenant = null)
    {
        Clock = new FakeTimeProvider(maintenant ?? Midi);
        Synchronisation = new SynchronisationProcessusReprise(Store, Store, Store, Store, OptionsReprise, Clock);
        Recalcul = new RecalculObligations(Store, Store, Store, Store, Clock, new OptionsCalcul(), Synchronisation);
        MiseAJour = new Projections.MiseAJourProjection(Recalcul, Store, Store, Store);
        Enregistrement = new EnregistrementReprise(Store, Store, MiseAJour, Store, Store, OptionsReprise, Clock);
    }

    public InMemoryStore Store { get; } = new();

    /// <summary>Occupation en cours chez l'affilié depuis 2025 (préalable de toute annonce de reprise, #298) ; idempotent.</summary>
    public OccupationLocale OccupationActive()
    {
        var occupation = Store.Occupations.SingleOrDefault(o => o.PersonneId == Personne && o.AffilieId == Affilie);
        if (occupation is null)
        {
            occupation = new OccupationLocale(Guid.CreateVersion7(), Personne, Affilie);
            occupation.Debuter(new DateOnly(2025, 1, 1));
            Store.Add(occupation);
        }

        return occupation;
    }

    public FakeTimeProvider Clock { get; }

    public OptionsReprise OptionsReprise { get; } = new();

    public SynchronisationProcessusReprise Synchronisation { get; }

    public EnregistrementReprise Enregistrement { get; }

    public RecalculObligations Recalcul { get; }

    public Projections.MiseAJourProjection MiseAJour { get; }

    public OptionsObligations Options { get; } = new();

    public Guid Affilie { get; } = Guid.CreateVersion7();

    public Guid Personne { get; } = Guid.CreateVersion7();

    public Guid Poste { get; } = Guid.CreateVersion7();

    public static FakeUser Cpmt { get; } = new("cpmt-1", Roles.Cpmt);

    public static FakeUser Employeur { get; } = new("employeur-1", Roles.Employeur);

    public static FakeUser Travailleur { get; } = new("travailleur-1", Roles.Travailleur);
}
