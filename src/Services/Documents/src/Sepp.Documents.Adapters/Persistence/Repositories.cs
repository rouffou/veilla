using Microsoft.EntityFrameworkCore;

using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Application;
using Sepp.Documents.Domain.Documents;
using Sepp.Documents.Domain.Modeles;

namespace Sepp.Documents.Adapters.Persistence;

internal sealed class ModeleRepository(DocumentsDbContext db) : IModeleRepository
{
    public Task<Modele?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Modeles.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);

    public Task<Modele?> PublieAsync(string code, Language langue, CancellationToken cancellationToken) =>
        db.Modeles.SingleOrDefaultAsync(m => m.Code == code && m.Langue == langue && m.Statut == StatutModele.Publie, cancellationToken);

    public async Task<IReadOnlyList<Modele>> ListAsync(string? code, CancellationToken cancellationToken) =>
        await db.Modeles.Where(m => code == null || m.Code == code).AsSplitQuery().ToListAsync(cancellationToken);

    public void Add(Modele modele) => db.Modeles.Add(modele);
}

internal sealed class DocumentRepository(DocumentsDbContext db) : IDocumentRepository
{
    public Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Documents.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<Document?> ParCleIdempotenceAsync(string cle, CancellationToken cancellationToken) =>
        db.Documents.SingleOrDefaultAsync(d => d.CleIdempotence == cle, cancellationToken);

    public async Task<IReadOnlyList<Document>> ListAsync(FiltreDocuments filtre, CancellationToken cancellationToken)
    {
        var requete = db.Documents.AsQueryable();
        if (filtre.ObjetId is { } objet)
        {
            requete = requete.Where(d => d.ObjetId == objet);
        }

        if (!string.IsNullOrWhiteSpace(filtre.ObjetType))
        {
            requete = requete.Where(d => d.ObjetType == filtre.ObjetType);
        }

        if (filtre.DestinataireId is { } destinataire)
        {
            requete = requete.Where(d => d.DestinataireId == destinataire);
        }

        if (filtre.TypeDestinataire is { } type)
        {
            requete = requete.Where(d => d.TypeDestinataire == type);
        }

        return await requete.OrderByDescending(d => d.Date).Take(500).AsSplitQuery().ToListAsync(cancellationToken);
    }

    public void Add(Document document) => db.Documents.Add(document);
}
