using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts;
using Sepp.Contracts.Integrations;
using Sepp.Contracts.Reintegration;
using Sepp.Reintegration.Application.Incapacites;
using Sepp.Reintegration.Application.Projections;
using Sepp.Reintegration.Application.Trajets;
using Sepp.Reintegration.Domain.Trajets;

using Shouldly;

namespace Sepp.Reintegration.Application.Tests;

/// <summary>
/// Monde de test : un magasin partagé, un utilisateur et un contexte d'accès par appel ; les cas d'usage sont composés par
/// réflexion à partir des faux (même graphe qu'en production, sans conteneur).
/// </summary>
internal sealed class Monde
{
    public static readonly DateTimeOffset Debut = new(2026, 3, 2, 8, 0, 0, TimeSpan.Zero);

    public Monde() => Audit = new FakeAudit(Store);

    public InMemoryStore Store { get; } = new();

    public FakeAudit Audit { get; }

    public FakeContexte Contexte { get; private set; } = new();

    public FakeHorloge Horloge { get; } = new(Debut);

    public FakeCanal Canal { get; } = new();

    public OptionsReintegration Options { get; } = new();

    public ICurrentUser Utilisateur { get; private set; } = new FakeUser("cpmt-1", Roles.Cpmt);

    public Guid Personne { get; } = Guid.CreateVersion7();

    public Guid Affilie { get; } = Guid.CreateVersion7();

    public CancellationToken Ct { get; } = TestContext.Current.CancellationToken;

    public Monde Agir(string userId, string role, string? motif = null, bool brisDeGlace = false, Guid? affilie = null, Guid? personne = null)
    {
        Utilisateur = new FakeUser(userId, role);
        Contexte = new FakeContexte { Motif = motif, BrisDeGlace = brisDeGlace, AffilieIdJeton = affilie, PersonneIdJeton = personne };
        return this;
    }

    public T Creer<T>() => (T)Creer(typeof(T));

    public async Task<TResult> Ok<TCommand, TResult>(ICommandHandler<TCommand, TResult> handler, TCommand command)
    {
        var resultat = await handler.HandleAsync(command, Ct);
        resultat.IsSuccess.ShouldBeTrue(resultat.Error?.Message);
        return resultat.Value;
    }

    /// <summary>Incapacité notifiée par le flux eHealth (aucun gestionnaire : hors relation de soin).</summary>
    public async Task<Guid> NotifierAsync(DateOnly? debut = null)
    {
        var e = new IncapaciteNotifiee(Guid.CreateVersion7(), Personne, Affilie, debut ?? new DateOnly(2026, 3, 2), CodesReintegration.SourcesIncapacite.Employeur, new DateOnly(2026, 4, 30));
        await Creer<IncapaciteNotifieeHandler>().HandleAsync(e, Ct);
        return e.IncapaciteId;
    }

    /// <summary>Incapacité saisie par le CPMT courant (gestionnaire) et trajet demandé.</summary>
    public async Task<(Guid Incapacite, Guid Trajet)> TrajetAsync(DateOnly? demande = null)
    {
        var incapacite = await Ok(Creer<SaisirIncapaciteHandler>(), new SaisirIncapacite(Personne, Affilie, new DateOnly(2026, 3, 2), null));
        var trajet = await Ok(Creer<DemanderTrajetHandler>(), new DemanderTrajet(incapacite, InitiateurTrajet.Travailleur, demande ?? new DateOnly(2026, 5, 4)));
        return (incapacite, trajet);
    }

    public IReadOnlyList<T> Publies<T>()
        where T : IntegrationEvent => [.. Store.Published.OfType<T>()];

    private object Creer(Type type)
    {
        if (Instance(type) is { } instance)
        {
            return instance;
        }

        if (type.IsInterface && type.IsGenericType)
        {
            var implementation = typeof(DependencyInjection).Assembly.GetTypes()
                .Single(t => t is { IsClass: true, IsAbstract: false } && t.GetInterfaces().Contains(type));
            return Creer(implementation);
        }

        var constructeur = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        return constructeur.Invoke([.. constructeur.GetParameters().Select(p => Creer(p.ParameterType))]);
    }

    private object? Instance(Type type) => type switch
    {
        _ when type == typeof(ICurrentUser) => Utilisateur,
        _ when type == typeof(IContexteAcces) => Contexte,
        _ when type == typeof(IAuditTrail) => Audit,
        _ when type == typeof(TimeProvider) => Horloge,
        _ when type == typeof(ICanalEHealth) => Canal,
        _ when type == typeof(OptionsReintegration) => Options,
        _ when type.IsInstanceOfType(Store) && type.IsInterface => Store,
        _ => null,
    };
}
