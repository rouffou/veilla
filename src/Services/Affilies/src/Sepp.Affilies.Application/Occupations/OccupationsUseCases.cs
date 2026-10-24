using Sepp.Affilies.Domain.Affilies;
using Sepp.Affilies.Domain.Occupations;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Personnes;

namespace Sepp.Affilies.Application.Occupations;

/// <summary>
/// POR-11, #340 : employeur du travailleur connecté. Dénomination et langue seulement (aucune autre donnée de la fiche).
/// <see cref="Langue"/> est la langue de correspondance de l'affilié, <see cref="RegimeLinguistique"/> son régime linguistique (NF-41).
/// </summary>
public sealed record MonEmployeurDto(Guid AffilieId, string Denomination, Language Langue, RegimeLinguistique RegimeLinguistique);

/// <summary>
/// Employeurs de la personne du jeton (claim <c>personne_id</c>) à une date : les affiliés chez qui elle a une occupation
/// active. La personne n'est jamais lue dans la requête et aucune liste d'affiliés n'est acceptée de l'appelant.
/// </summary>
public sealed record ListerMesEmployeurs(DateOnly Date);

public sealed class ListerMesEmployeursHandler(
    IOccupationRepository occupations, IAffilieRepository affilies, ICurrentUser currentUser, IContextePersonne contexte)
    : IQueryHandler<ListerMesEmployeurs, IReadOnlyList<MonEmployeurDto>>
{
    public async Task<Result<IReadOnlyList<MonEmployeurDto>>> HandleAsync(ListerMesEmployeurs query, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.AffilieLireEmployeurs))
        {
            return Error.Forbidden("affilie.lecture-interdite", "Vous n'avez pas accès aux employeurs.");
        }

        if (contexte.PersonneId is not { } personneId)
        {
            return Error.Forbidden("perimetre.interdit", "Le jeton ne désigne aucune personne : accès refusé.");
        }

        var affilieIds = await occupations.AffiliesOccupesAsync(personneId, query.Date, cancellationToken);
        return Result<IReadOnlyList<MonEmployeurDto>>.Success(await affilies.EmployeursAsync(affilieIds, cancellationToken));
    }
}

/// <summary>Occupation commencée (<c>personnes.occupation-debutee.v1</c>), idempotente (inbox, ARC-31).</summary>
public sealed class OccupationDebuteeHandler(IOccupationRepository occupations, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<OccupationDebutee>
{
    public async Task HandleAsync(OccupationDebutee integrationEvent, CancellationToken cancellationToken)
    {
        var occupation = await occupations.GetAsync(integrationEvent.OccupationId, cancellationToken);
        if (occupation is null)
        {
            occupation = new OccupationLocale(integrationEvent.OccupationId, integrationEvent.PersonneId, integrationEvent.AffilieId);
            occupations.Add(occupation);
        }

        occupation.Debuter(integrationEvent.DateDebut);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Occupation terminée (<c>personnes.occupation-terminee.v1</c>) ; la fin peut arriver avant le début.</summary>
public sealed class OccupationTermineeHandler(IOccupationRepository occupations, IUnitOfWork unitOfWork)
    : IIntegrationEventHandler<OccupationTerminee>
{
    public async Task HandleAsync(OccupationTerminee integrationEvent, CancellationToken cancellationToken)
    {
        var occupation = await occupations.GetAsync(integrationEvent.OccupationId, cancellationToken);
        if (occupation is null)
        {
            occupation = new OccupationLocale(integrationEvent.OccupationId, integrationEvent.PersonneId, integrationEvent.AffilieId);
            occupations.Add(occupation);
        }

        if (occupation.Terminer(integrationEvent.DateFin, integrationEvent.OccurredAt))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
