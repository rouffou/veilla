using Microsoft.EntityFrameworkCore;

using Sepp.Communications.Application;
using Sepp.Communications.Domain.Messages;

namespace Sepp.Communications.Adapters.Persistence;

internal sealed class MessageRepository(CommunicationsDbContext db) : IMessageRepository
{
    public Task<Message?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Messages.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<bool> ExisteParCleAsync(string cleIdempotence, CancellationToken cancellationToken) =>
        db.Messages.Local.Any(m => m.CleIdempotence == cleIdempotence) ||
        await db.Messages.IgnoreQueryFilters().AnyAsync(m => m.CleIdempotence == cleIdempotence, cancellationToken);

    public async Task<IReadOnlyList<Message>> ListerEchusAsync(DateTimeOffset maintenant, int limite, CancellationToken cancellationToken) =>
        await db.Messages
            .Where(m => (m.Statut == StatutMessage.EnAttente || m.Statut == StatutMessage.EnEchec) && m.ProchaineTentative <= maintenant)
            .OrderBy(m => m.ProchaineTentative)
            .Take(limite)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Message>> ListerParObjetAsync(string objetType, Guid objetId, CancellationToken cancellationToken) =>
        await db.Messages.Where(m => m.ObjetType == objetType && m.ObjetId == objetId).ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<Message> Messages, int Total)> RechercherAsync(FiltreMessages filtre, CancellationToken cancellationToken)
    {
        var requete = db.Messages.AsNoTracking().AsQueryable();
        if (filtre.TypeDestinataire is { } type)
        {
            requete = requete.Where(m => m.TypeDestinataire == type);
        }

        if (filtre.DestinataireId is { } destinataire)
        {
            requete = requete.Where(m => m.DestinataireId == destinataire);
        }

        if (!string.IsNullOrWhiteSpace(filtre.ObjetType))
        {
            requete = requete.Where(m => m.ObjetType == filtre.ObjetType);
        }

        if (filtre.ObjetId is { } objet)
        {
            requete = requete.Where(m => m.ObjetId == objet);
        }

        if (filtre.Statut is { } statut)
        {
            requete = requete.Where(m => m.Statut == statut);
        }

        if (filtre.Canal is { } canal)
        {
            requete = requete.Where(m => m.Canal == canal);
        }

        var total = await requete.CountAsync(cancellationToken);
        var messages = await requete
            .OrderByDescending(m => m.CreeLe).ThenByDescending(m => m.Id)
            .Skip((filtre.Page - 1) * filtre.Taille).Take(filtre.Taille)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return (messages, total);
    }

    public async Task<bool> ReserverAsync(Message message, DateTimeOffset jusqua, CancellationToken cancellationToken)
    {
        var attendue = message.ProchaineTentative;
        var lignes = await db.Messages
            .Where(m => m.Id == message.Id && m.ProchaineTentative == attendue)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProchaineTentative, jusqua), cancellationToken);
        return lignes == 1;
    }

    public void Add(Message message) => db.Messages.Add(message);
}
