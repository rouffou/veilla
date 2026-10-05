using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.PostesRisques;
using Sepp.PostesRisques.Domain.Risques;

namespace Sepp.PostesRisques.Application.Risques;

public sealed record RegleDto(
    int Version,
    TypeSurveillance TypeSurveillance,
    int? FrequenceMois,
    IReadOnlyList<string> ActesSupplementaires,
    IReadOnlyList<string> Vaccins,
    bool SurveillanceProlongee,
    DateOnly ValideDu,
    DateOnly? ValideJusquAu)
{
    public static RegleDto From(RegleSurveillance r) =>
        new(r.Version, r.TypeSurveillance, r.FrequenceMois, r.ActesSupplementaires, r.Vaccins, r.SurveillanceProlongee, r.Validite.ValidFrom, r.Validite.ValidTo);
}

public sealed record RisqueDto(
    Guid Id,
    string Code,
    CategorieRisque Categorie,
    string Libelle,
    string ReferenceLegale,
    RegleDto? RegleApplicable,
    IReadOnlyList<RegleDto> Historique)
{
    public static RisqueDto From(Risque r, DateOnly date, Language langue, bool avecHistorique) =>
        new(r.Id, r.Code, r.Categorie, r.Libelle.In(langue), r.ReferenceLegale,
            r.RegleAu(date) is { } regle ? RegleDto.From(regle) : null,
            avecHistorique ? r.Regles.OrderBy(x => x.Version).Select(RegleDto.From).ToList() : []);
}

public sealed record LibellesDto(string Fr, string Nl, string De, string? En)
{
    public LocalizedLabel ToLabel() => new(Fr, Nl, De, En);
}

public sealed record RegleSaisie(
    TypeSurveillance TypeSurveillance,
    int? FrequenceMois,
    IReadOnlyList<string>? ActesSupplementaires,
    IReadOnlyList<string>? Vaccins,
    bool SurveillanceProlongee,
    DateOnly ValideDu);

/// <summary>AFF-11 : référentiel des risques à une date, dans une langue, filtrable par catégorie.</summary>
public sealed record ListerRisques(CategorieRisque? Categorie, DateOnly Date, Language Langue);

public sealed class ListerRisquesHandler(IRisqueRepository repository) : IQueryHandler<ListerRisques, IReadOnlyList<RisqueDto>>
{
    public async Task<Result<IReadOnlyList<RisqueDto>>> HandleAsync(ListerRisques query, CancellationToken cancellationToken)
    {
        var risques = await repository.ListAsync(cancellationToken);
        return risques
            .Where(r => query.Categorie is null || r.Categorie == query.Categorie)
            .OrderBy(r => r.Code, StringComparer.Ordinal)
            .Select(r => RisqueDto.From(r, query.Date, query.Langue, avecHistorique: false))
            .ToList();
    }
}

public sealed record ObtenirRisque(string Code, DateOnly Date, Language Langue);

public sealed class ObtenirRisqueHandler(IRisqueRepository repository) : IQueryHandler<ObtenirRisque, RisqueDto>
{
    public async Task<Result<RisqueDto>> HandleAsync(ObtenirRisque query, CancellationToken cancellationToken)
    {
        var risque = await repository.GetByCodeAsync(query.Code.Trim().ToUpperInvariant(), cancellationToken);
        return risque is null
            ? Error.NotFound("risque.inconnu", $"Risque '{query.Code}' inconnu.")
            : RisqueDto.From(risque, query.Date, query.Langue, avecHistorique: true);
    }
}

public sealed record CreerRisque(string Code, CategorieRisque Categorie, LibellesDto Libelle, string ReferenceLegale, RegleSaisie Regle);

public sealed class CreerRisqueHandler(
    IRisqueRepository repository,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    ICurrentUser currentUser) : ICommandHandler<CreerRisque, Guid>
{
    public async Task<Result<Guid>> HandleAsync(CreerRisque command, CancellationToken cancellationToken)
    {
        if (Acces.Permission(currentUser, Permissions.ReferentielsAdministrer, "Seul l'administrateur fonctionnel gère le référentiel des risques.") is { } refus)
        {
            return refus;
        }

        if (string.IsNullOrWhiteSpace(command.Code) || await repository.GetByCodeAsync(command.Code.Trim().ToUpperInvariant(), cancellationToken) is not null)
        {
            return Error.Conflict("risque.existe", $"Le risque '{command.Code}' existe déjà.");
        }

        try
        {
            var risque = Risque.Creer(command.Code, command.Categorie, command.Libelle.ToLabel(), command.ReferenceLegale);
            var regle = risque.DefinirRegle(command.Regle.TypeSurveillance, command.Regle.FrequenceMois, command.Regle.ActesSupplementaires ?? [],
                command.Regle.Vaccins ?? [], command.Regle.SurveillanceProlongee, command.Regle.ValideDu)!;
            repository.Add(risque);
            outbox.Add(Evenements.RegleModifiee(risque, regle));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return risque.Id;
        }
        catch (DomainException ex)
        {
            return Error.Validation("risque.invalide", ex.Message);
        }
    }
}

/// <summary>AFF-12 : nouvelle version de la règle de surveillance d'un risque à partir d'une date.</summary>
public sealed record DefinirRegleSurveillance(string Code, RegleSaisie Regle);

public sealed class DefinirRegleSurveillanceHandler(
    IRisqueRepository repository,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    ICurrentUser currentUser) : ICommandHandler<DefinirRegleSurveillance, int>
{
    public async Task<Result<int>> HandleAsync(DefinirRegleSurveillance command, CancellationToken cancellationToken)
    {
        if (Acces.Permission(currentUser, Permissions.ReferentielsAdministrer, "Seul l'administrateur fonctionnel gère les règles de surveillance.") is { } refus)
        {
            return refus;
        }

        var risque = await repository.GetByCodeAsync(command.Code.Trim().ToUpperInvariant(), cancellationToken);
        if (risque is null)
        {
            return Error.NotFound("risque.inconnu", $"Risque '{command.Code}' inconnu.");
        }

        RegleSurveillance? regle;
        try
        {
            var r = command.Regle;
            regle = risque.DefinirRegle(r.TypeSurveillance, r.FrequenceMois, r.ActesSupplementaires ?? [], r.Vaccins ?? [], r.SurveillanceProlongee, r.ValideDu);
        }
        catch (DomainException ex)
        {
            return Error.Validation("regle.invalide", ex.Message);
        }

        if (regle is null)
        {
            return Error.Conflict("regle.inchangee", $"La règle en vigueur du risque {risque.Code} est identique.");
        }

        outbox.Add(Evenements.RegleModifiee(risque, regle));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return regle.Version;
    }
}

internal static class Evenements
{
    public static RegleSurveillanceModifiee RegleModifiee(Risque risque, RegleSurveillance regle) =>
        new(risque.Id, risque.Code, risque.Categorie.ToString(), regle.Version, regle.TypeSurveillance.ToString(), regle.FrequenceMois,
            regle.SurveillanceProlongee, regle.Validite.ValidFrom);
}
