using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Referentiels;
using Sepp.Referentiels.Domain.Nomenclatures;

namespace Sepp.Referentiels.Application.Nomenclatures;

public sealed record NomenclatureDto(Guid Id, string Code, string Libelle, int Version, IReadOnlyList<EntreeDto> Entrees);

public sealed record EntreeDto(string Code, string Libelle, string? CodeParent, DateOnly ValideDu, DateOnly? ValideJusquAu);

public sealed record LibellesDto(string Fr, string Nl, string De, string? En)
{
    public LocalizedLabel ToLabel() => new(Fr, Nl, De, En);
}

public sealed record ListerNomenclatures(Language Langue);

public sealed class ListerNomenclaturesHandler(INomenclatureRepository repository)
    : IQueryHandler<ListerNomenclatures, IReadOnlyList<NomenclatureDto>>
{
    public async Task<Result<IReadOnlyList<NomenclatureDto>>> HandleAsync(ListerNomenclatures query, CancellationToken cancellationToken)
    {
        var nomenclatures = await repository.ListAsync(cancellationToken);
        return nomenclatures.OrderBy(n => n.Code, StringComparer.Ordinal)
            .Select(n => new NomenclatureDto(n.Id, n.Code, n.Libelle.In(query.Langue), n.NumeroVersion, []))
            .ToList();
    }
}

/// <summary>Contenu d'une nomenclature à une date, dans une langue (DAT-07).</summary>
public sealed record ObtenirNomenclature(string Code, DateOnly Date, Language Langue);

public sealed class ObtenirNomenclatureHandler(INomenclatureRepository repository)
    : IQueryHandler<ObtenirNomenclature, NomenclatureDto>
{
    public async Task<Result<NomenclatureDto>> HandleAsync(ObtenirNomenclature query, CancellationToken cancellationToken)
    {
        var nomenclature = await repository.GetAsync(query.Code.Trim().ToUpperInvariant(), cancellationToken);
        if (nomenclature is null)
        {
            return Error.NotFound("nomenclature.inconnue", $"Nomenclature '{query.Code}' inconnue.");
        }

        return new NomenclatureDto(
            nomenclature.Id,
            nomenclature.Code,
            nomenclature.Libelle.In(query.Langue),
            nomenclature.NumeroVersion,
            nomenclature.EntreesAu(query.Date)
                .Select(e => new EntreeDto(e.Code, e.Libelle.In(query.Langue), e.CodeParent, e.Validite.ValidFrom, e.Validite.ValidTo))
                .ToList());
    }
}

public sealed record CreerNomenclature(string Code, LibellesDto Libelle);

public sealed class CreerNomenclatureHandler(
    INomenclatureRepository repository,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    ICurrentUser currentUser) : ICommandHandler<CreerNomenclature, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreerNomenclature command, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.ReferentielsAdministrer))
        {
            return Error.Forbidden("referentiels.interdit", "Seul l'administrateur fonctionnel peut créer une nomenclature.");
        }

        if (await repository.GetAsync(command.Code.Trim().ToUpperInvariant(), cancellationToken) is not null)
        {
            return Error.Conflict("nomenclature.existe", $"La nomenclature '{command.Code}' existe déjà.");
        }

        Nomenclature nomenclature;
        try
        {
            nomenclature = Nomenclature.Creer(command.Code, command.Libelle.ToLabel());
        }
        catch (DomainException ex)
        {
            return Error.Validation("nomenclature.invalide", ex.Message);
        }

        repository.Add(nomenclature);
        outbox.Add(new NomenclatureModifiee(nomenclature.Id, nomenclature.Code, nomenclature.NumeroVersion));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return nomenclature.Id;
    }
}

public sealed record AjouterEntree(string Nomenclature, string Code, LibellesDto Libelle, DateOnly ValideDu, string? CodeParent);

public sealed class AjouterEntreeHandler(
    INomenclatureRepository repository,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    ICurrentUser currentUser) : ICommandHandler<AjouterEntree, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AjouterEntree command, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.ReferentielsAdministrer))
        {
            return Error.Forbidden("referentiels.interdit", "Seul l'administrateur fonctionnel peut modifier une nomenclature.");
        }

        var nomenclature = await repository.GetAsync(command.Nomenclature.Trim().ToUpperInvariant(), cancellationToken);
        if (nomenclature is null)
        {
            return Error.NotFound("nomenclature.inconnue", $"Nomenclature '{command.Nomenclature}' inconnue.");
        }

        try
        {
            nomenclature.AjouterEntree(command.Code, command.Libelle.ToLabel(), command.ValideDu, command.CodeParent);
        }
        catch (DomainException ex)
        {
            return Error.Validation("nomenclature.entree-invalide", ex.Message);
        }

        outbox.Add(new NomenclatureModifiee(nomenclature.Id, nomenclature.Code, nomenclature.NumeroVersion));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
