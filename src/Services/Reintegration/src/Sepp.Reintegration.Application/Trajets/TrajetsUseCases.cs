using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Reintegration.Application.Incapacites;
using Sepp.Reintegration.Domain.Echanges;
using Sepp.Reintegration.Domain.Incapacites;
using Sepp.Reintegration.Domain.Trajets;

namespace Sepp.Reintegration.Application.Trajets;

public sealed record TrajetResumeDto(Guid Id, Guid IncapaciteId, InitiateurTrajet Initiateur, DateOnly DateDemande, StatutTrajet Statut, DateOnly DateLimiteInvitation, DateOnly? FinLe, IssueTrajet? Issue);

public sealed record EtapeDto(TypeEtape Type, DateOnly Date, DateOnly? Echeance, Guid? DocumentId);

public sealed record AbsenceDto(Guid Id, Guid? RendezVousId, DateOnly DateRendezVous, bool NotifieMedecinConseil);

/// <summary>Trajet complet (zone médicale) : la note d'évaluation est une donnée médicale.</summary>
public sealed record TrajetDto(
    TrajetResumeDto Trajet,
    Guid PersonneId,
    Guid AffilieId,
    DateOnly? InvitationEnvoyeeLe,
    bool InvitationRecommandee,
    DateOnly? EvalueLe,
    string? EvaluateurId,
    string? NoteEvaluation,
    DateOnly? FormulaireLe,
    ConclusionEvaluation? Conclusion,
    ReponseEmployeur? ReponseEmployeur,
    DateOnly? ReponseEmployeurLe,
    DateOnly? EcheanceReponseTravailleur,
    ReponseTravailleur? ReponseTravailleur,
    DateOnly? ReponseTravailleurLe,
    DateOnly? MedecinConseilInformeLe,
    IReadOnlyList<AbsenceDto> Absences,
    IReadOnlyList<EtapeDto> Etapes,
    IReadOnlyList<EcheanceDto> Echeances);

/// <summary>Suivi du trajet pour l'employeur, le SIPP, le gestionnaire et le travailleur : statuts, dates et conclusion, aucune donnée médicale.</summary>
public sealed record SuiviTrajetDto(
    Guid TrajetId,
    Guid PersonneId,
    Guid AffilieId,
    StatutTrajet Statut,
    DateOnly DateDemande,
    DateOnly DateLimiteInvitation,
    DateOnly? InvitationEnvoyeeLe,
    DateOnly? FormulaireLe,
    ConclusionEvaluation? Conclusion,
    ReponseEmployeur? ReponseEmployeur,
    DateOnly? ReponseEmployeurLe,
    DateOnly? EcheanceReponseTravailleur,
    ReponseTravailleur? ReponseTravailleur,
    DateOnly? FinLe,
    IssueTrajet? Issue);

public static class TrajetProjections
{
    public static TrajetResumeDto Resume(this Trajet t) => new(t.Id, t.IncapaciteId, t.Initiateur, t.DateDemande, t.Statut, t.DateLimiteInvitation, t.FinLe, t.Issue);

    public static SuiviTrajetDto Suivi(this Trajet t) => new(
        t.Id, t.PersonneId, t.AffilieId, t.Statut, t.DateDemande, t.DateLimiteInvitation, t.InvitationEnvoyeeLe, t.FormulaireLe, t.Conclusion,
        t.ReponseEmployeur, t.ReponseEmployeurLe, t.EcheanceReponseTravailleur, t.ReponseTravailleur, t.FinLe, t.Issue);
}

/// <summary>
/// SAN-63 : au seuil d'absences, le médecin-conseil est informé par eHealth (échange en attente d'envoi, traité par le service
/// d'arrière-plan). Le message ne porte que le nombre d'absences et les dates des rendez-vous manqués.
/// </summary>
public sealed class InformationMedecinConseil(IEchangeEHealthRepository echanges, TimeProvider horloge)
{
    public void Traiter(Trajet trajet)
    {
        foreach (var seuil in trajet.DomainEvents.OfType<SeuilAbsencesAtteint>())
        {
            var dates = string.Join(", ", trajet.Absences.OrderBy(a => a.DateRendezVous).Select(a => Regles.Jour(a.DateRendezVous)));
            echanges.Add(EchangeEHealth.Preparer(trajet.IncapaciteId, trajet.Id, trajet.PersonneId, DestinataireEHealth.MedecinConseil, ObjetEchange.NotificationAbsences,
                $"Trajet de réintégration : {seuil.NombreAbsences} absences aux rendez-vous ({dates}), seuil REINTEGRATION.ABSENCES_MEDECIN_CONSEIL atteint.",
                null, accordTravailleur: false, horloge.GetUtcNow()));
        }
    }
}

/// <summary>
/// Compensation du trajet (ARC-33) : annulation, déliaison de l'incapacité, annulation des envois eHealth non effectués,
/// publication de <c>TrajetTermine(ANNULE)</c> (Obligations clôt l'évaluation de réintégration due). L'appelant enregistre.
/// </summary>
public sealed class CompensationTrajet(ITrajetRepository trajets, IEchangeEHealthRepository echanges, MinuteriesDossier minuteries, IIntegrationEventOutbox outbox, TimeProvider horloge)
{
    public async Task<Result<Unit>> AnnulerAsync(Guid trajetId, Incapacite incapacite, DateOnly date, CancellationToken cancellationToken)
    {
        var trajet = await trajets.GetAsync(trajetId, cancellationToken);
        return trajet is null ? GardeReintegration.TrajetInconnu : await AnnulerAsync(trajet, incapacite, date, cancellationToken);
    }

    public async Task<Result<Unit>> AnnulerAsync(Trajet trajet, Incapacite incapacite, DateOnly date, CancellationToken cancellationToken)
    {
        var annule = Regles.Appliquer("reintegration.trajet-invalide", () => trajet.Annuler(date, horloge.GetUtcNow()));
        if (!annule.IsSuccess)
        {
            return annule.Error!;
        }

        incapacite.DelierTrajet(trajet.Id);
        foreach (var echange in await echanges.ListerParTrajetAsync(trajet.Id, cancellationToken))
        {
            echange.Annuler();
        }

        await minuteries.ReprogrammerAsync(trajet, cancellationToken);
        EvenementsIntegration.Publier(trajet, outbox);
        return Unit.Value;
    }
}

/// <summary>SAN-62 : demande d'un trajet formel (travailleur, médecin traitant, médecin-conseil, employeur ou CPMT).</summary>
public sealed record DemanderTrajet(Guid IncapaciteId, InitiateurTrajet Initiateur, DateOnly DateDemande);

public sealed class DemanderTrajetHandler(
    GardeReintegration garde, ITrajetRepository trajets, ParametresReintegration parametres, MinuteriesDossier minuteries,
    IIntegrationEventOutbox outbox, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<DemanderTrajet, Guid>
{
    public async Task<Result<Guid>> HandleAsync(DemanderTrajet command, CancellationToken cancellationToken)
    {
        var acces = await garde.EcrireAsync(command.IncapaciteId, PartiesDossier.Trajet, ActionAudit.Creation, cancellationToken);
        if (!acces.IsSuccess)
        {
            return acces.Error!;
        }

        var incapacite = acces.Value;
        if (incapacite.TrajetId is not null)
        {
            return Error.Conflict("reintegration.trajet-en-cours", "Un trajet de réintégration est déjà en cours pour cette incapacité.");
        }

        var politique = await parametres.PolitiqueAsync(command.DateDemande, cancellationToken);
        var demande = Regles.Appliquer("reintegration.trajet-invalide", () =>
        {
            if (command.DateDemande < incapacite.DateDebut)
            {
                throw new Sepp.BuildingBlocks.Domain.DomainException("La demande de trajet ne peut pas précéder le début de l'incapacité.");
            }

            var trajet = Trajet.Demander(incapacite.Id, incapacite.PersonneId, incapacite.AffilieId, command.Initiateur, command.DateDemande, politique, horloge.GetUtcNow());
            incapacite.LierTrajet(trajet.Id, command.DateDemande);
            return trajet;
        });
        if (!demande.IsSuccess)
        {
            return demande.Error!;
        }

        demande.Value.Reprogrammer(politique);
        await minuteries.ReprogrammerAsync(incapacite, cancellationToken);
        trajets.Add(demande.Value);
        EvenementsIntegration.Publier(demande.Value, outbox);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return demande.Value.Id;
    }
}

public sealed record ObtenirTrajet(Guid TrajetId);

public sealed class ObtenirTrajetHandler(GardeReintegration garde, ParametresReintegration parametres, TimeProvider horloge) : IQueryHandler<ObtenirTrajet, TrajetDto>
{
    public async Task<Result<TrajetDto>> HandleAsync(ObtenirTrajet query, CancellationToken cancellationToken)
    {
        var acces = await garde.TrajetAsync(query.TrajetId, PartiesDossier.Trajet, ActionAudit.Lecture, cancellationToken);
        if (!acces.IsSuccess)
        {
            return acces.Error!;
        }

        var t = acces.Value.Trajet;
        var politique = await parametres.PolitiqueAsync(t.DateDemande, cancellationToken);
        return new TrajetDto(
            t.Resume(), t.PersonneId, t.AffilieId, t.InvitationEnvoyeeLe, t.InvitationRecommandee, t.EvalueLe, t.EvaluateurId, t.NoteEvaluation,
            t.FormulaireLe, t.Conclusion, t.ReponseEmployeur, t.ReponseEmployeurLe, t.EcheanceReponseTravailleur, t.ReponseTravailleur, t.ReponseTravailleurLe,
            t.MedecinConseilInformeLe,
            [.. t.Absences.OrderBy(a => a.DateRendezVous).Select(a => new AbsenceDto(a.Id, a.RendezVousId, a.DateRendezVous, a.NotifieMedecinConseil))],
            [.. t.Etapes.OrderBy(e => e.Date).Select(e => new EtapeDto(e.Type, e.Date, e.Echeance, e.DocumentId))],
            t.Dto(politique, horloge.Aujourdhui()));
    }
}

/// <summary>Étape du trajet par un professionnel de la zone médicale : contrôle d'accès, règle métier, minuteries et publication.</summary>
public sealed class EtapeTrajetMedicale(
    GardeReintegration garde, ParametresReintegration parametres, ICurrentUser user, IIntegrationEventOutbox outbox, IUnitOfWork unitOfWork)
{
    public async Task<Result<Unit>> ExecuterAsync(
        Guid trajetId, string partie, Action<Trajet, Domain.PolitiqueReintegration> etape, CancellationToken cancellationToken, string? permissionSupplementaire = null,
        Action<Trajet, Incapacite>? apres = null)
    {
        if (permissionSupplementaire is not null && !user.HasPermission(permissionSupplementaire))
        {
            return Error.Forbidden("reintegration.interdit", "L'évaluation de réintégration et son formulaire sont réservés au CPMT (§3.3).");
        }

        var acces = await garde.TrajetAsync(trajetId, partie, ActionAudit.Modification, cancellationToken);
        if (!acces.IsSuccess)
        {
            return acces.Error!;
        }

        var trajet = acces.Value.Trajet;
        var politique = await parametres.PolitiqueAsync(trajet.DateDemande, cancellationToken);
        var resultat = Regles.Appliquer("reintegration.trajet-invalide", () => etape(trajet, politique));
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        apres?.Invoke(trajet, acces.Value.Incapacite);
        trajet.Reprogrammer(politique);
        EvenementsIntegration.Publier(trajet, outbox);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Invitation (recommandée) du travailleur à l'évaluation de réintégration.</summary>
public sealed record InviterTravailleur(Guid TrajetId, DateOnly Date, bool Recommandee, Guid? DocumentId);

public sealed class InviterTravailleurHandler(EtapeTrajetMedicale etape) : ICommandHandler<InviterTravailleur, Unit>
{
    public Task<Result<Unit>> HandleAsync(InviterTravailleur c, CancellationToken cancellationToken) =>
        etape.ExecuterAsync(c.TrajetId, PartiesDossier.Trajet, (t, _) => t.Inviter(c.Date, c.Recommandee, c.DocumentId), cancellationToken);
}

public sealed record EvaluerReintegration(Guid TrajetId, DateOnly Date, string? Note);

public sealed class EvaluerReintegrationHandler(EtapeTrajetMedicale etape, GardeReintegration garde) : ICommandHandler<EvaluerReintegration, Unit>
{
    public Task<Result<Unit>> HandleAsync(EvaluerReintegration c, CancellationToken cancellationToken) =>
        etape.ExecuterAsync(c.TrajetId, PartiesDossier.Trajet, (t, _) => t.Evaluer(c.Date, garde.UtilisateurId, c.Note), cancellationToken, Permissions.ReintegrationEvaluer);
}

public sealed record EtablirFormulaire(Guid TrajetId, DateOnly Date, ConclusionEvaluation Conclusion, Guid? DocumentId);

public sealed class EtablirFormulaireHandler(EtapeTrajetMedicale etape) : ICommandHandler<EtablirFormulaire, Unit>
{
    public Task<Result<Unit>> HandleAsync(EtablirFormulaire c, CancellationToken cancellationToken) =>
        etape.ExecuterAsync(c.TrajetId, PartiesDossier.Trajet, (t, p) => t.EtablirFormulaire(c.Date, c.Conclusion, c.DocumentId, p), cancellationToken, Permissions.ReintegrationEvaluer);
}

public sealed record TerminerTrajet(Guid TrajetId, DateOnly Date, IssueTrajet Issue);

public sealed class TerminerTrajetHandler(EtapeTrajetMedicale etape, TimeProvider horloge) : ICommandHandler<TerminerTrajet, Unit>
{
    // Un trajet terminé libère l'incapacité : un nouveau trajet pourra être demandé.
    public Task<Result<Unit>> HandleAsync(TerminerTrajet c, CancellationToken cancellationToken) =>
        etape.ExecuterAsync(c.TrajetId, PartiesDossier.Trajet, (t, _) => t.Terminer(c.Date, c.Issue, horloge.GetUtcNow()), cancellationToken,
            apres: (t, incapacite) => incapacite.DelierTrajet(t.Id));
}

public sealed record AnnulerTrajet(Guid TrajetId, DateOnly Date);

public sealed class AnnulerTrajetHandler(GardeReintegration garde, CompensationTrajet compensation, MinuteriesDossier minuteries, IUnitOfWork unitOfWork)
    : ICommandHandler<AnnulerTrajet, Unit>
{
    public async Task<Result<Unit>> HandleAsync(AnnulerTrajet c, CancellationToken cancellationToken)
    {
        var acces = await garde.TrajetAsync(c.TrajetId, PartiesDossier.Trajet, ActionAudit.Modification, cancellationToken);
        if (!acces.IsSuccess)
        {
            return acces.Error!;
        }

        var resultat = await compensation.AnnulerAsync(acces.Value.Trajet, acces.Value.Incapacite, c.Date, cancellationToken);
        if (!resultat.IsSuccess)
        {
            return resultat;
        }

        await minuteries.ReprogrammerAsync(acces.Value.Incapacite, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return resultat;
    }
}

/// <summary>SAN-63 : absence à un rendez-vous saisie par le professionnel (les absences constatées par Planification arrivent par événement).</summary>
public sealed record SignalerAbsence(Guid TrajetId, DateOnly DateRendezVous);

public sealed class SignalerAbsenceHandler(EtapeTrajetMedicale etape, InformationMedecinConseil information, TimeProvider horloge) : ICommandHandler<SignalerAbsence, Unit>
{
    public Task<Result<Unit>> HandleAsync(SignalerAbsence c, CancellationToken cancellationToken) =>
        etape.ExecuterAsync(c.TrajetId, PartiesDossier.Absences,
            (t, p) => t.EnregistrerAbsence(null, c.DateRendezVous, horloge.Aujourdhui(), p, horloge.GetUtcNow()), cancellationToken, apres: (t, _) => information.Traiter(t));
}

/// <summary>
/// Plan de réintégration ou rapport motivé de l'employeur (référence au document). Déposé par l'employeur ou le SIPP de
/// l'affilié, par le gestionnaire, ou enregistré par le professionnel de la zone médicale.
/// </summary>
public sealed record DeposerReponseEmployeur(Guid TrajetId, ReponseEmployeur Reponse, DateOnly Date, Guid? DocumentId);

public sealed class DeposerReponseEmployeurHandler(
    ITrajetRepository trajets, AccesSuivi perimetre, ICurrentUser user, EtapeTrajetMedicale etape, GardeReintegration garde,
    ParametresReintegration parametres, IIntegrationEventOutbox outbox, IUnitOfWork unitOfWork)
    : ICommandHandler<DeposerReponseEmployeur, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeposerReponseEmployeur c, CancellationToken cancellationToken)
    {
        if (!user.HasPermission(Permissions.ReintegrationPlanDeposer))
        {
            return await etape.ExecuterAsync(c.TrajetId, PartiesDossier.Trajet, (t, p) => t.DeposerReponseEmployeur(c.Reponse, c.Date, c.DocumentId, p), cancellationToken);
        }

        var trajet = await trajets.GetAsync(c.TrajetId, cancellationToken);
        if (trajet is null || !perimetre.PeutVoirAffilie(trajet.AffilieId))
        {
            return AccesSuivi.HorsPerimetre;
        }

        var politique = await parametres.PolitiqueAsync(trajet.DateDemande, cancellationToken);
        var resultat = Regles.Appliquer("reintegration.trajet-invalide", () => trajet.DeposerReponseEmployeur(c.Reponse, c.Date, c.DocumentId, politique));
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        trajet.Reprogrammer(politique);
        garde.Journaliser(ActionAudit.Modification, PartiesDossier.Suivi, trajet.IncapaciteId);
        EvenementsIntegration.Publier(trajet, outbox);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Acceptation ou refus du plan par le travailleur lui-même (claim <c>personne_id</c>) ou enregistré par le professionnel.</summary>
public sealed record EnregistrerReponseTravailleur(Guid TrajetId, ReponseTravailleur Reponse, DateOnly Date);

public sealed class EnregistrerReponseTravailleurHandler(
    ITrajetRepository trajets, AccesSuivi perimetre, ICurrentUser user, EtapeTrajetMedicale etape, GardeReintegration garde,
    ParametresReintegration parametres, IUnitOfWork unitOfWork)
    : ICommandHandler<EnregistrerReponseTravailleur, Unit>
{
    public async Task<Result<Unit>> HandleAsync(EnregistrerReponseTravailleur c, CancellationToken cancellationToken)
    {
        if (!user.HasPermission(Permissions.ReintegrationReponseEnregistrer))
        {
            return await etape.ExecuterAsync(c.TrajetId, PartiesDossier.Trajet, (t, _) => t.EnregistrerReponseTravailleur(c.Reponse, c.Date), cancellationToken);
        }

        var trajet = await trajets.GetAsync(c.TrajetId, cancellationToken);
        if (trajet is null || !perimetre.EstLeTravailleur(trajet.PersonneId))
        {
            return AccesSuivi.HorsPerimetre;
        }

        var resultat = Regles.Appliquer("reintegration.trajet-invalide", () => trajet.EnregistrerReponseTravailleur(c.Reponse, c.Date));
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        trajet.Reprogrammer(await parametres.PolitiqueAsync(trajet.DateDemande, cancellationToken));
        garde.Journaliser(ActionAudit.Modification, PartiesDossier.Suivi, trajet.IncapaciteId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>Suivi des trajets d'un affilié (employeur, SIPP, gestionnaire) : sans donnée médicale ; chaque trajet lu est tracé.</summary>
public sealed record SuivreTrajets(Guid AffilieId);

public sealed class SuivreTrajetsHandler(ITrajetRepository trajets, AccesSuivi perimetre, ICurrentUser user, GardeReintegration garde)
    : IQueryHandler<SuivreTrajets, IReadOnlyList<SuiviTrajetDto>>
{
    public async Task<Result<IReadOnlyList<SuiviTrajetDto>>> HandleAsync(SuivreTrajets query, CancellationToken cancellationToken)
    {
        if (!user.HasPermission(Permissions.ReintegrationSuiviLire))
        {
            return Error.Forbidden("reintegration.interdit", "Suivi des trajets réservé à l'employeur, au SIPP et au gestionnaire.");
        }

        if (!perimetre.PeutVoirAffilie(query.AffilieId))
        {
            return AccesSuivi.HorsPerimetre;
        }

        var liste = await trajets.ListerParAffilieAsync(query.AffilieId, cancellationToken);
        foreach (var trajet in liste)
        {
            garde.Journaliser(ActionAudit.Lecture, PartiesDossier.Suivi, trajet.IncapaciteId);
        }

        await garde.ValiderTracesAsync(cancellationToken);
        return liste.OrderByDescending(t => t.DateDemande).Select(t => t.Suivi()).ToList();
    }
}
