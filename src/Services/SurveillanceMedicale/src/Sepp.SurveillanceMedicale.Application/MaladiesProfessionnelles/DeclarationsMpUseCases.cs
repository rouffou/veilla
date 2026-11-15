using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.SurveillanceMedicale.Domain.Decisions;
using Sepp.SurveillanceMedicale.Domain.MaladiesProfessionnelles;

namespace Sepp.SurveillanceMedicale.Application.MaladiesProfessionnelles;

public sealed record DemandeInformationDto(Guid Id, DateOnly DateDemande, DateOnly? Echeance, string Objet, DateOnly? DateReponse, string? Reponse);

public sealed record DeclarationMpDto(
    Guid Id, Guid DossierId, Guid PersonneId, DateOnly Date, string AuteurId, StatutDeclarationMp Statut, ContenuDeclarationMp Contenu,
    string? ReferenceFedris, DateOnly? DateEnvoi, DateOnly? DateIssue, IReadOnlyList<DemandeInformationDto> DemandesInformation);

internal static class DeclarationsMp
{
    public static DeclarationMpDto Dto(this DeclarationMaladieProfessionnelle d) => new(
        d.Id, d.DossierId, d.PersonneId, d.Date, d.AuteurId, d.Statut, d.Contenu, d.ReferenceFedris, d.DateEnvoi, d.DateIssue,
        [.. d.DemandesInformation.Select(i => new DemandeInformationDto(i.Id, i.DateDemande, i.Echeance, i.Objet, i.DateReponse, i.Reponse))]);
}

/// <summary>
/// Chargement d'une déclaration avec contrôle d'accès au dossier (secret médical). Écriture : <c>decision:signer</c> pour la
/// déclaration elle-même (préparation, envoi, réponse à Fedris), <c>decision:ecrire</c> pour le suivi administratif (statut
/// Fedris, enregistrement d'une demande d'information), ouvert à l'infirmier (#401).
/// </summary>
public sealed class AccesDeclarationsMp(IDeclarationMpRepository declarations, GardeDossier garde, ICurrentUser user)
{
    public const string MessageDeclarer = "La déclaration de maladie professionnelle est établie par le CPMT.";

    public Task<Result<DeclarationMaladieProfessionnelle>> LireAsync(Guid id, CancellationToken cancellationToken) =>
        ChargerAsync(id, ActionAudit.Lecture, null, null, cancellationToken);

    /// <summary>Acte du CPMT sur la déclaration elle-même (<c>decision:signer</c>) : envoi, réponse à Fedris.</summary>
    public Task<Result<DeclarationMaladieProfessionnelle>> DeclarerAsync(Guid id, ActionAudit action, CancellationToken cancellationToken) =>
        ChargerAsync(id, action, Permissions.DecisionSigner, MessageDeclarer, cancellationToken);

    /// <summary>Suivi administratif (<c>decision:ecrire</c>) : statut Fedris, demande d'information reçue.</summary>
    public Task<Result<DeclarationMaladieProfessionnelle>> SuivreAsync(Guid id, CancellationToken cancellationToken) =>
        ChargerAsync(id, ActionAudit.Modification, Permissions.DecisionEcrire,
            "Seuls les CPMT et l'infirmier suivent la déclaration de maladie professionnelle.", cancellationToken);

    private async Task<Result<DeclarationMaladieProfessionnelle>> ChargerAsync(
        Guid id, ActionAudit action, string? permission, string? message, CancellationToken cancellationToken)
    {
        if (permission is not null && Regles.Permission(user, permission, message!) is { } interdit)
        {
            return interdit;
        }

        if (garde.VerifierPermission(action) is { } refuse)
        {
            return refuse;
        }

        var declaration = await declarations.GetAsync(id, cancellationToken);
        if (declaration is null)
        {
            return Error.NotFound("declaration-mp.inconnue", "Déclaration de maladie professionnelle inconnue.");
        }

        var acces = action == ActionAudit.Lecture
            ? await garde.LireAsync(declaration.DossierId, PartiesDossier.MaladiesProfessionnelles, cancellationToken)
            : await garde.EcrireAsync(declaration.DossierId, PartiesDossier.MaladiesProfessionnelles, action, cancellationToken);
        return acces.IsSuccess ? declaration : acces.Error!;
    }
}

/// <summary>
/// SAN-70 : déclaration préremplie depuis le dossier : expositions, employeurs (affiliés des examens) et dernière
/// décision ; le CPMT complète le code de la maladie (liste belge) et la description clinique.
/// </summary>
public sealed record PreparerDeclarationMp(Guid DossierId, string CodeMaladie, string Description, string? AgentCausal)
{
    public override string ToString() => $"PreparerDeclarationMp {{ DossierId = {DossierId} }}";
}

public sealed class PreparerDeclarationMpHandler(
    GardeDossier garde, IExamenRepository examens, IDecisionRepository decisions, IDeclarationMpRepository declarations, ICurrentUser user,
    IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<PreparerDeclarationMp, DeclarationMpDto>
{
    public async Task<Result<DeclarationMpDto>> HandleAsync(PreparerDeclarationMp command, CancellationToken cancellationToken)
    {
        if (Regles.Permission(user, Permissions.DecisionSigner, AccesDeclarationsMp.MessageDeclarer) is { } interdit)
        {
            return interdit;
        }

        var acces = await garde.EcrireAsync(command.DossierId, PartiesDossier.MaladiesProfessionnelles, ActionAudit.Creation, cancellationToken);
        if (!acces.IsSuccess)
        {
            return acces.Error!;
        }

        var dossier = acces.Value;
        var affilies = (await examens.ListerParDossierAsync(dossier.Id, cancellationToken)).Select(e => e.AffilieId).Distinct().ToList();
        var derniere = (await decisions.ListerParDossierAsync(dossier.Id, cancellationToken))
            .Where(d => d.Statut == StatutDecision.Emise).MaxBy(d => d.DateExamen);
        var contenu = new ContenuDeclarationMp(
            command.CodeMaladie, command.Description, command.AgentCausal,
            [.. dossier.Expositions.OrderBy(e => e.PeriodeDebut).Select(e => new ExpositionDeclaree(e.Agent, e.Niveau, e.PeriodeDebut, e.PeriodeFin))],
            affilies,
            derniere is null ? null : CodesDecision.Code(derniere.Categorie));

        var declaration = Regles.Appliquer("declaration-mp.invalide",
            () => DeclarationMaladieProfessionnelle.Preparer(dossier.Id, dossier.PersonneId, horloge.Aujourdhui(), user.UserId, contenu));
        if (!declaration.IsSuccess)
        {
            return declaration.Error!;
        }

        declarations.Add(declaration.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return declaration.Value.Dto();
    }
}

public sealed record ObtenirDeclarationMp(Guid DeclarationId);

public sealed class ObtenirDeclarationMpHandler(AccesDeclarationsMp acces) : IQueryHandler<ObtenirDeclarationMp, DeclarationMpDto>
{
    public async Task<Result<DeclarationMpDto>> HandleAsync(ObtenirDeclarationMp query, CancellationToken cancellationToken)
    {
        var declaration = await acces.LireAsync(query.DeclarationId, cancellationToken);
        return declaration.IsSuccess ? declaration.Value.Dto() : declaration.Error!;
    }
}

/// <summary>SAN-70 : envoi à Fedris (port <see cref="IFedris"/>, simulateur tant que le canal réel n'est pas confirmé).</summary>
public sealed record EnvoyerDeclarationMp(Guid DeclarationId);

public sealed class EnvoyerDeclarationMpHandler(AccesDeclarationsMp acces, IFedris fedris, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<EnvoyerDeclarationMp, DeclarationMpDto>
{
    public async Task<Result<DeclarationMpDto>> HandleAsync(EnvoyerDeclarationMp command, CancellationToken cancellationToken)
    {
        var acces1 = await acces.DeclarerAsync(command.DeclarationId, ActionAudit.Export, cancellationToken);
        if (!acces1.IsSuccess)
        {
            return acces1.Error!;
        }

        var declaration = acces1.Value;
        if (declaration.Statut != StatutDeclarationMp.Brouillon)
        {
            return Error.Conflict("declaration-mp.deja-envoyee", "Cette déclaration a déjà été envoyée.");
        }

        var date = horloge.Aujourdhui();
        var reference = await fedris.DeclarerAsync(new DeclarationFedris(declaration.Id, declaration.PersonneId, declaration.Contenu, date), cancellationToken);
        var resultat = Regles.Appliquer("declaration-mp.envoi", () => declaration.MarquerEnvoyee(reference, date));
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return declaration.Dto();
    }
}

/// <summary>SAN-71 : suivi du dossier déclaré (statut consulté auprès de Fedris).</summary>
public sealed record SuivreDeclarationMp(Guid DeclarationId);

public sealed class SuivreDeclarationMpHandler(AccesDeclarationsMp acces, IFedris fedris, IUnitOfWork unitOfWork) : ICommandHandler<SuivreDeclarationMp, DeclarationMpDto>
{
    public async Task<Result<DeclarationMpDto>> HandleAsync(SuivreDeclarationMp command, CancellationToken cancellationToken)
    {
        var acces1 = await acces.SuivreAsync(command.DeclarationId, cancellationToken);
        if (!acces1.IsSuccess)
        {
            return acces1.Error!;
        }

        var declaration = acces1.Value;
        if (declaration.ReferenceFedris is null)
        {
            return Error.Validation("declaration-mp.non-envoyee", "La déclaration n'a pas encore été envoyée à Fedris.");
        }

        var statut = await fedris.ConsulterStatutAsync(declaration.ReferenceFedris, cancellationToken);
        var resultat = Regles.Appliquer("declaration-mp.suivi", () => declaration.AppliquerStatutFedris(statut.Statut, statut.Date));
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return declaration.Dto();
    }
}

/// <summary>SAN-71 : demande d'information reçue de Fedris.</summary>
public sealed record EnregistrerDemandeInformationFedris(Guid DeclarationId, DateOnly DateDemande, DateOnly? Echeance, string Objet)
{
    public override string ToString() => $"EnregistrerDemandeInformationFedris {{ DeclarationId = {DeclarationId} }}";
}

public sealed class EnregistrerDemandeInformationFedrisHandler(AccesDeclarationsMp acces, IUnitOfWork unitOfWork)
    : ICommandHandler<EnregistrerDemandeInformationFedris, Guid>
{
    public async Task<Result<Guid>> HandleAsync(EnregistrerDemandeInformationFedris command, CancellationToken cancellationToken)
    {
        var declaration = await acces.SuivreAsync(command.DeclarationId, cancellationToken);
        if (!declaration.IsSuccess)
        {
            return declaration.Error!;
        }

        var resultat = Regles.Appliquer("declaration-mp.demande",
            () => declaration.Value.EnregistrerDemandeInformation(command.DateDemande, command.Echeance, command.Objet).Id);
        if (resultat.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return resultat;
    }
}

public sealed record RepondreDemandeInformationFedris(Guid DeclarationId, Guid DemandeId, DateOnly Date, string Reponse)
{
    public override string ToString() => $"RepondreDemandeInformationFedris {{ DeclarationId = {DeclarationId}, DemandeId = {DemandeId} }}";
}

public sealed class RepondreDemandeInformationFedrisHandler(AccesDeclarationsMp acces, IUnitOfWork unitOfWork)
    : ICommandHandler<RepondreDemandeInformationFedris, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RepondreDemandeInformationFedris command, CancellationToken cancellationToken)
    {
        var declaration = await acces.DeclarerAsync(command.DeclarationId, ActionAudit.Modification, cancellationToken);
        if (!declaration.IsSuccess)
        {
            return declaration.Error!;
        }

        var resultat = Regles.Appliquer("declaration-mp.reponse", () => declaration.Value.RepondreDemande(command.DemandeId, command.Date, command.Reponse));
        if (resultat.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return resultat;
    }
}
