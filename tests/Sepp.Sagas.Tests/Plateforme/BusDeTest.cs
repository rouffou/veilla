using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.Testing.Architecture;

namespace Sepp.Sagas.Tests.Plateforme;

/// <summary>Message publié par un hôte, tel que le verrait Service Bus (sujet = nom versionné du contrat, ADR 0004).</summary>
public sealed record MessagePublie(string Source, Guid Id, string Sujet, string Rubrique, string Charge, DateTimeOffset OccurredAt);

/// <summary>Message qui n'a pu être traité après le nombre maximal de livraisons (lettre morte).</summary>
public sealed record LettreMorte(string Destinataire, MessagePublie Message, string Erreur);

/// <summary>
/// Bus d'événements de test (ARC-31, ARC-32) : remplace le publieur Service Bus de chaque hôte. Un message publié est
/// livré aux hôtes abonnés selon <b>la même table de routage que Terraform</b> : <c>subscribes_to</c> et
/// <c>subject_filters</c> de <c>infra/variables.tf</c>. La livraison est au moins une fois ; en mode chaos chaque message
/// est livré deux fois, dans un ordre mélangé (graine fixe, donc reproductible).
/// </summary>
public sealed class BusDeTest
{
    /// <summary>Nombre maximal de livraisons d'un message à un abonné, comme <c>max_delivery_count</c> de l'abonnement.</summary>
    public const int LivraisonsMax = 10;

    private readonly VariablesTerraform _terraform = VariablesTerraform.Lire();
    private readonly Dictionary<string, Func<Guid, string, string, CancellationToken, Task<int>>> _abonnes = new(StringComparer.Ordinal);
    private readonly List<Livraison> _file = [];
    private readonly List<Livraison> _retenues = [];
    private readonly List<MessagePublie> _publies = [];
    private readonly List<LettreMorte> _lettresMortes = [];
    private readonly Lock _verrou = new();
    private Random _hasard = new(20261009);

    /// <summary>Livre chaque message deux fois, dans un ordre mélangé.</summary>
    public bool Chaos { get; set; }

    /// <summary>Nombre de livraisons tentées depuis le début (une par abonné et par message, deux en mode chaos).</summary>
    public int Livraisons { get; private set; }

    /// <summary>Tous les messages publiés depuis le début, dans l'ordre de publication.</summary>
    public IReadOnlyList<MessagePublie> Publies
    {
        get
        {
            lock (_verrou)
            {
                return [.. _publies];
            }
        }
    }

    public IReadOnlyList<LettreMorte> LettresMortes
    {
        get
        {
            lock (_verrou)
            {
                return [.. _lettresMortes];
            }
        }
    }

    /// <summary>Sujets (noms versionnés de contrats) dont la livraison est suspendue jusqu'à <see cref="Relacher"/> : ordre inversé forcé.</summary>
    public HashSet<string> Retenus { get; } = new(StringComparer.Ordinal);

    public void Reinitialiser(int graine)
    {
        lock (_verrou)
        {
            _file.Clear();
            _retenues.Clear();
            Retenus.Clear();
            Chaos = false;
            _hasard = new Random(graine);
        }
    }

    /// <summary>Remet en livraison les messages retenus et cesse d'en retenir.</summary>
    public void Relacher()
    {
        lock (_verrou)
        {
            Retenus.Clear();
            _file.AddRange(_retenues);
            _retenues.Clear();
        }
    }

    public void Abonner(string service, Func<Guid, string, string, CancellationToken, Task<int>> distribuer) => _abonnes[service] = distribuer;

    /// <summary>Appelé par le publieur de l'hôte émetteur, depuis le processeur de l'outbox.</summary>
    public void Publier(string source, OutboxMessage message)
    {
        var publie = new MessagePublie(source, message.Id, message.EventType, message.Topic, message.Payload, message.OccurredAt);
        lock (_verrou)
        {
            _publies.Add(publie);
            foreach (var destinataire in Destinataires(publie))
            {
                _file.Add(new Livraison(destinataire, publie, 1));
            }
        }
    }

    /// <summary>Services hébergés abonnés à la rubrique du message et acceptant son sujet (table de routage Terraform).</summary>
    public IReadOnlyList<string> Destinataires(MessagePublie message) =>
    [
        .. _abonnes.Keys
            .Where(service => _terraform.Services.TryGetValue(service, out var s)
                && s.SubscribesTo.Contains(message.Rubrique, StringComparer.Ordinal)
                && (!s.SubjectFilters.TryGetValue(message.Rubrique, out var sujets) || sujets.Contains(message.Sujet, StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>Livre les messages en attente (une passe). Renvoie le nombre de livraisons tentées.</summary>
    public async Task<int> LivrerAsync(CancellationToken cancellationToken)
    {
        List<Livraison> lot;
        lock (_verrou)
        {
            lot = [.. _file.Where(l => !Retenus.Contains(l.Message.Sujet))];
            _retenues.AddRange(_file.Where(l => Retenus.Contains(l.Message.Sujet)));
            _file.Clear();
            if (Chaos)
            {
                // Chaque livraison est doublée puis l'ensemble est mélangé : doublons et ordre inversé.
                lot = [.. lot, .. lot];
                _hasard.Shuffle(CollectionsMarshalAsSpan(lot));
            }
        }

        foreach (var livraison in lot)
        {
            Livraisons++;
            try
            {
                await _abonnes[livraison.Destinataire](livraison.Message.Id, livraison.Message.Sujet, livraison.Message.Charge, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lock (_verrou)
                {
                    if (livraison.Tentative >= LivraisonsMax)
                    {
                        _lettresMortes.Add(new LettreMorte(livraison.Destinataire, livraison.Message, ex.Message));
                    }
                    else
                    {
                        _file.Add(livraison with { Tentative = livraison.Tentative + 1 });
                    }
                }
            }
        }

        return lot.Count;
    }

    private static Span<Livraison> CollectionsMarshalAsSpan(List<Livraison> liste) => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(liste);

    private sealed record Livraison(string Destinataire, MessagePublie Message, int Tentative);
}

/// <summary>Publieur de l'hôte : remplace Service Bus (ou la publication en mémoire) et alimente le bus de test.</summary>
internal sealed class PublieurDeTest(string source, BusDeTest bus) : IMessagePublisher
{
    public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        bus.Publier(source, message);
        return Task.CompletedTask;
    }
}
