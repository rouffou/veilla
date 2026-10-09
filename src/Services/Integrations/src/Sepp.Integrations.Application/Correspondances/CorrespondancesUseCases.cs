using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Affilies;
using Sepp.Integrations.Domain;
using Sepp.Integrations.Domain.Correspondances;

namespace Sepp.Integrations.Application.Correspondances;

public sealed record CorrespondanceDto(Guid Id, TypeIdentifiantExterne TypeExterne, string ValeurExterne, TypeObjetInterne TypeInterne, Guid IdentifiantInterne)
{
    public static CorrespondanceDto De(CorrespondanceIdentifiant c) => new(c.Id, c.TypeExterne, c.ValeurExterne, c.TypeInterne, c.IdentifiantInterne);
}

/// <summary>Enregistre ou met à jour une correspondance (clé : type et valeur externes) ; idempotent.</summary>
public sealed class RegistreCorrespondances(ICorrespondanceRepository repository)
{
    public async Task<CorrespondanceIdentifiant> DefinirAsync(TypeIdentifiantExterne type, string valeur, TypeObjetInterne typeInterne, Guid identifiant, CancellationToken cancellationToken)
    {
        var existante = await repository.GetAsync(type, CorrespondanceIdentifiant.Normaliser(type, valeur), cancellationToken);
        if (existante is null)
        {
            existante = CorrespondanceIdentifiant.Creer(type, valeur, typeInterne, identifiant);
            repository.Add(existante);
        }
        else
        {
            existante.Rattacher(identifiant);
        }

        return existante;
    }
}

/// <summary>§15.3 : consultation des correspondances d'identifiants (par type externe ou par objet interne).</summary>
public sealed record ListerCorrespondances(TypeIdentifiantExterne? TypeExterne, string? ValeurExterne, Guid? IdentifiantInterne);

public sealed class ListerCorrespondancesHandler(ICorrespondanceRepository repository, ICurrentUser utilisateur)
    : IQueryHandler<ListerCorrespondances, IReadOnlyList<CorrespondanceDto>>
{
    public async Task<Result<IReadOnlyList<CorrespondanceDto>>> HandleAsync(ListerCorrespondances query, CancellationToken cancellationToken)
    {
        if (!utilisateur.HasPermission(Permissions.IntegrationsAdministrer))
        {
            return Error.Forbidden("integrations.interdit", "Consultation réservée au gestionnaire et à l'administrateur fonctionnel.");
        }

        if (!string.IsNullOrWhiteSpace(query.ValeurExterne))
        {
            if (query.TypeExterne is not { } type)
            {
                return Error.Validation("correspondance.type-requis", "Le type d'identifiant externe est requis pour une recherche par valeur.");
            }

            string valeur;
            try
            {
                valeur = CorrespondanceIdentifiant.Normaliser(type, query.ValeurExterne);
            }
            catch (DomainException ex)
            {
                return Error.Validation("correspondance.valeur-invalide", ex.Message);
            }

            var trouvee = await repository.GetAsync(type, valeur, cancellationToken);
            return trouvee is null ? [] : new[] { CorrespondanceDto.De(trouvee) };
        }

        var liste = await repository.ListerAsync(query.TypeExterne, query.IdentifiantInterne, 1000, cancellationToken);
        return liste.Select(CorrespondanceDto.De).ToList();
    }
}

/// <summary>Saisie manuelle d'une correspondance (reprise de données, affilié antérieur à l'abonnement aux événements).</summary>
public sealed record DefinirCorrespondance(TypeIdentifiantExterne TypeExterne, string ValeurExterne, TypeObjetInterne TypeInterne, Guid IdentifiantInterne);

public sealed class DefinirCorrespondanceHandler(RegistreCorrespondances correspondances, IUnitOfWork unitOfWork, ICurrentUser utilisateur)
    : ICommandHandler<DefinirCorrespondance, CorrespondanceDto>
{
    public async Task<Result<CorrespondanceDto>> HandleAsync(DefinirCorrespondance command, CancellationToken cancellationToken)
    {
        if (!utilisateur.HasPermission(Permissions.IntegrationsAdministrer))
        {
            return Error.Forbidden("integrations.interdit", "Saisie réservée au gestionnaire et à l'administrateur fonctionnel.");
        }

        try
        {
            var correspondance = await correspondances.DefinirAsync(command.TypeExterne, command.ValeurExterne, command.TypeInterne, command.IdentifiantInterne, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return CorrespondanceDto.De(correspondance);
        }
        catch (DomainException ex)
        {
            return Error.Validation("correspondance.invalide", ex.Message);
        }
    }
}

/// <summary>Numéro BCE ↔ affilié, appris de la création d'un affilié (§15.3) ; idempotent (clé : numéro BCE).</summary>
public sealed class AffilieCreeHandler(RegistreCorrespondances correspondances, IUnitOfWork unitOfWork) : IIntegrationEventHandler<AffilieCree>
{
    public async Task HandleAsync(AffilieCree integrationEvent, CancellationToken cancellationToken)
    {
        await correspondances.DefinirAsync(TypeIdentifiantExterne.NumeroBce, integrationEvent.NumeroBce, TypeObjetInterne.Affilie, integrationEvent.AffilieId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Numéro BCE ↔ affilié, tenu à jour à chaque modification de la fiche.</summary>
public sealed class AffilieModifieHandler(RegistreCorrespondances correspondances, IUnitOfWork unitOfWork) : IIntegrationEventHandler<AffilieModifie>
{
    public async Task HandleAsync(AffilieModifie integrationEvent, CancellationToken cancellationToken)
    {
        await correspondances.DefinirAsync(TypeIdentifiantExterne.NumeroBce, integrationEvent.NumeroBce, TypeObjetInterne.Affilie, integrationEvent.AffilieId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Lecture des dernières données BCE d'une entreprise (adresses des unités d'établissement comprises).</summary>
public sealed record ObtenirEntrepriseBce(string NumeroBce);

public sealed record AdresseBceDto(string Rue, string Numero, string? Boite, string CodePostal, string Localite, string CodePays);

public sealed record UniteEtablissementBceDto(string Numero, string Denomination, AdresseBceDto Adresse, DateOnly? DateDebut);

public sealed record EntrepriseBceDto(
    string NumeroBce,
    Guid? AffilieId,
    string Denomination,
    string FormeJuridique,
    string CodeNace,
    DateOnly DateExtraction,
    DateTimeOffset ActualiseeLe,
    IReadOnlyList<UniteEtablissementBceDto> UnitesEtablissement);

public sealed class ObtenirEntrepriseBceHandler(IEntrepriseBceRepository entreprises, ICorrespondanceRepository correspondances, ICurrentUser utilisateur)
    : IQueryHandler<ObtenirEntrepriseBce, EntrepriseBceDto>
{
    public async Task<Result<EntrepriseBceDto>> HandleAsync(ObtenirEntrepriseBce query, CancellationToken cancellationToken)
    {
        // Données d'entreprise publiques : lisibles par les profils qui consultent les affiliés et par le compte technique
        // du service Affiliés (consommateur de donnees-bce-recues, permission dédiée sans accès aux fiches).
        if (!utilisateur.HasPermission(Permissions.AffilieLire) && !utilisateur.HasPermission(Permissions.IntegrationsAdministrer)
            && !utilisateur.HasPermission(Permissions.IntegrationsBceLire))
        {
            return Error.Forbidden("integrations.interdit", "Droits insuffisants.");
        }

        if (!NumerosBce.EstEntrepriseValide(query.NumeroBce, out var numero))
        {
            return Error.Validation("bce.numero-invalide", $"Numéro d'entreprise BCE invalide : '{query.NumeroBce}'.");
        }

        var entreprise = await entreprises.GetParNumeroAsync(numero, cancellationToken);
        if (entreprise is null)
        {
            return Error.NotFound("bce.entreprise-non-recue", "Aucune donnée BCE reçue pour cette entreprise.");
        }

        var affilie = await correspondances.GetAsync(TypeIdentifiantExterne.NumeroBce, numero, cancellationToken);
        return new EntrepriseBceDto(entreprise.NumeroBce, affilie?.IdentifiantInterne, entreprise.Denomination, entreprise.FormeJuridique,
            entreprise.CodeNace, entreprise.DateExtraction, entreprise.ActualiseeLe,
            entreprise.UnitesEtablissement.OrderBy(u => u.Numero, StringComparer.Ordinal).Select(u => new UniteEtablissementBceDto(
                u.Numero, u.Denomination,
                new AdresseBceDto(u.Adresse.Rue, u.Adresse.Numero, u.Adresse.Boite, u.Adresse.CodePostal, u.Adresse.Localite, u.Adresse.CodePays),
                u.DateDebut)).ToList());
    }
}
