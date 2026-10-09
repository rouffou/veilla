using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts;
using Sepp.Planification.Domain;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Convocations;
using Sepp.Planification.Domain.Projections;
using Sepp.Planification.Domain.Ressources;
using Sepp.Planification.Domain.Sessions;

namespace Sepp.Planification.Application.Tests;

/// <summary>Adaptateurs en mémoire : la couche application est testée sans infrastructure (ARC-23).</summary>
internal sealed class InMemoryStore : IUnitOfWork, IIntegrationEventOutbox, ILieuRepository, IRessourceRepository, IAbsenceRepository, IModeleAgendaRepository,
    IDureeStandardRepository, ICreneauRepository, IRendezVousRepository, IConvocationRepository, IPreferenceConvocationRepository, ISessionRepository,
    IObligationRepository, IParametreLocalRepository, ICalendrierLocalRepository
{
    private readonly List<IntegrationEvent> _pending = [];

    public List<Lieu> Lieux { get; } = [];

    public List<Ressource> Ressources { get; } = [];

    public List<Absence> Absences { get; } = [];

    public List<ModeleAgenda> Modeles { get; } = [];

    public List<DureeStandard> Durees { get; } = [];

    public List<Creneau> Creneaux { get; } = [];

    public List<RendezVous> RendezVous { get; } = [];

    public List<Convocation> Convocations { get; } = [];

    public List<PreferenceConvocation> Preferences { get; } = [];

    public List<Session> Sessions { get; } = [];

    public List<ObligationAPlanifier> Obligations { get; } = [];

    public List<ParametreLegalLocal> Parametres { get; } = [];

    public List<CalendrierLocal> Calendriers { get; } = [];

    public List<IntegrationEvent> Published { get; } = [];

    public int Saves { get; private set; }

    public IEnumerable<T> Evenements<T>()
        where T : IntegrationEvent => Published.OfType<T>();

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        Published.AddRange(_pending);
        _pending.Clear();
        return Task.CompletedTask;
    }

    void IIntegrationEventOutbox.Add(IntegrationEvent integrationEvent) => _pending.Add(integrationEvent);

    // Lieux
    Task<Lieu?> ILieuRepository.GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Lieux.SingleOrDefault(l => l.Id == id));

    Task<IReadOnlyList<Lieu>> ILieuRepository.ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Lieu>>(Lieux.ToList());

    void ILieuRepository.Add(Lieu lieu) => Lieux.Add(lieu);

    // Ressources
    Task<Ressource?> IRessourceRepository.GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Ressources.SingleOrDefault(r => r.Id == id));

    Task<IReadOnlyList<Ressource>> IRessourceRepository.ListAsync(TypeRessource? type, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Ressource>>(Ressources.Where(r => type is null || r.Type == type).ToList());

    void IRessourceRepository.Add(Ressource ressource) => Ressources.Add(ressource);

    // Absences
    Task<Absence?> IAbsenceRepository.GetParReferenceAsync(SourceAbsence source, string referenceExterne, CancellationToken cancellationToken) =>
        Task.FromResult(Absences.SingleOrDefault(a => a.Source == source && a.ReferenceExterne == referenceExterne));

    Task<IReadOnlyList<Absence>> IAbsenceRepository.ListAsync(Guid? ressourceId, DateTimeOffset du, DateTimeOffset au, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Absence>>(Absences.Where(a => (ressourceId is null || a.RessourceId == ressourceId) && a.Debut < au && a.Fin > du).ToList());

    void IAbsenceRepository.Add(Absence absence) => Absences.Add(absence);

    // Modèles et durées
    Task<ModeleAgenda?> IModeleAgendaRepository.GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Modeles.SingleOrDefault(m => m.Id == id));

    Task<IReadOnlyList<ModeleAgenda>> IModeleAgendaRepository.ListAsync(Guid? ressourceId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ModeleAgenda>>(Modeles.Where(m => ressourceId is null || m.RessourceId == ressourceId).ToList());

    void IModeleAgendaRepository.Add(ModeleAgenda modele) => Modeles.Add(modele);

    Task<IReadOnlyList<DureeStandard>> IDureeStandardRepository.ListAsync(string? typeActe, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DureeStandard>>(Durees.Where(d => typeActe is null || d.TypeActe == typeActe).ToList());

    void IDureeStandardRepository.Add(DureeStandard duree) => Durees.Add(duree);

    // Créneaux
    Task<Creneau?> ICreneauRepository.GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Creneaux.SingleOrDefault(c => c.Id == id));

    Task<IReadOnlyList<Creneau>> ICreneauRepository.RechercherAsync(CritereCreneaux critere, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Creneau>>(Creneaux
            .Where(c => c.Debut >= critere.Du && c.Debut < critere.Au)
            .Where(c => critere.Ressource is null || c.Mobilise(critere.Ressource.Value))
            .Where(c => critere.LieuId is null || c.LieuId == critere.LieuId)
            .Where(c => critere.SessionId is null || c.SessionId == critere.SessionId)
            .Where(c => critere.TypeActe is null || c.TypeActe == critere.TypeActe)
            .Where(c => critere.Statut is null || c.Statut == critere.Statut)
            .Where(c => critere.ReserveUrgence is null || c.ReserveUrgence == critere.ReserveUrgence)
            .Where(c => critere.OuvertEnLigne is null || c.OuvertEnLigne == critere.OuvertEnLigne)
            .OrderBy(c => c.Debut).ThenBy(c => c.Id)
            .ToList());

    Task<IReadOnlyList<Creneau>> ICreneauRepository.ChevauchantsAsync(IReadOnlyCollection<Guid> ressources, DateTimeOffset du, DateTimeOffset au,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Creneau>>(Creneaux.Where(c => c.Occupations.Any(o => ressources.Contains(o.RessourceId) && o.Debut < au && o.Fin > du)).ToList());

    void ICreneauRepository.Add(Creneau creneau) => Creneaux.Add(creneau);

    void ICreneauRepository.Remove(Creneau creneau) => Creneaux.Remove(creneau);

    // Rendez-vous et convocations
    Task<RendezVous?> IRendezVousRepository.GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(RendezVous.SingleOrDefault(r => r.Id == id));

    Task<IReadOnlyList<RendezVous>> IRendezVousRepository.RechercherAsync(CritereRendezVous critere, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RendezVous>>(RendezVous
            .Where(r => critere.PersonneId is null || r.PersonneId == critere.PersonneId)
            .Where(r => critere.AffilieId is null || r.AffilieId == critere.AffilieId)
            .Where(r => critere.RessourceId is null || r.RessourceId == critere.RessourceId)
            .Where(r => critere.LieuId is null || r.LieuId == critere.LieuId)
            .Where(r => critere.Du is null || r.Debut >= critere.Du)
            .Where(r => critere.Au is null || r.Debut < critere.Au)
            .Where(r => critere.Statuts is null || critere.Statuts.Contains(r.Statut))
            .Where(r => critere.CreneauIds is null || critere.CreneauIds.Contains(r.CreneauId))
            .OrderBy(r => r.Debut).ThenBy(r => r.Id)
            .ToList());

    void IRendezVousRepository.Add(RendezVous rendezVous) => RendezVous.Add(rendezVous);

    Task<Convocation?> IConvocationRepository.GetAsync(Guid convocationId, CancellationToken cancellationToken) =>
        Task.FromResult(Convocations.SingleOrDefault(c => c.Id == convocationId));

    Task<CalendrierLocal?> ICalendrierLocalRepository.GetAsync(int annee, CancellationToken cancellationToken) =>
        Task.FromResult(Calendriers.SingleOrDefault(c => c.Annee == annee));

    Task<IReadOnlyList<CalendrierLocal>> ICalendrierLocalRepository.ListAsync(int anneeDebut, int anneeFin, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CalendrierLocal>>(Calendriers.Where(c => c.Annee >= anneeDebut && c.Annee <= anneeFin).ToList());

    void ICalendrierLocalRepository.Add(CalendrierLocal calendrier) => Calendriers.Add(calendrier);

    Task<IReadOnlyList<Convocation>> IConvocationRepository.ListAsync(Guid rendezVousId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Convocation>>(Convocations.Where(c => c.RendezVousId == rendezVousId).ToList());

    void IConvocationRepository.Add(Convocation convocation) => Convocations.Add(convocation);

    Task<PreferenceConvocation?> IPreferenceConvocationRepository.GetAsync(Guid affilieId, CancellationToken cancellationToken) =>
        Task.FromResult(Preferences.SingleOrDefault(p => p.AffilieId == affilieId));

    void IPreferenceConvocationRepository.Add(PreferenceConvocation preference) => Preferences.Add(preference);

    // Sessions
    Task<Session?> ISessionRepository.GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Sessions.SingleOrDefault(s => s.Id == id));

    Task<IReadOnlyList<Session>> ISessionRepository.ListAsync(DateOnly du, DateOnly au, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Session>>(Sessions.Where(s => s.Date >= du && s.Date <= au).ToList());

    void ISessionRepository.Add(Session session) => Sessions.Add(session);

    // Projections
    Task<ObligationAPlanifier?> IObligationRepository.GetAsync(Guid obligationId, CancellationToken cancellationToken) =>
        Task.FromResult(Obligations.SingleOrDefault(o => o.ObligationId == obligationId));

    Task<IReadOnlyList<ObligationAPlanifier>> IObligationRepository.ListAsync(IReadOnlyCollection<Guid> obligationIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ObligationAPlanifier>>(Obligations.Where(o => obligationIds.Contains(o.ObligationId)).ToList());

    Task<IReadOnlyList<ObligationAPlanifier>> IObligationRepository.ListerAPlanifierAsync(IReadOnlyCollection<Guid>? affilieIds, DateOnly horizon,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ObligationAPlanifier>>(Obligations
            .Where(o => o.RendezVousId is null && !o.Cloturee && o.DateDue <= horizon && (affilieIds is null || affilieIds.Contains(o.AffilieId)))
            .OrderBy(o => o.DateDue).ThenBy(o => o.ObligationId)
            .ToList());

    Task<IReadOnlyList<ObligationAPlanifier>> IObligationRepository.ListerParPersonneAsync(Guid personneId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ObligationAPlanifier>>(Obligations.Where(o => o.PersonneId == personneId).ToList());

    void IObligationRepository.Add(ObligationAPlanifier obligation) => Obligations.Add(obligation);

    Task<ParametreLegalLocal?> IParametreLocalRepository.GetAsync(string code, DateOnly valideDu, CancellationToken cancellationToken) =>
        Task.FromResult(Parametres.SingleOrDefault(p => p.Code == code && p.ValideDu == valideDu));

    Task<ParametreLegalLocal?> IParametreLocalRepository.ApplicableAsync(string code, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult(Parametres.Where(p => p.Code == code && p.EstApplicableAu(date)).OrderByDescending(p => p.ValideDu).FirstOrDefault());

    void IParametreLocalRepository.Add(ParametreLegalLocal parametre) => Parametres.Add(parametre);
}

internal sealed class FakeUser(params string[] roles) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public string UserId { get; init; } = "test-user";

    public IReadOnlySet<string> Roles { get; } = roles.ToHashSet();

    public bool HasPermission(string permission) => RolePermissions.For(Roles).Contains(permission);
}

/// <summary>Périmètre issu du jeton : un employeur (affilie_id) ou un travailleur (personne_id).</summary>
internal sealed class FakePerimetre(bool externe, Guid? personneId = null, params Guid[] affilies) : IPerimetreUtilisateur
{
    public static FakePerimetre Interne { get; } = new(false);

    public static FakePerimetre Employeur(params Guid[] affilies) => new(true, null, affilies);

    public static FakePerimetre Travailleur(Guid personneId) => new(true, personneId);

    public bool EstExterne => externe;

    public Guid? PersonneId => personneId;

    public bool PeutAccederAffilie(Guid affilieId) => !externe || affilies.Contains(affilieId);
}

internal sealed class FakeClock(DateTimeOffset maintenant) : TimeProvider
{
    private DateTimeOffset _maintenant = maintenant;

    public override DateTimeOffset GetUtcNow() => _maintenant;

    public void Avancer(TimeSpan duree) => _maintenant += duree;

    public void Fixer(DateTimeOffset maintenant) => _maintenant = maintenant;
}

internal sealed class FakeOutilRh(params CongeRh[] conges) : IOutilRh
{
    public Task<IReadOnlyList<CongeRh>> LireCongesAsync(DateOnly du, DateOnly au, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CongeRh>>(conges.ToList());
}

internal sealed class FakeAgendaExterne : IAgendaExterne
{
    public Dictionary<string, EvenementAgenda> Evenements { get; } = [];

    public List<OccupationExterne> Occupations { get; } = [];

    public HashSet<string> ComptesInjoignables { get; } = [];

    public Task<string> EcrireAsync(FournisseurAgenda fournisseur, string compte, string? reference, EvenementAgenda evenement, CancellationToken cancellationToken)
    {
        Verifier(compte);
        var cle = reference ?? $"ext-{Evenements.Count + 1}";
        Evenements[cle] = evenement;
        return Task.FromResult(cle);
    }

    public Task SupprimerAsync(FournisseurAgenda fournisseur, string compte, string reference, CancellationToken cancellationToken)
    {
        Verifier(compte);
        Evenements.Remove(reference);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OccupationExterne>> LireOccupationsAsync(FournisseurAgenda fournisseur, string compte, DateTimeOffset du, DateTimeOffset au,
        CancellationToken cancellationToken)
    {
        Verifier(compte);
        var ecrites = Evenements.Select(e => new OccupationExterne(e.Key, e.Value.Debut, e.Value.Fin));
        return Task.FromResult<IReadOnlyList<OccupationExterne>>(Occupations.Concat(ecrites).Where(o => o.Debut < au && o.Fin > du).ToList());
    }

    private void Verifier(string compte)
    {
        if (ComptesInjoignables.Contains(compte))
        {
            throw new AgendaExterneIndisponibleException("Agenda injoignable.");
        }
    }
}
