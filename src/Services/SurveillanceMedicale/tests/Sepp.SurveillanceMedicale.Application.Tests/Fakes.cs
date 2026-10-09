using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts;
using Sepp.SurveillanceMedicale.Domain.Conservation;
using Sepp.SurveillanceMedicale.Domain.Decisions;
using Sepp.SurveillanceMedicale.Domain.Dossiers;
using Sepp.SurveillanceMedicale.Domain.Examens;
using Sepp.SurveillanceMedicale.Domain.MaladiesProfessionnelles;
using Sepp.SurveillanceMedicale.Domain.Projections;
using Sepp.SurveillanceMedicale.Domain.Protocoles;
using Sepp.SurveillanceMedicale.Domain.Transferts;
using Sepp.SurveillanceMedicale.Domain.Vaccinations;

namespace Sepp.SurveillanceMedicale.Application.Tests;

/// <summary>Adaptateurs en mémoire : la couche application est testée sans infrastructure (ARC-23).</summary>
internal sealed class InMemoryStore : IUnitOfWork, IIntegrationEventOutbox, IDossierSanteRepository, IExamenRepository, IDecisionRepository,
    IProtocolesRepository, ILotVaccinRepository, IDeclarationMpRepository, ITransfertRepository, IPurgeDossiers, IProjectionRepository
{
    private readonly List<IntegrationEvent> _pending = [];

    public List<DossierSante> Dossiers { get; } = [];

    public List<Examen> Examens { get; } = [];

    public List<Decision> Decisions { get; } = [];

    public List<ValeurReference> Valeurs { get; } = [];

    public List<SchemaVaccinal> Schemas { get; } = [];

    public List<ModeleQuestionnaire> Questionnaires { get; } = [];

    public List<ModeleTexte> Textes { get; } = [];

    public List<DureeConservationExposition> Durees { get; } = [];

    public List<LotVaccin> Lots { get; } = [];

    public List<DeclarationMaladieProfessionnelle> DeclarationsMp { get; } = [];

    public List<TransfertDossier> Transferts { get; } = [];

    public List<PreuveDestruction> Preuves { get; } = [];

    public List<ObligationDue> Obligations { get; } = [];

    public List<RendezVousPrevu> RendezVous { get; } = [];

    public List<AffectationPersonne> Affectations { get; } = [];

    public List<ProfilRisquePoste> Profils { get; } = [];

    public List<MesurageExposition> Mesurages { get; } = [];

    public List<ParametreLegalLocal> Parametres { get; } = [];

    public List<CalendrierLocal> Calendriers { get; } = [];

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

    // Dossiers
    Task<DossierSante?> IDossierSanteRepository.GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Dossiers.SingleOrDefault(d => d.Id == id));

    public Task<DossierSante?> GetParPersonneAsync(Guid personneId, CancellationToken cancellationToken) => Task.FromResult(Dossiers.SingleOrDefault(d => d.PersonneId == personneId));

    public Task<IReadOnlyList<DossierSante>> ListerRattachesAuGroupeAsync(Guid groupeExpositionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DossierSante>>([.. Dossiers.Where(d => d.GroupesExposition.Any(g => g.GroupeExpositionId == groupeExpositionId))]);

    public Task<IReadOnlyList<DossierSante>> ListerParStatutAsync(StatutArchivage statut, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DossierSante>>([.. Dossiers.Where(d => d.StatutArchivage == statut)]);

    public void Add(DossierSante dossier) => Dossiers.Add(dossier);

    // Examens
    Task<Examen?> IExamenRepository.GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Examens.SingleOrDefault(e => e.Id == id));

    Task<IReadOnlyList<Examen>> IExamenRepository.ListerParDossierAsync(Guid dossierId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Examen>>([.. Examens.Where(e => e.DossierId == dossierId)]);

    public Task<bool> ExisteExamenDuProfessionnelAsync(Guid dossierId, string professionnelId, CancellationToken cancellationToken) =>
        Task.FromResult(Examens.Exists(e => e.DossierId == dossierId && e.ProfessionnelId == professionnelId));

    public void Add(Examen examen) => Examens.Add(examen);

    // Décisions
    Task<Decision?> IDecisionRepository.GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Decisions.SingleOrDefault(d => d.Id == id));

    public Task<Decision?> GetParExamenAsync(Guid examenId, CancellationToken cancellationToken) => Task.FromResult(Decisions.SingleOrDefault(d => d.ExamenId == examenId));

    Task<IReadOnlyList<Decision>> IDecisionRepository.ListerParDossierAsync(Guid dossierId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Decision>>([.. Decisions.Where(d => d.DossierId == dossierId)]);

    public void Add(Decision decision) => Decisions.Add(decision);

    // Protocoles
    public Task<IReadOnlyList<ValeurReference>> ListerValeursReferenceAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ValeurReference>>([.. Valeurs]);

    public Task<ValeurReference?> GetValeurReferenceAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Valeurs.SingleOrDefault(v => v.Id == id));

    public void Add(ValeurReference valeur) => Valeurs.Add(valeur);

    public Task<IReadOnlyList<SchemaVaccinal>> ListerSchemasVaccinauxAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SchemaVaccinal>>([.. Schemas]);

    public void Add(SchemaVaccinal schema) => Schemas.Add(schema);

    public Task<ModeleQuestionnaire?> GetModeleQuestionnaireAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(Questionnaires.Where(q => q.Code == code).MaxBy(q => q.Version));

    public Task<IReadOnlyList<ModeleQuestionnaire>> ListerModelesQuestionnaireAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ModeleQuestionnaire>>([.. Questionnaires]);

    public void Add(ModeleQuestionnaire modele) => Questionnaires.Add(modele);

    public Task<IReadOnlyList<ModeleTexte>> ListerModelesTexteAsync(string cpmtId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ModeleTexte>>([.. Textes.Where(t => t.CpmtId == cpmtId)]);

    public Task<ModeleTexte?> GetModeleTexteAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Textes.SingleOrDefault(t => t.Id == id));

    public void Add(ModeleTexte modele) => Textes.Add(modele);

    public void Remove(ModeleTexte modele) => Textes.Remove(modele);

    public Task<IReadOnlyList<DureeConservationExposition>> ListerDureesConservationAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DureeConservationExposition>>([.. Durees]);

    public void Add(DureeConservationExposition duree) => Durees.Add(duree);

    // Lots
    Task<LotVaccin?> ILotVaccinRepository.GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Lots.SingleOrDefault(l => l.Id == id));

    public Task<IReadOnlyList<LotVaccin>> ListerParCentreAsync(Guid centreId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LotVaccin>>([.. Lots.Where(l => l.CentreId == centreId)]);

    public void Add(LotVaccin lot) => Lots.Add(lot);

    // Déclarations MP
    Task<DeclarationMaladieProfessionnelle?> IDeclarationMpRepository.GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(DeclarationsMp.SingleOrDefault(d => d.Id == id));

    Task<IReadOnlyList<DeclarationMaladieProfessionnelle>> IDeclarationMpRepository.ListerParDossierAsync(Guid dossierId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DeclarationMaladieProfessionnelle>>([.. DeclarationsMp.Where(d => d.DossierId == dossierId)]);

    public void Add(DeclarationMaladieProfessionnelle declaration) => DeclarationsMp.Add(declaration);

    // Transferts
    Task<TransfertDossier?> ITransfertRepository.GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Transferts.SingleOrDefault(t => t.Id == id));

    Task<IReadOnlyList<TransfertDossier>> ITransfertRepository.ListerParDossierAsync(Guid dossierId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TransfertDossier>>([.. Transferts.Where(t => t.DossierId == dossierId)]);

    public void Add(TransfertDossier transfert) => Transferts.Add(transfert);

    // Purge
    public async Task DetruireAsync(DossierSante dossier, PreuveDestruction preuve, CancellationToken cancellationToken)
    {
        Preuves.Add(preuve);
        Dossiers.Remove(dossier);
        Examens.RemoveAll(e => e.DossierId == dossier.Id);
        Decisions.RemoveAll(d => d.DossierId == dossier.Id);
        DeclarationsMp.RemoveAll(d => d.DossierId == dossier.Id);
        Transferts.RemoveAll(t => t.DossierId == dossier.Id);
        await SaveChangesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<PreuveDestruction>> ListerPreuvesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PreuveDestruction>>([.. Preuves]);

    // Projections
    public Task<ObligationDue?> GetObligationAsync(Guid obligationId, CancellationToken cancellationToken) => Task.FromResult(Obligations.SingleOrDefault(o => o.ObligationId == obligationId));

    public Task<IReadOnlyList<ObligationDue>> ListerObligationsAsync(Guid personneId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ObligationDue>>([.. Obligations.Where(o => o.PersonneId == personneId)]);

    public Task<IReadOnlyList<ObligationDue>> ListerObligationsParIdsAsync(IReadOnlyCollection<Guid> obligationIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ObligationDue>>([.. Obligations.Where(o => obligationIds.Contains(o.ObligationId))]);

    public void Add(ObligationDue obligation) => Obligations.Add(obligation);

    public Task<RendezVousPrevu?> GetRendezVousAsync(Guid rendezVousId, CancellationToken cancellationToken) => Task.FromResult(RendezVous.SingleOrDefault(r => r.RendezVousId == rendezVousId));

    public void Add(RendezVousPrevu rendezVous) => RendezVous.Add(rendezVous);

    public Task<AffectationPersonne?> GetAffectationAsync(Guid affectationId, CancellationToken cancellationToken) => Task.FromResult(Affectations.SingleOrDefault(a => a.AffectationId == affectationId));

    public Task<IReadOnlyList<AffectationPersonne>> ListerAffectationsAsync(Guid personneId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AffectationPersonne>>([.. Affectations.Where(a => a.PersonneId == personneId)]);

    public void Add(AffectationPersonne affectation) => Affectations.Add(affectation);

    public Task<ProfilRisquePoste?> GetProfilAsync(Guid posteId, DateOnly valideDu, CancellationToken cancellationToken) =>
        Task.FromResult(Profils.SingleOrDefault(p => p.PosteId == posteId && p.ValideDu == valideDu));

    public Task<IReadOnlyList<ProfilRisquePoste>> ListerProfilsAsync(IReadOnlyCollection<Guid> posteIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProfilRisquePoste>>([.. Profils.Where(p => posteIds.Contains(p.PosteId))]);

    public void Add(ProfilRisquePoste profil) => Profils.Add(profil);

    public Task<MesurageExposition?> GetMesurageAsync(Guid mesurageId, CancellationToken cancellationToken) => Task.FromResult(Mesurages.SingleOrDefault(m => m.MesurageId == mesurageId));

    public Task<IReadOnlyList<MesurageExposition>> ListerMesuragesAsync(Guid groupeExpositionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MesurageExposition>>([.. Mesurages.Where(m => m.GroupeExpositionId == groupeExpositionId)]);

    public void Add(MesurageExposition mesurage) => Mesurages.Add(mesurage);

    public Task<ParametreLegalLocal?> GetParametreAsync(string code, DateOnly valideDu, CancellationToken cancellationToken) =>
        Task.FromResult(Parametres.SingleOrDefault(p => p.Code == code && p.ValideDu == valideDu));

    public Task<ParametreLegalLocal?> ParametreApplicableAsync(string code, DateOnly date, CancellationToken cancellationToken) =>
        Task.FromResult(Parametres.Where(p => p.Code == code && p.EstApplicableAu(date)).MaxBy(p => p.ValideDu));

    public void Add(ParametreLegalLocal parametre) => Parametres.Add(parametre);

    public Task<CalendrierLocal?> GetCalendrierAsync(int annee, CancellationToken cancellationToken) => Task.FromResult(Calendriers.SingleOrDefault(c => c.Annee == annee));

    public Task<IReadOnlyList<CalendrierLocal>> ListerCalendriersAsync(int anneeDebut, int anneeFin, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CalendrierLocal>>([.. Calendriers.Where(c => c.Annee >= anneeDebut && c.Annee <= anneeFin)]);

    public void Add(CalendrierLocal calendrier) => Calendriers.Add(calendrier);
}

internal sealed record Trace(ActionAudit Action, string ObjetType, Guid ObjetId, string? Motif, bool BrisDeGlace, bool Immediate);

/// <summary>Journal d'audit en mémoire : vérifie qu'un accès est tracé (et une lecture tracée avant d'être servie).</summary>
internal sealed class FakeAudit(InMemoryStore store) : IAuditTrail
{
    public List<Trace> Traces { get; } = [];

    public void Enregistrer(ActionAudit action, string objetType, Guid objetId, string? motif = null, bool brisDeGlace = false)
    {
        if (MotifAcces.Verifier(motif, brisDeGlace) is { } erreur)
        {
            throw new ArgumentException(erreur.Message, nameof(motif));
        }

        Traces.Add(new Trace(action, objetType, objetId, motif, brisDeGlace, false));
    }

    public async Task EnregistrerLectureAsync(string objetType, Guid objetId, string? motif, bool brisDeGlace, CancellationToken cancellationToken)
    {
        Traces.Add(new Trace(ActionAudit.Lecture, objetType, objetId, motif, brisDeGlace, true));
        await store.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class FakeUser(string userId, params string[] roles) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public string UserId { get; } = userId;

    public IReadOnlySet<string> Roles { get; } = roles.ToHashSet();

    public bool HasPermission(string permission) => RolePermissions.For(Roles).Contains(permission);
}

internal sealed class FakeContexte : IContexteAcces
{
    public string? Motif { get; set; }

    public bool BrisDeGlace { get; set; }

    public Guid? AffilieIdJeton { get; set; }

    public Guid? PersonneIdJeton { get; set; }
}

internal sealed class FakeHorloge(DateTimeOffset maintenant) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => maintenant;
}

internal sealed class FakeSignature : ISignatureQualifiee
{
    public List<DemandeSignature> Demandes { get; } = [];

    public Task<SignatureObtenue> SignerAsync(DemandeSignature demande, CancellationToken cancellationToken)
    {
        Demandes.Add(demande);
        return Task.FromResult(new SignatureObtenue("SIG-TEST", new DateTimeOffset(2026, 3, 3, 10, 0, 0, TimeSpan.Zero)));
    }
}
