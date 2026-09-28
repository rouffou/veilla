using Sepp.Affilies.Application.Affilies;
using Sepp.Affilies.Application.Securite;
using Sepp.Affilies.Domain.Affilies;
using Sepp.BuildingBlocks.Application;

namespace Sepp.Affilies.Application.Concertation;

// AFF-03 (contacts) et AFF-04 (organes de concertation) : écriture ouverte au gestionnaire de dossiers,
// ainsi qu'à l'employeur et au SIPP pour leur propre affilié (écriture partielle, §3.3).

public sealed record ContactSaisi(string Nom, string? Fonction, RoleContact Role, string? Email, string? Telephone)
{
    public DonneesContact ToDonnees() => new(Nom, Fonction, Role, Email, Telephone);
}

public sealed record AjouterContact(Guid AffilieId, ContactSaisi Contact, DateOnly ValideDu);

public sealed class AjouterContactHandler(ModificateurAffilie modificateur) : ICommandHandler<AjouterContact, Guid>
{
    public Task<Result<Guid>> HandleAsync(AjouterContact command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Concertation, "contact.ajoute", affilie =>
            affilie.AjouterContact(command.Contact.ToDonnees(), command.ValideDu).Id, cancellationToken);
}

/// <summary>Nouvelle version d'un contact à partir d'une date (DAT-04) ; renvoie l'identifiant de la nouvelle ligne.</summary>
public sealed record ModifierContact(Guid AffilieId, Guid ContactId, ContactSaisi Contact, DateOnly APartirDu);

public sealed class ModifierContactHandler(ModificateurAffilie modificateur) : ICommandHandler<ModifierContact, Guid>
{
    public Task<Result<Guid>> HandleAsync(ModifierContact command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Concertation, "contact.modifie", affilie =>
            affilie.ModifierContact(command.ContactId, command.Contact.ToDonnees(), command.APartirDu).Id, cancellationToken);
}

public sealed record TerminerContact(Guid AffilieId, Guid ContactId, DateOnly Fin);

public sealed class TerminerContactHandler(ModificateurAffilie modificateur) : ICommandHandler<TerminerContact, Unit>
{
    public Task<Result<Unit>> HandleAsync(TerminerContact command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Concertation, "contact.termine", affilie =>
        {
            affilie.TerminerContact(command.ContactId, command.Fin);
            return Unit.Value;
        }, cancellationToken);
}

public sealed record InstallerOrgane(Guid AffilieId, TypeOrgane Type, DateOnly Depuis);

public sealed class InstallerOrganeHandler(ModificateurAffilie modificateur) : ICommandHandler<InstallerOrgane, Guid>
{
    public Task<Result<Guid>> HandleAsync(InstallerOrgane command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Concertation, "organe.installe", affilie =>
            affilie.InstallerOrgane(command.Type, command.Depuis).Id, cancellationToken);
}

public sealed record DissoudreOrgane(Guid AffilieId, Guid OrganeId, DateOnly Fin);

public sealed class DissoudreOrganeHandler(ModificateurAffilie modificateur) : ICommandHandler<DissoudreOrgane, Unit>
{
    public Task<Result<Unit>> HandleAsync(DissoudreOrgane command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Concertation, "organe.dissous", affilie =>
        {
            affilie.DissoudreOrgane(command.OrganeId, command.Fin);
            return Unit.Value;
        }, cancellationToken);
}

/// <summary>Réunion d'un organe ; l'ordre du jour est un document du service Documents (référence par identifiant).</summary>
public sealed record PlanifierReunion(Guid AffilieId, Guid OrganeId, DateOnly DateReunion, Guid? OrdreDuJourDocumentId, bool ParticipationSepp);

public sealed class PlanifierReunionHandler(ModificateurAffilie modificateur) : ICommandHandler<PlanifierReunion, Guid>
{
    public Task<Result<Guid>> HandleAsync(PlanifierReunion command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Concertation, "reunion.planifiee", affilie =>
            affilie.PlanifierReunion(command.OrganeId, command.DateReunion, command.OrdreDuJourDocumentId, command.ParticipationSepp).Id,
            cancellationToken);
}

public sealed record ModifierReunion(Guid AffilieId, Guid OrganeId, Guid ReunionId, DateOnly DateReunion, Guid? OrdreDuJourDocumentId, bool ParticipationSepp);

public sealed class ModifierReunionHandler(ModificateurAffilie modificateur) : ICommandHandler<ModifierReunion, Unit>
{
    public Task<Result<Unit>> HandleAsync(ModifierReunion command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Concertation, "reunion.modifiee", affilie =>
        {
            affilie.ModifierReunion(command.OrganeId, command.ReunionId, command.DateReunion, command.OrdreDuJourDocumentId, command.ParticipationSepp);
            return Unit.Value;
        }, cancellationToken);
}
