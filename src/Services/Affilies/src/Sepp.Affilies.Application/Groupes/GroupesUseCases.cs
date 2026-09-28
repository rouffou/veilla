using Sepp.Affilies.Application.Securite;
using Sepp.Affilies.Domain.Groupes;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;

namespace Sepp.Affilies.Application.Groupes;

// AFF-02 — Groupes d'entreprises : regroupement facultatif d'affiliés, géré par le gestionnaire de dossiers.

public sealed record GroupeDto(Guid Id, string Nom);

public sealed record ListerGroupes;

public sealed class ListerGroupesHandler(IGroupeRepository groupes, ControleAcces acces) : IQueryHandler<ListerGroupes, IReadOnlyList<GroupeDto>>
{
    public async Task<Result<IReadOnlyList<GroupeDto>>> HandleAsync(ListerGroupes query, CancellationToken cancellationToken)
    {
        if (acces.VerifierPermissionLecture() is { } refus)
        {
            return refus;
        }

        if (acces.PerimetreLecture is not null)
        {
            return Error.Forbidden("groupe.interdit", "La liste des groupes est réservée aux utilisateurs internes.");
        }

        var liste = await groupes.ListAsync(cancellationToken);
        return liste.OrderBy(g => g.Nom, StringComparer.CurrentCultureIgnoreCase).Select(g => new GroupeDto(g.Id, g.Nom)).ToList();
    }
}

public sealed record CreerGroupe(string Nom);

public sealed class CreerGroupeHandler(IGroupeRepository groupes, IUnitOfWork unitOfWork, ControleAcces acces) : ICommandHandler<CreerGroupe, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreerGroupe command, CancellationToken cancellationToken)
    {
        if (acces.VerifierGestion() is { } refus)
        {
            return refus;
        }

        Groupe groupe;
        try
        {
            groupe = Groupe.Creer(command.Nom);
        }
        catch (DomainException ex)
        {
            return Error.Validation("groupe.invalide", ex.Message);
        }

        groupes.Add(groupe);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return groupe.Id;
    }
}

public sealed record RenommerGroupe(Guid GroupeId, string Nom);

public sealed class RenommerGroupeHandler(IGroupeRepository groupes, IUnitOfWork unitOfWork, ControleAcces acces) : ICommandHandler<RenommerGroupe, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RenommerGroupe command, CancellationToken cancellationToken)
    {
        if (acces.VerifierGestion() is { } refus)
        {
            return refus;
        }

        var groupe = await groupes.GetAsync(command.GroupeId, cancellationToken);
        if (groupe is null)
        {
            return Error.NotFound("groupe.inconnu", $"Groupe {command.GroupeId} inconnu.");
        }

        try
        {
            groupe.Renommer(command.Nom);
        }
        catch (DomainException ex)
        {
            return Error.Validation("groupe.invalide", ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
