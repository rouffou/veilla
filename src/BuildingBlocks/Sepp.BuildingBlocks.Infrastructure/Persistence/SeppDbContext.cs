using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.Contracts;

namespace Sepp.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Contexte de base de chaque service : une base par service (ARC-02).
/// Applique les conventions communes : colonnes d'audit et verrou optimiste (DAT-03),
/// suppression logique (DAT-05), outbox et inbox (ARC-31, ARC-32).
/// </summary>
public abstract class SeppDbContext : DbContext, IUnitOfWork, IIntegrationEventOutbox
{
    public const string CreatedAt = "created_at";
    public const string CreatedBy = "created_by";
    public const string UpdatedAt = "updated_at";
    public const string UpdatedBy = "updated_by";
    public const string Version = "version";
    public const string DeletedAt = "deleted_at";

    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly List<IntegrationEvent> _pendingEvents = [];

    protected SeppDbContext(DbContextOptions options, ICurrentUser currentUser, TimeProvider timeProvider)
        : base(options)
    {
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public void Add(IntegrationEvent integrationEvent) => _pendingEvents.Add(integrationEvent);

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        await SaveChangesAsync(cancellationToken);

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyConventions();
        OutboxMessages.AddRange(_pendingEvents.Select(OutboxMessage.From));
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        _pendingEvents.Clear();
        foreach (var aggregate in ChangeTracker.Entries<AggregateRoot>())
        {
            aggregate.Entity.ClearDomainEvents();
        }

        return result;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("Utiliser SaveChangesAsync.");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_message");
            b.HasKey(m => m.Id);
            b.Property(m => m.EventType).HasMaxLength(200);
            b.Property(m => m.Topic).HasMaxLength(100);
            b.Property(m => m.Payload).HasColumnType("jsonb");
            b.Property(m => m.CorrelationId).HasMaxLength(100);
            b.HasIndex(m => m.ProcessedAt).HasFilter("processed_at IS NULL");
        });

        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("inbox_message");
            b.HasKey(m => new { m.MessageId, m.Consumer });
            b.Property(m => m.Consumer).HasMaxLength(200);
        });

        ConfigureModel(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(Entity).IsAssignableFrom(t.ClrType) && !t.IsOwned()))
        {
            var builder = modelBuilder.Entity(entityType.ClrType);
            builder.Property<DateTimeOffset>(CreatedAt);
            builder.Property<string>(CreatedBy).HasMaxLength(100).IsRequired();
            builder.Property<DateTimeOffset>(UpdatedAt);
            builder.Property<string>(UpdatedBy).HasMaxLength(100).IsRequired();
            builder.Property<int>(Version).IsConcurrencyToken();

            // DAT-05 : suppression logique des racines d'agrégat ; les entités enfants suivent leur agrégat.
            if (entityType.BaseType is null && typeof(AggregateRoot).IsAssignableFrom(entityType.ClrType))
            {
                builder.Property<DateTimeOffset?>(DeletedAt);
                builder.HasQueryFilter(SoftDeleteFilter(entityType.ClrType));
            }
        }
    }

    /// <summary>Configuration du modèle propre au service.</summary>
    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    private void ApplyConventions()
    {
        var now = _timeProvider.GetUtcNow();
        var user = _currentUser.UserId;

        // Un agrégat modifié via ses seules entités enfants est lui aussi versionné et audité :
        // le verrou optimiste protège l'agrégat entier (ARC-22).
        foreach (var aggregate in ChangeTracker.Entries<AggregateRoot>()
                     .Where(e => e.State == EntityState.Unchanged && e.Entity.DomainEvents.Count > 0))
        {
            Touch(aggregate, now, user);
        }

        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(CreatedAt).CurrentValue = now;
                    entry.Property(CreatedBy).CurrentValue = user;
                    entry.Property(UpdatedAt).CurrentValue = now;
                    entry.Property(UpdatedBy).CurrentValue = user;
                    entry.Property(Version).CurrentValue = 1;
                    break;
                case EntityState.Modified:
                    Touch(entry, now, user);
                    break;
                case EntityState.Deleted when entry.Metadata.FindProperty(DeletedAt) is not null:
                    // DAT-05 : suppression logique ; seule la purge légale supprime physiquement.
                    entry.State = EntityState.Modified;
                    entry.Property(DeletedAt).CurrentValue = now;
                    Touch(entry, now, user);
                    break;
            }
        }
    }

    private static void Touch(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, DateTimeOffset now, string user)
    {
        entry.Property(UpdatedAt).CurrentValue = now;
        entry.Property(UpdatedBy).CurrentValue = user;
        entry.Property(Version).CurrentValue = (int)entry.Property(Version).OriginalValue! + 1;
    }

    private static LambdaExpression SoftDeleteFilter(Type type)
    {
        var parameter = Expression.Parameter(type, "e");
        var property = Expression.Call(
            typeof(EF), nameof(EF.Property), [typeof(DateTimeOffset?)], parameter, Expression.Constant(DeletedAt));
        return Expression.Lambda(Expression.Equal(property, Expression.Constant(null, typeof(DateTimeOffset?))), parameter);
    }
}
