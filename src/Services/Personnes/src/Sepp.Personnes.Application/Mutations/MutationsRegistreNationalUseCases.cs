using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Personnes.Domain.Personnes;

namespace Sepp.Personnes.Application.Mutations;

/// <summary>Issue du traitement d'une mutation du registre national.</summary>
public enum StatutMutation
{
    /// <summary>La mutation a été appliquée et historisée.</summary>
    Appliquee,

    /// <summary>Rejeu : la mutation avait déjà été appliquée (idempotence sur la référence), rien n'a changé.</summary>
    DejaAppliquee,

    /// <summary>Le NISS ne correspond à aucune personne suivie par le SEPP : rien à mettre à jour.</summary>
    PersonneInconnue,
}

/// <summary>
/// AFF-20, AFF-22 : mutation du registre national (changement d'adresse, de nom, de prénom, de langue ou décès) transmise
/// par le service Intégrations (connexion BCSS hors périmètre). Le NISS voyage dans le corps de l'appel interne, jamais
/// dans un événement, une URL ni un journal (ARC-06, DAT-06). <see cref="ReferenceMutation"/> est la clé d'idempotence ;
/// seule la valeur propre au <see cref="Type"/> est lue (pour un décès, la date du décès est <see cref="DateEffet"/>).
/// </summary>
public sealed record EnregistrerMutationRegistreNational(
    string ReferenceMutation,
    string Niss,
    TypeMutationRegistreNational Type,
    DateOnly DateEffet,
    AdresseDto? Adresse,
    string? Nom,
    string? Prenom,
    Language? Langue)
{
    public override string ToString() => $"EnregistrerMutationRegistreNational {{ ReferenceMutation = {ReferenceMutation}, Type = {Type}, Niss = masqué }}";
}

public sealed record MutationEnregistreeDto(Guid? PersonneId, StatutMutation Statut);

public sealed class EnregistrerMutationRegistreNationalHandler(
    IPersonneRepository repository,
    INissIndex index,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    ICurrentUser currentUser,
    PerimetreUtilisateur perimetre) : ICommandHandler<EnregistrerMutationRegistreNational, MutationEnregistreeDto>
{
    public async Task<Result<MutationEnregistreeDto>> HandleAsync(EnregistrerMutationRegistreNational command, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.PersonneEcrire) || perimetre.EstExterne)
        {
            return Error.Forbidden("personnes.interdit", "Les mutations du registre national sont réservées au SEPP (service Intégrations).");
        }

        if (!Niss.TryParse(command.Niss, out var niss, out var erreur))
        {
            return Error.Validation("personne.niss-invalide", erreur);
        }

        var reference = command.ReferenceMutation?.Trim() ?? string.Empty;
        if (reference.Length is 0 or > 100)
        {
            return Error.Validation("mutation.reference-invalide", "La référence de la mutation est obligatoire (100 caractères maximum).");
        }

        var hash = index.Calculer(niss);
        var dejaConnue = await repository.GetParReferenceMutationAsync(reference, cancellationToken);
        if (dejaConnue is not null && dejaConnue.NissHash != hash)
        {
            return Error.Conflict("mutation.reference-conflit", $"La référence de mutation {reference} est déjà enregistrée pour une autre personne.");
        }

        var personne = dejaConnue ?? await repository.GetParNissHashAsync(hash, cancellationToken);
        if (personne is null)
        {
            // Mutation d'une personne que le SEPP ne suit pas (encore) : rien à mettre à jour. Une entrée DIMONA ultérieure
            // reprendra l'identité courante du registre national.
            return new MutationEnregistreeDto(null, StatutMutation.PersonneInconnue);
        }

        var applique = Regles.Appliquer("mutation.invalide", () => personne.AppliquerMutation(new DemandeMutationRegistreNational(
            reference, command.Type, command.DateEffet, command.Adresse?.ToAdresse(), command.Nom, command.Prenom, command.Langue)));
        if (!applique.IsSuccess)
        {
            return applique.Error!;
        }

        if (!applique.Value.Appliquee)
        {
            return new MutationEnregistreeDto(personne.Id, StatutMutation.DejaAppliquee);
        }

        // Un décès publie OccupationTerminee / AffectationModifiee (identifiants et dates seulement, ARC-06) : Obligations recalcule.
        EvenementsIntegration.Publier(personne, outbox);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new MutationEnregistreeDto(personne.Id, StatutMutation.Appliquee);
    }
}
