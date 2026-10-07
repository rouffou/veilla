using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Communications.Application.Expedition;
using Sepp.Communications.Domain.Messages;
using Sepp.Contracts;

namespace Sepp.Communications.Application.Tests;

/// <summary>Adaptateurs en mémoire : la couche application est testée sans infrastructure (ARC-23).</summary>
internal sealed class InMemoryStore : IUnitOfWork, IIntegrationEventOutbox, IMessageRepository
{
    public List<Message> Messages { get; } = [];

    public List<IntegrationEvent> Published { get; } = [];

    public int Saves { get; private set; }

    /// <summary>Simule une autre instance qui a déjà réservé le message.</summary>
    public bool RefuserReservation { get; set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }

    public void Add(IntegrationEvent integrationEvent) => Published.Add(integrationEvent);

    public Task<Message?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Messages.SingleOrDefault(m => m.Id == id));

    public Task<bool> ExisteParCleAsync(string cleIdempotence, CancellationToken cancellationToken) =>
        Task.FromResult(Messages.Any(m => m.CleIdempotence == cleIdempotence));

    public Task<IReadOnlyList<Message>> ListerEchusAsync(DateTimeOffset maintenant, int limite, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Message>>(Messages.Where(m => m.EstEchu(maintenant)).OrderBy(m => m.ProchaineTentative).Take(limite).ToList());

    public Task<IReadOnlyList<Message>> ListerParObjetAsync(string objetType, Guid objetId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Message>>(Messages.Where(m => m.ObjetType == objetType && m.ObjetId == objetId).ToList());

    public Task<(IReadOnlyList<Message> Messages, int Total)> RechercherAsync(FiltreMessages filtre, CancellationToken cancellationToken)
    {
        var liste = Messages
            .Where(m => (filtre.TypeDestinataire is null || m.TypeDestinataire == filtre.TypeDestinataire)
                        && (filtre.DestinataireId is null || m.DestinataireId == filtre.DestinataireId)
                        && (filtre.Statut is null || m.Statut == filtre.Statut)
                        && (filtre.Canal is null || m.Canal == filtre.Canal))
            .ToList();
        return Task.FromResult<(IReadOnlyList<Message>, int)>((liste.Skip((filtre.Page - 1) * filtre.Taille).Take(filtre.Taille).ToList(), liste.Count));
    }

    public Task<bool> ReserverAsync(Message message, DateTimeOffset jusqua, CancellationToken cancellationToken) => Task.FromResult(!RefuserReservation);

    public void Add(Message message) => Messages.Add(message);
}

internal sealed class FakeUser(params string[] roles) : ICurrentUser
{
    public bool IsAuthenticated => true;

    public string UserId => "test-user";

    public IReadOnlySet<string> Roles { get; } = roles.ToHashSet();

    public bool HasPermission(string permission) => RolePermissions.For(Roles).Contains(permission);
}

internal sealed class FakeHorloge(DateTimeOffset debut) : TimeProvider
{
    private DateTimeOffset _maintenant = debut;

    public override DateTimeOffset GetUtcNow() => _maintenant;

    public void Avancer(TimeSpan duree) => _maintenant += duree;
}

internal sealed class FakeAnnuaire : IAnnuaireDestinataires
{
    public Dictionary<(TypeDestinataire, Guid), Destinataire?> Destinataires { get; } = [];

    public List<Destinataire> Dirigeants { get; } = [];

    public Destinataire Personne(Guid id, Language langue = Language.Fr, Canal? prefere = Canal.Email, string? email = "marie@exemple.test", string? telephone = "+3225550100",
        bool adresse = true)
    {
        var destinataire = new Destinataire(TypeDestinataire.Personne, id, langue, "Marie Exemple", email, telephone,
            adresse ? new AdressePostale("Marie Exemple", "Rue de l'Exemple", "1", null, "1000", "Bruxelles", "BE") : null, null, null, PortailActif: true, prefere);
        Destinataires[(TypeDestinataire.Personne, id)] = destinataire;
        return destinataire;
    }

    public Destinataire Affilie(Guid id)
    {
        var destinataire = new Destinataire(TypeDestinataire.Affilie, id, Language.Nl, "Entreprise Exemple", "contact@entreprise.test", null,
            new AdressePostale("Entreprise Exemple", "Rue du Travail", "2", null, "9000", "Gent", "BE"), "0123456789", null, PortailActif: true, null);
        Destinataires[(TypeDestinataire.Affilie, id)] = destinataire;
        return destinataire;
    }

    public Task<Destinataire?> ObtenirAsync(TypeDestinataire type, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(type == TypeDestinataire.Interne ? Dirigeants.FirstOrDefault(x => x.Id == id) : Destinataires.TryGetValue((type, id), out var d) ? d : null);

    public Task<IReadOnlyList<Destinataire>> ResoudreDirigeantsAsync(string zone, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Destinataire>>(Dirigeants);
}

/// <summary>Canal factice : enregistre les envois et peut échouer à la demande.</summary>
internal sealed class FakeCanal(Canal canal, TimeProvider horloge) : ICanalEnvoi
{
    public Canal Canal { get; } = canal;

    public List<Envoi> Envoyes { get; } = [];

    /// <summary>Erreurs à lever aux prochains envois, dans l'ordre.</summary>
    public Queue<ErreurEnvoiException> Pannes { get; } = new();

    public Task<ResultatEnvoi> EnvoyerAsync(Envoi envoi, CancellationToken cancellationToken)
    {
        if (Pannes.TryDequeue(out var panne))
        {
            throw panne;
        }

        Envoyes.Add(envoi);
        return Task.FromResult(new ResultatEnvoi($"accuse-{Canal}", $"ref-{envoi.MessageId:N}", horloge.GetUtcNow()));
    }
}

/// <summary>Assemble les cas d'usage autour des faux adaptateurs.</summary>
internal sealed class Contexte
{
    public Contexte(params string[] roles)
    {
        Horloge = new FakeHorloge(new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero));
        Canaux = Enum.GetValues<Canal>().ToDictionary(c => c, c => new FakeCanal(c, Horloge));
        Utilisateur = new FakeUser(roles);
        Liens = new OptionsLiens
        {
            PortailTravailleur = "https://travailleur.exemple.test/messages",
            PortailEmployeur = "https://employeur.exemple.test/messages",
            Interne = "https://interne.exemple.test/messages",
        };
        Createur = new CreateurMessages(Store, Annuaire, Liens, Horloge);
        Expediteur = new ExpediteurMessages(Store, Annuaire, Canaux.Values, Store, Horloge);
    }

    public InMemoryStore Store { get; } = new();

    public FakeAnnuaire Annuaire { get; } = new();

    public FakeHorloge Horloge { get; }

    public Dictionary<Canal, FakeCanal> Canaux { get; }

    public FakeUser Utilisateur { get; }

    public OptionsLiens Liens { get; }

    public CreateurMessages Createur { get; }

    public ExpediteurMessages Expediteur { get; }

    public IEnumerable<Envoi> Envoyes => Canaux.Values.SelectMany(c => c.Envoyes);
}
