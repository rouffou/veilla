using Sepp.Affilies.Application.Affilies;
using Sepp.Affilies.Application.Securite;
using Sepp.Affilies.Domain.Affilies;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;

namespace Sepp.Affilies.Application.Hierarchie;

// AFF-02 — Hiérarchie : unités d'établissement, sites, départements. Réservée au gestionnaire de dossiers.

public sealed record AjouterUniteEtablissement(Guid AffilieId, string Numero, string Nom, AdresseDto Adresse, Language Langue, DateOnly Depuis);

public sealed class AjouterUniteEtablissementHandler(ModificateurAffilie modificateur, IAffilieRepository affilies)
    : ICommandHandler<AjouterUniteEtablissement, Guid>
{
    public Task<Result<Guid>> HandleAsync(AjouterUniteEtablissement command, CancellationToken cancellationToken) =>
        modificateur.ModifierEtVerifierAsync(command.AffilieId, PartieFiche.Fiche, "unite-etablissement.ajoutee", async affilie =>
        {
            var numero = new NumeroUniteEtablissement(command.Numero);
            if (affilie.UnitesEtablissement.All(u => u.Numero != numero) && await affilies.UniteEtablissementUtiliseeAsync(numero, cancellationToken))
            {
                return Error.Conflict("unite-etablissement.existe", $"L'unité d'établissement {numero} est rattachée à un autre affilié.");
            }

            return Result<Guid>.Success(affilie.AjouterUniteEtablissement(numero, command.Nom, command.Adresse.ToAdresse(), command.Langue, command.Depuis).Id);
        }, cancellationToken);
}

public sealed record ModifierUniteEtablissement(Guid AffilieId, Guid UniteId, string Nom, AdresseDto Adresse, Language Langue);

public sealed class ModifierUniteEtablissementHandler(ModificateurAffilie modificateur) : ICommandHandler<ModifierUniteEtablissement, Unit>
{
    public Task<Result<Unit>> HandleAsync(ModifierUniteEtablissement command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Fiche, "unite-etablissement.modifiee", affilie =>
        {
            affilie.ModifierUniteEtablissement(command.UniteId, command.Nom, command.Adresse.ToAdresse(), command.Langue);
            return Unit.Value;
        }, cancellationToken);
}

public sealed record FermerUniteEtablissement(Guid AffilieId, Guid UniteId, DateOnly Fin);

public sealed class FermerUniteEtablissementHandler(ModificateurAffilie modificateur) : ICommandHandler<FermerUniteEtablissement, Unit>
{
    public Task<Result<Unit>> HandleAsync(FermerUniteEtablissement command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Fiche, "unite-etablissement.fermee", affilie =>
        {
            affilie.FermerUniteEtablissement(command.UniteId, command.Fin);
            return Unit.Value;
        }, cancellationToken);
}

public sealed record AjouterSite(Guid AffilieId, Guid UniteId, string Nom, AdresseDto Adresse, double? Latitude, double? Longitude, DateOnly Depuis);

public sealed class AjouterSiteHandler(ModificateurAffilie modificateur) : ICommandHandler<AjouterSite, Guid>
{
    public Task<Result<Guid>> HandleAsync(AjouterSite command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Fiche, "site.ajoute", affilie =>
            affilie.AjouterSite(command.UniteId, command.Nom, command.Adresse.ToAdresse(), command.Latitude, command.Longitude, command.Depuis).Id,
            cancellationToken);
}

public sealed record ModifierSite(Guid AffilieId, Guid SiteId, string Nom, AdresseDto Adresse, double? Latitude, double? Longitude);

public sealed class ModifierSiteHandler(ModificateurAffilie modificateur) : ICommandHandler<ModifierSite, Unit>
{
    public Task<Result<Unit>> HandleAsync(ModifierSite command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Fiche, "site.modifie", affilie =>
        {
            affilie.ModifierSite(command.SiteId, command.Nom, command.Adresse.ToAdresse(), command.Latitude, command.Longitude);
            return Unit.Value;
        }, cancellationToken);
}

public sealed record FermerSite(Guid AffilieId, Guid SiteId, DateOnly Fin);

public sealed class FermerSiteHandler(ModificateurAffilie modificateur) : ICommandHandler<FermerSite, Unit>
{
    public Task<Result<Unit>> HandleAsync(FermerSite command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Fiche, "site.ferme", affilie =>
        {
            affilie.FermerSite(command.SiteId, command.Fin);
            return Unit.Value;
        }, cancellationToken);
}

public sealed record AjouterDepartement(Guid AffilieId, Guid SiteId, string Nom, Guid? ParentId, DateOnly Depuis);

public sealed class AjouterDepartementHandler(ModificateurAffilie modificateur) : ICommandHandler<AjouterDepartement, Guid>
{
    public Task<Result<Guid>> HandleAsync(AjouterDepartement command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Fiche, "departement.ajoute", affilie =>
            affilie.AjouterDepartement(command.SiteId, command.Nom, command.ParentId, command.Depuis).Id, cancellationToken);
}

public sealed record ModifierDepartement(Guid AffilieId, Guid DepartementId, string Nom, Guid? ParentId);

public sealed class ModifierDepartementHandler(ModificateurAffilie modificateur) : ICommandHandler<ModifierDepartement, Unit>
{
    public Task<Result<Unit>> HandleAsync(ModifierDepartement command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Fiche, "departement.modifie", affilie =>
        {
            affilie.ModifierDepartement(command.DepartementId, command.Nom, command.ParentId);
            return Unit.Value;
        }, cancellationToken);
}

public sealed record FermerDepartement(Guid AffilieId, Guid DepartementId, DateOnly Fin);

public sealed class FermerDepartementHandler(ModificateurAffilie modificateur) : ICommandHandler<FermerDepartement, Unit>
{
    public Task<Result<Unit>> HandleAsync(FermerDepartement command, CancellationToken cancellationToken) =>
        modificateur.ModifierAsync(command.AffilieId, PartieFiche.Fiche, "departement.ferme", affilie =>
        {
            affilie.FermerDepartement(command.DepartementId, command.Fin);
            return Unit.Value;
        }, cancellationToken);
}
