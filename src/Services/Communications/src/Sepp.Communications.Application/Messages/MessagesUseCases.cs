using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Communications.Application.Expedition;
using Sepp.Communications.Domain.Messages;

namespace Sepp.Communications.Application.Messages;

public sealed record PreuveEnvoiDto(Guid Id, string Type, DateTimeOffset Horodatage, string Reference, string Empreinte);

/// <summary>Message du journal (DOC-05) ; le corps n'est rendu que par <see cref="ObtenirMessage"/>.</summary>
public sealed record MessageDto(
    Guid Id,
    TypeMessage Type,
    Canal Canal,
    bool Recommande,
    TypeDestinataire TypeDestinataire,
    Guid DestinataireId,
    Language Langue,
    string ObjetType,
    Guid ObjetId,
    string Sujet,
    StatutMessage Statut,
    int Tentatives,
    DateTimeOffset? ProchaineTentative,
    string? DerniereErreur,
    DateTimeOffset CreeLe,
    DateTimeOffset? EnvoyeLe)
{
    public static MessageDto From(Message m) => new(
        m.Id, m.Type, m.Canal, m.Recommande, m.TypeDestinataire, m.DestinataireId, m.Langue, m.ObjetType, m.ObjetId, m.Sujet, m.Statut,
        m.Tentatives, m.ProchaineTentative, m.DerniereErreur, m.CreeLe, m.EnvoyeLe);
}

public sealed record MessageDetailDto(MessageDto Message, string Corps, bool ContenuGenerique, IReadOnlyList<PreuveEnvoiDto> Preuves)
{
    public static MessageDetailDto From(Message m) => new(
        MessageDto.From(m), m.Corps, m.ContenuGenerique,
        m.Preuves.OrderBy(p => p.Horodatage).Select(p => new PreuveEnvoiDto(p.Id, p.Type, p.Horodatage, p.Reference, p.Empreinte)).ToList());
}

public sealed record PageMessagesDto(IReadOnlyList<MessageDto> Messages, int Total, int Page, int Taille);

/// <summary>DOC-05 : journal des messages, consultable par destinataire, objet, statut ou canal.</summary>
public sealed record RechercherMessages(
    TypeDestinataire? TypeDestinataire,
    Guid? DestinataireId,
    string? ObjetType,
    Guid? ObjetId,
    StatutMessage? Statut,
    Canal? Canal,
    int? Page,
    int? Taille);

public sealed class RechercherMessagesHandler(IMessageRepository messages, ICurrentUser currentUser) : IQueryHandler<RechercherMessages, PageMessagesDto>
{
    public const int TailleMaximale = 200;

    public async Task<Result<PageMessagesDto>> HandleAsync(RechercherMessages query, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.CommunicationsLire))
        {
            return Error.Forbidden("communications.interdit", "Droits insuffisants sur le journal des communications.");
        }

        var page = Math.Max(1, query.Page ?? 1);
        var taille = Math.Clamp(query.Taille ?? 50, 1, TailleMaximale);
        var (liste, total) = await messages.RechercherAsync(
            new FiltreMessages(query.TypeDestinataire, query.DestinataireId, query.ObjetType, query.ObjetId, query.Statut, query.Canal, page, taille), cancellationToken);
        return new PageMessagesDto(liste.Select(MessageDto.From).ToList(), total, page, taille);
    }
}

public sealed record ObtenirMessage(Guid Id);

public sealed class ObtenirMessageHandler(IMessageRepository messages, ICurrentUser currentUser) : IQueryHandler<ObtenirMessage, MessageDetailDto>
{
    public async Task<Result<MessageDetailDto>> HandleAsync(ObtenirMessage query, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.CommunicationsLire))
        {
            return Error.Forbidden("communications.interdit", "Droits insuffisants sur le journal des communications.");
        }

        var message = await messages.GetAsync(query.Id, cancellationToken);
        return message is null ? Error.NotFound("message.inconnu", "Message inconnu.") : MessageDetailDto.From(message);
    }
}

/// <summary>
/// Envoi manuel (DOC-03) : un gestionnaire écrit à un destinataire par un canal sécurisé. Par e-mail et SMS, seule la
/// notification générique est possible : le texte libre est refusé (aucune donnée sensible par ces canaux).
/// </summary>
/// <param name="Sujet">Sujet du message (canaux sécurisés).</param>
/// <param name="Corps">Corps du message (canaux sécurisés) ; ignoré par e-mail et SMS, où la notification générique est envoyée.</param>
public sealed record EnvoyerMessage(
    TypeDestinataire TypeDestinataire,
    Guid DestinataireId,
    Canal Canal,
    bool Recommande,
    string? Sujet,
    string? Corps);

public sealed class EnvoyerMessageHandler(
    IMessageRepository messages,
    IAnnuaireDestinataires annuaire,
    OptionsLiens liens,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    TimeProvider horloge) : ICommandHandler<EnvoyerMessage, Guid>
{
    public async Task<Result<Guid>> HandleAsync(EnvoyerMessage command, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.CommunicationsEnvoyer))
        {
            return Error.Forbidden("communications.interdit", "L'envoi manuel est réservé au gestionnaire, au planificateur et à l'assistant médical.");
        }

        if (command.TypeDestinataire == TypeDestinataire.Interne)
        {
            return Error.Validation("message.destinataire-interne", "L'envoi manuel s'adresse à un affilié ou à un travailleur.");
        }

        var destinataire = await annuaire.ObtenirAsync(command.TypeDestinataire, command.DestinataireId, cancellationToken);
        if (destinataire is null)
        {
            return Error.NotFound("destinataire.inconnu", "Destinataire inconnu.");
        }

        if (!destinataire.Joignabilite.Peut(command.Canal))
        {
            return Error.Validation("message.canal-injoignable", $"Le destinataire n'est pas joignable par le canal {command.Canal}.");
        }

        var id = Message.NouvelIdentifiant();
        var lien = liens.Pour(command.TypeDestinataire, id);
        ContenuMessage contenu;
        if (command.Canal.PeutPorterUnContenuDetaille())
        {
            if (string.IsNullOrWhiteSpace(command.Sujet) || string.IsNullOrWhiteSpace(command.Corps))
            {
                return Error.Validation("message.contenu", "Le sujet et le corps du message sont obligatoires pour ce canal.");
            }

            contenu = new ContenuMessage(command.Sujet.Trim(), $"{command.Corps.Trim()}\n{lien}", EstGenerique: false);
        }
        else
        {
            contenu = Gabarits.Generique(command.Canal, destinataire.Langue, lien);
        }

        Message message;
        try
        {
            message = Message.Creer(new NouveauMessage(id, TypeMessage.Manuel, command.Canal, command.Recommande, command.TypeDestinataire, command.DestinataireId,
                destinataire.Langue, "manuel", id, $"manuel:{id:D}", contenu), horloge.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Error.Validation("message.invalide", ex.Message);
        }

        messages.Add(message);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return message.Id;
    }
}

/// <summary>DOC-05 : relance d'un message abandonné.</summary>
public sealed record RelancerMessage(Guid Id);

public sealed class RelancerMessageHandler(IMessageRepository messages, IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider horloge)
    : ICommandHandler<RelancerMessage, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RelancerMessage command, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.CommunicationsAdministrer))
        {
            return Error.Forbidden("communications.interdit", "La relance des échecs est réservée au gestionnaire et à l'administrateur fonctionnel.");
        }

        var message = await messages.GetAsync(command.Id, cancellationToken);
        if (message is null)
        {
            return Error.NotFound("message.inconnu", "Message inconnu.");
        }

        try
        {
            message.Relancer(horloge.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Error.Conflict("message.statut", ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Lance immédiatement l'envoi des messages échus (en plus du traitement périodique).</summary>
public sealed record ExpedierMessagesEchus;

public sealed record ExpeditionDto(int Traites);

public sealed class ExpedierMessagesEchusHandler(ExpediteurMessages expediteur, ICurrentUser currentUser)
    : ICommandHandler<ExpedierMessagesEchus, ExpeditionDto>
{
    public async Task<Result<ExpeditionDto>> HandleAsync(ExpedierMessagesEchus command, CancellationToken cancellationToken) =>
        currentUser.HasPermission(Permissions.CommunicationsAdministrer)
            ? new ExpeditionDto(await expediteur.ExpedierEchusAsync(cancellationToken))
            : Error.Forbidden("communications.interdit", "Le lancement de l'envoi est réservé au gestionnaire et à l'administrateur fonctionnel.");
}
