using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Reintegration.Application.Trajets;
using Sepp.Reintegration.Domain;
using Sepp.Reintegration.Domain.Incapacites;

namespace Sepp.Reintegration.Application.Incapacites;

public sealed record EcheanceDto(TypeEcheance Type, DateOnly Date, StatutEcheance Statut, DateOnly? RespecteeLe, string BaseLegale, bool AlerteEmise);

public sealed record IncapaciteResumeDto(
    Guid Id, Guid PersonneId, Guid AffilieId, DateOnly DateDebut, DateOnly? DateFinPrevue, DateOnly? DateFin, SourceIncapacite Source,
    StatutIncapacite Statut, string? GestionnaireId, Guid? TrajetId);

public sealed record EstimationDto(Guid Id, DateOnly Date, ResultatEstimation Resultat, string Methode, string AuteurId, string? Commentaire);

public sealed record IncapaciteDto(
    IncapaciteResumeDto Incapacite,
    int? EffectifEmployeur,
    DateOnly? TravailleurInformeLe,
    Guid? DocumentInformationId,
    IReadOnlyList<EstimationDto> Estimations,
    IReadOnlyList<TrajetResumeDto> Trajets,
    IReadOnlyList<EcheanceDto> Echeances);

public static class IncapaciteProjections
{
    public static IncapaciteResumeDto Resume(this Incapacite i) =>
        new(i.Id, i.PersonneId, i.AffilieId, i.DateDebut, i.DateFinPrevue, i.DateFin, i.Source, i.Statut, i.GestionnaireId, i.TrajetId);

    public static IReadOnlyList<EcheanceDto> Dto(this ProcessusAEcheances processus, PolitiqueReintegration politique, DateOnly aujourdHui) =>
    [
        .. processus.Echeances(politique).OrderBy(e => e.Date).Select(e =>
        {
            var statut = e.StatutAu(aujourdHui, politique);
            return new EcheanceDto(e.Type, e.Date, statut, e.RespecteeLe, e.BaseLegale,
                processus.AlerteDeclenchee(e, StatutEcheance.Proche) || processus.AlerteDeclenchee(e, StatutEcheance.Depassee));
        }),
    ];
}

/// <summary>Reprogramme les minuteries d'un dossier après chaque changement (ADR 0008).</summary>
public sealed class MinuteriesDossier(ParametresReintegration parametres)
{
    public async Task ReprogrammerAsync(Incapacite incapacite, CancellationToken cancellationToken) =>
        incapacite.Reprogrammer(await parametres.PolitiqueAsync(incapacite.DateDebut, cancellationToken));

    public async Task ReprogrammerAsync(Domain.Trajets.Trajet trajet, CancellationToken cancellationToken) =>
        trajet.Reprogrammer(await parametres.PolitiqueAsync(trajet.DateDemande, cancellationToken));
}

/// <summary>SAN-60 : saisie manuelle d'une notification d'incapacité reçue hors flux eHealth ; le saisissant en devient gestionnaire.</summary>
public sealed record SaisirIncapacite(Guid PersonneId, Guid AffilieId, DateOnly DateDebut, DateOnly? DateFinPrevue);

public sealed class SaisirIncapaciteHandler(
    IIncapaciteRepository incapacites, GardeReintegration garde, MinuteriesDossier minuteries, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<SaisirIncapacite, Guid>
{
    public async Task<Result<Guid>> HandleAsync(SaisirIncapacite command, CancellationToken cancellationToken)
    {
        if (garde.VerifierPermission(ActionAudit.Creation) is { } interdit)
        {
            return interdit;
        }

        var saisie = Regles.Appliquer("reintegration.incapacite-invalide", () =>
            Incapacite.Saisir(command.PersonneId, command.AffilieId, command.DateDebut, command.DateFinPrevue, garde.UtilisateurId, horloge.GetUtcNow()));
        if (!saisie.IsSuccess)
        {
            return saisie.Error!;
        }

        await minuteries.ReprogrammerAsync(saisie.Value, cancellationToken);
        incapacites.Add(saisie.Value);
        garde.Journaliser(ActionAudit.Creation, PartiesDossier.Incapacite, saisie.Value.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return saisie.Value.Id;
    }
}

/// <summary>Recherche des dossiers d'une personne : identifiants, dates et statuts, sans contenu médical ; chaque dossier trouvé est tracé.</summary>
public sealed record RechercherIncapacites(Guid PersonneId);

public sealed class RechercherIncapacitesHandler(IIncapaciteRepository incapacites, GardeReintegration garde, IContexteAcces contexte)
    : IQueryHandler<RechercherIncapacites, IReadOnlyList<IncapaciteResumeDto>>
{
    public async Task<Result<IReadOnlyList<IncapaciteResumeDto>>> HandleAsync(RechercherIncapacites query, CancellationToken cancellationToken)
    {
        if (garde.VerifierPermission(ActionAudit.Lecture) is { } interdit)
        {
            return interdit;
        }

        if (MotifAcces.Verifier(contexte.Motif, contexte.BrisDeGlace) is { } motifInvalide)
        {
            return motifInvalide;
        }

        var trouvees = await incapacites.ListerParPersonneAsync(query.PersonneId, cancellationToken);
        foreach (var incapacite in trouvees)
        {
            garde.Journaliser(ActionAudit.Lecture, PartiesDossier.Recherche, incapacite.Id);
        }

        await garde.ValiderTracesAsync(cancellationToken);
        return trouvees.OrderByDescending(i => i.DateDebut).Select(i => i.Resume()).ToList();
    }
}

public sealed record ObtenirIncapacite(Guid IncapaciteId);

public sealed class ObtenirIncapaciteHandler(GardeReintegration garde, ITrajetRepository trajets, ParametresReintegration parametres, TimeProvider horloge)
    : IQueryHandler<ObtenirIncapacite, IncapaciteDto>
{
    public async Task<Result<IncapaciteDto>> HandleAsync(ObtenirIncapacite query, CancellationToken cancellationToken)
    {
        var acces = await garde.LireAsync(query.IncapaciteId, PartiesDossier.Incapacite, cancellationToken);
        if (!acces.IsSuccess)
        {
            return acces.Error!;
        }

        var i = acces.Value;
        var aujourdHui = horloge.Aujourdhui();
        var politique = await parametres.PolitiqueAsync(i.DateDebut, cancellationToken);
        var liste = await trajets.ListerParIncapaciteAsync(i.Id, cancellationToken);
        return new IncapaciteDto(
            i.Resume(),
            i.EffectifEmployeur,
            i.TravailleurInformeLe,
            i.DocumentInformationId,
            [.. i.Estimations.OrderBy(e => e.Date).Select(e => new EstimationDto(e.Id, e.Date, e.Resultat, e.Methode, e.AuteurId, e.Commentaire))],
            [.. liste.OrderBy(t => t.DateDemande).Select(t => t.Resume())],
            i.Dto(politique, aujourdHui));
    }
}

/// <summary>Modification d'un dossier par un professionnel autorisé : contrôle, règle métier, minuteries, transaction unique.</summary>
public sealed class ModificationDossier(GardeReintegration garde, MinuteriesDossier minuteries, IUnitOfWork unitOfWork)
{
    public async Task<Result<T>> ExecuterAsync<T>(Guid incapaciteId, string partie, string codeErreur, Func<Incapacite, T> regle, CancellationToken cancellationToken, bool relationEtablie = false)
    {
        var acces = await garde.EcrireAsync(incapaciteId, partie, ActionAudit.Modification, cancellationToken, relationEtablie);
        if (!acces.IsSuccess)
        {
            return acces.Error!;
        }

        var resultat = Regles.Appliquer(codeErreur, () => regle(acces.Value));
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        await minuteries.ReprogrammerAsync(acces.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return resultat;
    }
}

/// <summary>Prise en charge du dossier : l'utilisateur en devient gestionnaire (relation de soin ; motif exigé s'il ne l'était pas).</summary>
public sealed record PrendreEnCharge(Guid IncapaciteId);

public sealed class PrendreEnChargeHandler(ModificationDossier modification, GardeReintegration garde) : ICommandHandler<PrendreEnCharge, Unit>
{
    public Task<Result<Unit>> HandleAsync(PrendreEnCharge command, CancellationToken cancellationToken) =>
        modification.ExecuterAsync(command.IncapaciteId, PartiesDossier.Incapacite, "reintegration.incapacite-invalide", i =>
        {
            i.PrendreEnCharge(garde.UtilisateurId);
            return Unit.Value;
        }, cancellationToken);
}

/// <summary>Effectif de l'employeur : détermine si le trajet est obligatoire (<c>REINTEGRATION.SEUIL_TRAVAILLEURS</c>).</summary>
public sealed record DefinirEffectifEmployeur(Guid IncapaciteId, int Effectif);

public sealed class DefinirEffectifEmployeurHandler(ModificationDossier modification) : ICommandHandler<DefinirEffectifEmployeur, Unit>
{
    public Task<Result<Unit>> HandleAsync(DefinirEffectifEmployeur command, CancellationToken cancellationToken) =>
        modification.ExecuterAsync(command.IncapaciteId, PartiesDossier.Incapacite, "reintegration.incapacite-invalide", i =>
        {
            i.DefinirEffectifEmployeur(command.Effectif);
            return Unit.Value;
        }, cancellationToken);
}

/// <summary>SAN-60 : information du travailleur (dès <c>REINTEGRATION.INFORMATION_TRAVAILLEUR</c> d'incapacité).</summary>
public sealed record InformerTravailleur(Guid IncapaciteId, DateOnly Date, Guid? DocumentId);

public sealed class InformerTravailleurHandler(ModificationDossier modification) : ICommandHandler<InformerTravailleur, Unit>
{
    public Task<Result<Unit>> HandleAsync(InformerTravailleur command, CancellationToken cancellationToken) =>
        modification.ExecuterAsync(command.IncapaciteId, PartiesDossier.Incapacite, "reintegration.information-invalide", i =>
        {
            i.EnregistrerInformationTravailleur(command.Date, command.DocumentId);
            return Unit.Value;
        }, cancellationToken);
}

/// <summary>SAN-61 : estimation du potentiel de travail selon la méthode standardisée, par le CPMT ou l'infirmier.</summary>
public sealed record EstimerPotentiel(Guid IncapaciteId, DateOnly Date, ResultatEstimation Resultat, string Methode, string? Commentaire);

public sealed class EstimerPotentielHandler(ModificationDossier modification, GardeReintegration garde) : ICommandHandler<EstimerPotentiel, Guid>
{
    public Task<Result<Guid>> HandleAsync(EstimerPotentiel command, CancellationToken cancellationToken) =>
        modification.ExecuterAsync(command.IncapaciteId, PartiesDossier.Estimation, "reintegration.estimation-invalide",
            i => i.EstimerPotentiel(command.Date, command.Resultat, command.Methode, garde.UtilisateurId, command.Commentaire).Id, cancellationToken);
}

/// <summary>Reprise du travail : fin de l'incapacité (les échéances cessent ; un trajet en cours se poursuit, à valider).</summary>
public sealed record TerminerIncapacite(Guid IncapaciteId, DateOnly DernierJour);

public sealed class TerminerIncapaciteHandler(ModificationDossier modification) : ICommandHandler<TerminerIncapacite, Unit>
{
    public Task<Result<Unit>> HandleAsync(TerminerIncapacite command, CancellationToken cancellationToken) =>
        modification.ExecuterAsync(command.IncapaciteId, PartiesDossier.Incapacite, "reintegration.incapacite-invalide", i =>
        {
            i.Terminer(command.DernierJour);
            return Unit.Value;
        }, cancellationToken);
}

/// <summary>Notification erronée : annulation de l'incapacité et compensation du trajet rattaché (<c>TrajetTermine(ANNULE)</c>).</summary>
public sealed record AnnulerIncapacite(Guid IncapaciteId);

public sealed class AnnulerIncapaciteHandler(GardeReintegration garde, CompensationTrajet compensation, MinuteriesDossier minuteries, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<AnnulerIncapacite, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AnnulerIncapacite command, CancellationToken cancellationToken)
    {
        var acces = await garde.EcrireAsync(command.IncapaciteId, PartiesDossier.Incapacite, ActionAudit.Suppression, cancellationToken);
        if (!acces.IsSuccess)
        {
            return acces.Error!;
        }

        var incapacite = acces.Value;
        var trajetId = incapacite.TrajetId;
        if (incapacite.Annuler(horloge.GetUtcNow()) && trajetId is { } id)
        {
            var annulation = await compensation.AnnulerAsync(id, incapacite, horloge.Aujourdhui(), cancellationToken);
            if (!annulation.IsSuccess)
            {
                return annulation.Error!;
            }
        }

        await minuteries.ReprogrammerAsync(incapacite, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
