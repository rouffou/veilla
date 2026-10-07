using System.Security.Cryptography;
using System.Text;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts;
using Sepp.Documents.Application.Generation;
using Sepp.Documents.Application.Securite;
using Sepp.Documents.Domain.Commun;
using Sepp.Documents.Domain.Documents;
using Sepp.Documents.Domain.Fusion;
using Sepp.Documents.Domain.Langues;
using Sepp.Documents.Domain.Modeles;

namespace Sepp.Documents.Application.Tests;

/// <summary>Adaptateurs en mémoire : la couche application est testée sans infrastructure (ARC-23).</summary>
internal sealed class InMemoryStore : IUnitOfWork, IIntegrationEventOutbox, IModeleRepository, IDocumentRepository, IJournalAcces
{
    private readonly List<IntegrationEvent> _pending = [];

    public List<Modele> Modeles { get; } = [];

    public List<Document> Documents { get; } = [];

    public List<IntegrationEvent> Published { get; } = [];

    public List<(ActionAudit Action, ZoneDocument Zone, Guid DocumentId)> Acces { get; } = [];

    public int Saves { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        Published.AddRange(_pending);
        _pending.Clear();
        return Task.CompletedTask;
    }

    public void Add(IntegrationEvent integrationEvent) => _pending.Add(integrationEvent);

    public Task<Modele?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Modeles.SingleOrDefault(m => m.Id == id));

    public Task<Modele?> PublieAsync(string code, Language langue, CancellationToken cancellationToken) =>
        Task.FromResult(Modeles.SingleOrDefault(m => m.Code == code && m.Langue == langue && m.Statut == StatutModele.Publie));

    public Task<IReadOnlyList<Modele>> ListAsync(string? code, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Modele>>(Modeles.Where(m => code is null || m.Code == code).ToList());

    public void Add(Modele modele) => Modeles.Add(modele);

    Task<Document?> IDocumentRepository.GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Documents.SingleOrDefault(d => d.Id == id));

    public Task<Document?> ParCleIdempotenceAsync(string cle, CancellationToken cancellationToken) =>
        Task.FromResult(Documents.SingleOrDefault(d => d.CleIdempotence == cle));

    public Task<IReadOnlyList<Document>> ListAsync(FiltreDocuments filtre, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Document>>(Documents
            .Where(d => (filtre.ObjetId is null || d.ObjetId == filtre.ObjetId) && (filtre.DestinataireId is null || d.DestinataireId == filtre.DestinataireId))
            .ToList());

    public void Add(Document document) => Documents.Add(document);

    public void Enregistrer(ActionAudit action, ZoneDocument zone, Guid documentId, string? motif = null) => Acces.Add((action, zone, documentId));

    public Task EnregistrerLectureAsync(ZoneDocument zone, Guid documentId, string? motif, CancellationToken cancellationToken)
    {
        Enregistrer(ActionAudit.Lecture, zone, documentId, motif);
        return Task.CompletedTask;
    }
}

internal sealed class FakeUser(string userId, params string[] roles) : ICurrentUser
{
    public FakeUser(params string[] roles) : this("test-user", roles)
    {
    }

    public bool IsAuthenticated => true;

    public string UserId { get; } = userId;

    public IReadOnlySet<string> Roles { get; } = roles.ToHashSet();

    public bool HasPermission(string permission) => RolePermissions.For(Roles).Contains(permission);
}

internal sealed class FakePerimetre : IPerimetreExterne
{
    public IReadOnlyCollection<Guid> Affilies { get; set; } = [];

    public Guid? PersonneId { get; set; }
}

/// <summary>PDF factice : le texte fusionné précédé d'un en-tête, pour vérifier le contenu sans moteur PDF.</summary>
internal sealed class FakeRendu : IRenduPdf
{
    public RenduPdf Rendre(DocumentFusionne document, MetadonneesPdf metadonnees) =>
        new(Encoding.UTF8.GetBytes($"%PDF-FAKE {metadonnees.Langue}\n{document.TexteIntegral}"), "PDF/A-1a");

    public static string Texte(byte[] pdf) => Encoding.UTF8.GetString(pdf);
}

/// <summary>Chiffrement factice lié à la zone et au document, avec un contrôle d'intégrité (comme AES-GCM).</summary>
internal sealed class FakeChiffrement : IChiffrementDocuments
{
    public ContenuChiffre Chiffrer(ZoneDocument zone, Guid documentId, byte[] clair)
    {
        var marque = Encoding.UTF8.GetBytes($"{zone.Code()}|{documentId:N}|");
        byte[] corps = [.. clair.Select(b => (byte)(b ^ 0x5A))];
        var sceau = SHA256.HashData([.. marque, .. corps]);
        return new ContenuChiffre([.. marque, .. corps, .. sceau], $"cle-{zone.Code()}");
    }

    public byte[] Dechiffrer(ZoneDocument zone, Guid documentId, byte[] chiffre)
    {
        var marque = Encoding.UTF8.GetBytes($"{zone.Code()}|{documentId:N}|");
        if (chiffre.Length < marque.Length + 32 || !chiffre.AsSpan(0, marque.Length).SequenceEqual(marque))
        {
            throw new ContenuAltereException("Contenu étranger à ce document ou à cette zone.");
        }

        var corps = chiffre.AsSpan(marque.Length, chiffre.Length - marque.Length - 32).ToArray();
        if (!SHA256.HashData(chiffre.AsSpan(0, chiffre.Length - 32)).AsSpan().SequenceEqual(chiffre.AsSpan(chiffre.Length - 32)))
        {
            throw new ContenuAltereException("Contenu altéré.");
        }

        return [.. corps.Select(b => (byte)(b ^ 0x5A))];
    }
}

internal sealed class FakeStockage : IStockageDocuments
{
    public Dictionary<string, byte[]> Objets { get; } = [];

    public Task<string> EcrireAsync(ZoneDocument zone, string nom, byte[] contenu, IReadOnlyDictionary<string, string> metadonnees, CancellationToken cancellationToken)
    {
        var uri = $"fake://documents-{zone.Code()}/{nom}";
        if (!Objets.TryAdd(uri, contenu))
        {
            throw new InvalidOperationException("Écriture unique.");
        }

        return Task.FromResult(uri);
    }

    public Task<byte[]?> LireAsync(string uri, CancellationToken cancellationToken) =>
        Task.FromResult(Objets.TryGetValue(uri, out var contenu) ? contenu : null);
}

internal sealed class FakeHorodatage(TimeProvider horloge) : IServiceHorodatage
{
    public Task<Horodatage> HorodaterAsync(string empreinte, CancellationToken cancellationToken) =>
        Task.FromResult(new Horodatage(horloge.GetUtcNow(), "test", "jeton-" + empreinte[..8]));
}

internal sealed class FakeSignature(TimeProvider horloge) : ISignatureQualifiee
{
    public Task<PreuveSignature> SignerAsync(Guid documentId, string empreinte, string signataireId, CancellationToken cancellationToken) =>
        Task.FromResult(new PreuveSignature("qualifiee-simulee", horloge.GetUtcNow(), $"signature:{documentId}:{empreinte}:{signataireId}"));
}

internal sealed class FakeSourceLinguistique : ISourceLinguistique
{
    public InformationsLinguistiques Informations { get; set; } = new(RegimeLinguistique.Francais, Language.Fr, null);

    public Task<InformationsLinguistiques> ObtenirAsync(Guid? affilieId, Guid? personneId, CancellationToken cancellationToken) =>
        Task.FromResult(Informations);
}

internal sealed class FakeHorloge(DateTimeOffset debut) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => debut;
}

/// <summary>Assemble les cas d'usage autour des faux adaptateurs.</summary>
internal sealed class Contexte
{
    public Contexte(params string[] roles) : this("test-user", roles)
    {
    }

    public Contexte(string userId, string[] roles)
    {
        Horloge = new FakeHorloge(new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero));
        Utilisateur = new FakeUser(userId, roles);
        Acces = new AccesDocuments(Utilisateur, Perimetre);
        Generateur = new GenerateurDocuments(Store, Store, new FakeRendu(), Chiffrement, Stockage, new FakeHorodatage(Horloge), Source, Store, Horloge);
    }

    public InMemoryStore Store { get; } = new();

    public FakeStockage Stockage { get; } = new();

    public FakeChiffrement Chiffrement { get; } = new();

    public FakeSourceLinguistique Source { get; } = new();

    public FakePerimetre Perimetre { get; } = new();

    public FakeHorloge Horloge { get; }

    public FakeUser Utilisateur { get; }

    public AccesDocuments Acces { get; }

    public GenerateurDocuments Generateur { get; }

    /// <summary>Crée, valide et publie un modèle (le modèle de départ est en brouillon : il doit être validé par un humain).</summary>
    public Modele Publie(string code, Language langue, ZoneDocument zone, string contenu, params ChampDeclare[] champs)
    {
        var modele = Modele.Creer(code, langue, TypeModele.Formulaire, zone, $"Modèle {code}", null, contenu, champs);
        modele.Valider("valideur", Horloge.GetUtcNow());
        modele.Publier("admin", Horloge.GetUtcNow());
        Store.Add(modele);
        return modele;
    }
}
