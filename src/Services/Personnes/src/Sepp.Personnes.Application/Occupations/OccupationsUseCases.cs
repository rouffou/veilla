using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Personnes.Application.Personnes;
using Sepp.Personnes.Domain.Personnes;

namespace Sepp.Personnes.Application.Occupations;

public sealed record ListerOccupations(Guid PersonneId);

public sealed class ListerOccupationsHandler(AccesPersonnes acces, PerimetreUtilisateur perimetre)
    : IQueryHandler<ListerOccupations, IReadOnlyList<OccupationDto>>
{
    public async Task<Result<IReadOnlyList<OccupationDto>>> HandleAsync(ListerOccupations query, CancellationToken cancellationToken)
    {
        var personne = await acces.ChargerAsync(query.PersonneId, Permissions.PersonneLire, cancellationToken);
        return personne.IsSuccess
            ? perimetre.OccupationsVisibles(personne.Value).Select(o => o.Dto()).ToList()
            : personne.Error!;
    }
}

/// <summary>AFF-20, AFF-21, AFF-23 : ajoute une occupation (entrée) à une personne connue.</summary>
public sealed record DebuterOccupation(Guid PersonneId, NouvelleOccupationDto Occupation);

public sealed class DebuterOccupationHandler(AccesPersonnes acces, PerimetreUtilisateur perimetre, IUnitOfWork unitOfWork, IIntegrationEventOutbox outbox)
    : ICommandHandler<DebuterOccupation, Guid>
{
    public async Task<Result<Guid>> HandleAsync(DebuterOccupation command, CancellationToken cancellationToken)
    {
        if (!perimetre.PeutAgirPour(command.Occupation.AffilieId))
        {
            return Error.Forbidden("personnes.hors-perimetre", "Un employeur n'enregistre que les occupations de son affilié.");
        }

        // Un employeur ne rattache par identifiant qu'une personne qu'il voit déjà ; sinon il passe par la saisie avec NISS.
        var personne = await acces.ChargerAsync(command.PersonneId, Permissions.PersonneEcrire, cancellationToken);
        if (!personne.IsSuccess)
        {
            return personne.Error!;
        }

        var occupation = Regles.Appliquer("occupation.invalide", () => personne.Value.DebuterOccupation(command.Occupation.ToOccupation()));
        if (!occupation.IsSuccess)
        {
            return occupation.Error!;
        }

        EvenementsIntegration.Publier(personne.Value, outbox);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return occupation.Value.Id;
    }
}

/// <summary>AFF-20 : sortie (dernier jour d'occupation inclus). Les affectations en cours sont clôturées.</summary>
public sealed record TerminerOccupation(Guid PersonneId, Guid OccupationId, DateOnly DateFin);

public sealed class TerminerOccupationHandler(AccesPersonnes acces, PerimetreUtilisateur perimetre, IUnitOfWork unitOfWork, IIntegrationEventOutbox outbox)
    : ICommandHandler<TerminerOccupation, Unit>
{
    public async Task<Result<Unit>> HandleAsync(TerminerOccupation command, CancellationToken cancellationToken)
    {
        var personne = await acces.ChargerAsync(command.PersonneId, Permissions.PersonneEcrire, cancellationToken);
        if (!personne.IsSuccess)
        {
            return personne.Error!;
        }

        var occupation = personne.Value.Occupations.SingleOrDefault(o => o.Id == command.OccupationId);
        if (occupation is null || !perimetre.PeutAgirPour(occupation.AffilieId))
        {
            return Error.NotFound("occupation.inconnue", "Occupation inconnue.");
        }

        var resultat = Regles.Appliquer("occupation.invalide", () => personne.Value.TerminerOccupation(command.OccupationId, command.DateFin));
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        if (resultat.Value)
        {
            EvenementsIntegration.Publier(personne.Value, outbox);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}

/// <summary>
/// AFF-20 : entrée déclarée par DIMONA, transmise par le service Intégrations (connexion BCSS hors périmètre).
/// Point d'entrée idempotent : la référence DIMONA est unique ; un rejeu renvoie l'occupation déjà enregistrée.
/// Le NISS voyage dans le corps de l'appel interne, jamais dans un événement (DAT-06).
/// </summary>
public sealed record EnregistrerEntreeDimona(
    string ReferenceDimona,
    string Niss,
    IdentiteDto Identite,
    Guid AffilieId,
    Guid? AffilieUtilisateurId,
    TypeTravailleur TypeTravailleur,
    TypeContrat TypeContrat,
    DateOnly DateDebut,
    DateOnly? DateFin)
{
    public override string ToString() => $"EnregistrerEntreeDimona {{ ReferenceDimona = {ReferenceDimona}, Niss = masqué }}";
}

public sealed record DimonaEnregistreeDto(Guid PersonneId, Guid OccupationId, bool DejaEnregistree);

public sealed class EnregistrerEntreeDimonaHandler(
    IPersonneRepository repository,
    EnregistrementTravailleurs enregistrement,
    INissIndex index,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    ICurrentUser currentUser,
    PerimetreUtilisateur perimetre) : ICommandHandler<EnregistrerEntreeDimona, DimonaEnregistreeDto>
{
    public async Task<Result<DimonaEnregistreeDto>> HandleAsync(EnregistrerEntreeDimona command, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.PersonneEcrire) || perimetre.EstExterne)
        {
            return Error.Forbidden("personnes.interdit", "L'alimentation DIMONA est réservée au SEPP (service Intégrations).");
        }

        if (!Niss.TryParse(command.Niss, out var niss, out var erreur))
        {
            return Error.Validation("personne.niss-invalide", erreur);
        }

        var reference = command.ReferenceDimona?.Trim().ToUpperInvariant() ?? string.Empty;
        if (reference.Length == 0)
        {
            return Error.Validation("occupation.dimona-invalide", "La référence DIMONA est obligatoire.");
        }

        var dejaConnue = await repository.GetParReferenceDimonaAsync(reference, cancellationToken);
        if (dejaConnue is not null)
        {
            if (dejaConnue.NissHash != index.Calculer(niss))
            {
                return Error.Conflict("occupation.dimona-conflit", $"La référence DIMONA {reference} est déjà enregistrée pour une autre personne.");
            }

            var existante = dejaConnue.Occupations.Single(o => o.ReferenceDimona == reference);
            return new DimonaEnregistreeDto(dejaConnue.Id, existante.Id, true);
        }

        var trouvee = await enregistrement.TrouverOuCreerAsync(niss, command.Identite.ToIdentite(), null, identiteDeReference: true, cancellationToken);
        if (!trouvee.IsSuccess)
        {
            return trouvee.Error!;
        }

        var (personne, nouvelle) = trouvee.Value;
        var occupation = Regles.Appliquer("occupation.invalide", () => personne.DebuterOccupation(new NouvelleOccupation(
            command.AffilieId, command.AffilieUtilisateurId, command.TypeTravailleur, command.TypeContrat, command.DateDebut, command.DateFin, reference)));
        if (!occupation.IsSuccess)
        {
            return occupation.Error!;
        }

        if (nouvelle)
        {
            repository.Add(personne);
        }

        EvenementsIntegration.Publier(personne, outbox);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new DimonaEnregistreeDto(personne.Id, occupation.Value.Id, false);
    }
}

/// <summary>AFF-20 : sortie déclarée par DIMONA ; idempotent (même date de fin → aucun effet).</summary>
public sealed record EnregistrerSortieDimona(string ReferenceDimona, DateOnly DateFin);

public sealed class EnregistrerSortieDimonaHandler(
    IPersonneRepository repository,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    ICurrentUser currentUser,
    PerimetreUtilisateur perimetre) : ICommandHandler<EnregistrerSortieDimona, Unit>
{
    public async Task<Result<Unit>> HandleAsync(EnregistrerSortieDimona command, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.PersonneEcrire) || perimetre.EstExterne)
        {
            return Error.Forbidden("personnes.interdit", "L'alimentation DIMONA est réservée au SEPP (service Intégrations).");
        }

        var reference = command.ReferenceDimona.Trim().ToUpperInvariant();
        var personne = await repository.GetParReferenceDimonaAsync(reference, cancellationToken);
        if (personne is null)
        {
            return Error.NotFound("occupation.inconnue", $"Aucune occupation pour la référence DIMONA {reference}.");
        }

        var occupation = personne.Occupations.Single(o => o.ReferenceDimona == reference);
        var resultat = Regles.Appliquer("occupation.invalide", () => personne.TerminerOccupation(occupation.Id, command.DateFin));
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        if (resultat.Value)
        {
            EvenementsIntegration.Publier(personne, outbox);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
