using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Referentiels;
using Sepp.Referentiels.Domain.Parametres;

namespace Sepp.Referentiels.Application.Parametres;

public sealed record ParametreDto(
    string Code,
    string Libelle,
    string Unite,
    string BaseLegale,
    decimal? ValeurCourante,
    IReadOnlyList<ValeurDto> Historique);

public sealed record ValeurDto(decimal Valeur, DateOnly ValideDu, DateOnly? ValideJusquAu);

internal static class ParametreMapping
{
    public static ParametreDto ToDto(this ParametreLegal p, DateOnly date, Language langue) => new(
        p.Code.Value,
        p.Libelle.In(langue),
        p.Unite.ToString(),
        p.BaseLegale,
        p.ValeurAu(date),
        p.Valeurs.OrderBy(v => v.Validite.ValidFrom).Select(v => new ValeurDto(v.Valeur, v.Validite.ValidFrom, v.Validite.ValidTo)).ToList());

    public static bool TryParseCode(string code, out CodeParametre? parsed)
    {
        try
        {
            parsed = new CodeParametre(code);
            return true;
        }
        catch (DomainException)
        {
            parsed = null;
            return false;
        }
    }
}

public sealed record ListerParametres(DateOnly Date, Language Langue);

public sealed class ListerParametresHandler(IParametreLegalRepository repository)
    : IQueryHandler<ListerParametres, IReadOnlyList<ParametreDto>>
{
    public async Task<Result<IReadOnlyList<ParametreDto>>> HandleAsync(ListerParametres query, CancellationToken cancellationToken)
    {
        var parametres = await repository.ListAsync(cancellationToken);
        return parametres.OrderBy(p => p.Code.Value, StringComparer.Ordinal)
            .Select(p => p.ToDto(query.Date, query.Langue))
            .ToList();
    }
}

public sealed record ObtenirParametre(string Code, DateOnly Date, Language Langue);

public sealed class ObtenirParametreHandler(IParametreLegalRepository repository)
    : IQueryHandler<ObtenirParametre, ParametreDto>
{
    public async Task<Result<ParametreDto>> HandleAsync(ObtenirParametre query, CancellationToken cancellationToken)
    {
        if (!ParametreMapping.TryParseCode(query.Code, out var code))
        {
            return Error.Validation("parametre.code-invalide", $"Code de paramètre invalide : '{query.Code}'.");
        }

        var parametre = await repository.GetAsync(code!, cancellationToken);
        return parametre is null
            ? Error.NotFound("parametre.inconnu", $"Paramètre légal '{query.Code}' inconnu.")
            : parametre.ToDto(query.Date, query.Langue);
    }
}

/// <summary>Modifie un paramètre légal à partir d'une date (administrateur fonctionnel, NF-61).</summary>
public sealed record DefinirValeurParametre(string Code, decimal Valeur, DateOnly ValideDu);

public sealed class DefinirValeurParametreHandler(
    IParametreLegalRepository repository,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    ICurrentUser currentUser) : ICommandHandler<DefinirValeurParametre, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DefinirValeurParametre command, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.ReferentielsAdministrer))
        {
            return Error.Forbidden("referentiels.interdit", "Seul l'administrateur fonctionnel peut modifier un paramètre légal.");
        }

        if (!ParametreMapping.TryParseCode(command.Code, out var code))
        {
            return Error.Validation("parametre.code-invalide", $"Code de paramètre invalide : '{command.Code}'.");
        }

        var parametre = await repository.GetAsync(code!, cancellationToken);
        if (parametre is null)
        {
            return Error.NotFound("parametre.inconnu", $"Paramètre légal '{command.Code}' inconnu.");
        }

        try
        {
            parametre.DefinirValeur(command.Valeur, command.ValideDu);
        }
        catch (DomainException ex)
        {
            return Error.Validation("parametre.valeur-invalide", ex.Message);
        }

        outbox.Add(new ParametreLegalModifie(parametre.Code.Value, command.Valeur, parametre.Unite.ToString(), command.ValideDu, null));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
