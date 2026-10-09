using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Obligations;
using Sepp.Planification.Application.Convocations;
using Sepp.Planification.Application.PriseRendezVous;
using Sepp.Planification.Application.Projections;
using Sepp.Planification.Application.Ressources;
using Sepp.Planification.Application.Urgences;
using Sepp.Planification.Domain;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Projections;
using Sepp.Planification.Domain.Ressources;

namespace Sepp.Planification.Application.Tests;

/// <summary>Assemble le service sans infrastructure : dépôts en mémoire, horloge maîtrisée, utilisateur de test.</summary>
internal sealed class Harness
{
    /// <summary>Lundi 1er mars 2027, 8 h (heure belge) : les créneaux de test tombent à partir du 15 mars.</summary>
    public static readonly DateOnly Aujourdhui = new(2027, 3, 1);

    public static readonly DateOnly Lundi = new(2027, 3, 15);

    public Harness(DateOnly? aujourdhui = null)
    {
        Horloge = new FakeClock(HeureBelge.VersUtc(aujourdhui ?? Aujourdhui, new TimeOnly(8, 0)));
        Parametres = new ParametresPlanification(Store, Store, Options);
        Annulation = new Annulation(Store, Store, Store, Horloge);
        Prise = new PriseDeRendezVous(Store, Store, Store, Store, Store, Store, Store, Parametres, Horloge);
        Urgence = new ReservationUrgence(Prise, Parametres, Store, Store, Horloge);
        Indisponibilites = new Indisponibilites(Store);
        Conseiller = AjouterRessource(TypeRessource.Conseiller, "Dr A.", "kc-a", "EVALUATION_PERIODIQUE", "EXAMEN_REPRISE");
        Lieu = AjouterLieu("Centre de Namur");
    }

    public InMemoryStore Store { get; } = new();

    public OptionsPlanification Options { get; } = new();

    public FakeClock Horloge { get; }

    public ParametresPlanification Parametres { get; }

    public Annulation Annulation { get; }

    public PriseDeRendezVous Prise { get; }

    public ReservationUrgence Urgence { get; }

    public Indisponibilites Indisponibilites { get; }

    public Ressource Conseiller { get; }

    public Lieu Lieu { get; }

    public Guid Personne { get; } = Guid.CreateVersion7();

    public Guid Affilie { get; } = Guid.CreateVersion7();

    public static ICurrentUser Planificateur { get; } = new FakeUser(Roles.Planificateur);

    public static ICurrentUser ResponsableCentre { get; } = new FakeUser(Roles.ResponsableCentre);

    public static ICurrentUser AssistantMedical { get; } = new FakeUser(Roles.AssistantMedical);

    public static ICurrentUser EmployeurUser { get; } = new FakeUser(Roles.Employeur);

    public static ICurrentUser TravailleurUser { get; } = new FakeUser(Roles.Travailleur);

    public static DateTimeOffset Instant(DateOnly jour, int heure, int minute = 0) => HeureBelge.VersUtc(jour, new TimeOnly(heure, minute));

    public Ressource AjouterRessource(TypeRessource type, string libelle, string? reference = null, params string[] competences)
    {
        var ressource = Ressource.Creer(type, libelle, reference, competences, null);
        Store.Ressources.Add(ressource);
        return ressource;
    }

    public Lieu AjouterLieu(string nom, Guid? affilieId = null)
    {
        var lieu = affilieId is null
            ? Lieu.Creer(TypeLieu.CentreFixe, nom, null, "5000", null, null, null)
            : Lieu.Creer(TypeLieu.CabinetEntreprise, nom, null, "5000", null, affilieId, null);
        Store.Lieux.Add(lieu);
        return lieu;
    }

    public Creneau AjouterCreneau(DateOnly jour, int heure, int minute = 0, string typeActe = "EVALUATION_PERIODIQUE", bool urgence = false, bool enLigne = false,
        Ressource? ressource = null, Lieu? lieu = null, int duree = 30, IEnumerable<Guid>? associees = null)
    {
        var debut = Instant(jour, heure, minute);
        var creneau = Creneau.Creer((ressource ?? Conseiller).Id, (lieu ?? Lieu).Id, debut, debut.AddMinutes(duree), typeActe, urgence, enLigne, associees);
        Store.Creneaux.Add(creneau);
        return creneau;
    }

    public ObligationAPlanifier AjouterObligation(string typeExamen = "EVALUATION_PERIODIQUE", Guid? personne = null, Guid? affilie = null, DateOnly? dateDue = null,
        DateOnly? dateLimite = null)
    {
        var obligation = new ObligationAPlanifier(Guid.CreateVersion7(), personne ?? Personne, affilie ?? Affilie, typeExamen, dateDue ?? Lundi, dateLimite,
            Horloge.GetUtcNow());
        Store.Obligations.Add(obligation);
        return obligation;
    }

    public ObligationCreeeHandler HandlerObligationCreee() => new(Store, Urgence, Parametres, Store);

    public Task ObligationCreee(string typeExamen, DateOnly dateDue, DateOnly? dateLimite = null, Guid? obligationId = null, Guid? personne = null) =>
        HandlerObligationCreee().HandleAsync(
            new ObligationCreee(obligationId ?? Guid.CreateVersion7(), personne ?? Personne, Affilie, typeExamen, dateDue, dateLimite),
            CancellationToken.None);

    public async Task<RendezVous> PlanifierRendezVous(Creneau creneau, params ObligationAPlanifier[] obligations)
    {
        var handler = new PlanifierRendezVousHandler(Store, Store, Prise, Store, Planificateur);
        var resultat = await handler.HandleAsync(
            new PlanifierRendezVous(creneau.Id, obligations.Length > 0 ? obligations[0].PersonneId : Personne, obligations.Length > 0 ? obligations[0].AffilieId : Affilie,
                obligations.Select(o => o.ObligationId).ToList(), null, null),
            CancellationToken.None);
        return Store.RendezVous.Single(r => r.Id == resultat.Value);
    }

    public ReservationUrgence NouvelleUrgence() => Urgence;

    public EmissionRappels Rappels() => new(Store, Store, Parametres, Store, Store, Horloge);
}
