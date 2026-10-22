using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Planification;
using Sepp.Planification.Application.PriseRendezVous;
using Sepp.Planification.Domain;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Convocations;

namespace Sepp.Planification.Application.Reservations;

/// <summary>Créneau ouvert à la réservation en ligne : ni ressource ni autre information interne.</summary>
public sealed record CreneauOuvertDto(Guid Id, Guid LieuId, DateTimeOffset Debut, DateTimeOffset Fin, string TypeActe);

/// <summary>
/// SAN-12 : créneaux ouverts à la réservation en ligne pour un affilié. Sont exclus les cabinets installés chez un autre
/// affilié et les créneaux réservés aux urgences (sauf libérés à l'approche de leur horaire).
/// </summary>
public sealed record ListerCreneauxOuverts(Guid AffilieId, string TypeActe, DateTimeOffset Du, DateTimeOffset Au, Guid? LieuId);

public sealed class ListerCreneauxOuvertsHandler(
    ICreneauRepository creneaux,
    ILieuRepository lieux,
    IObligationRepository obligations,
    IPerimetreUtilisateur perimetre,
    ParametresPlanification parametres,
    TimeProvider horloge,
    ICurrentUser user) : IQueryHandler<ListerCreneauxOuverts, IReadOnlyList<CreneauOuvertDto>>
{
    public async Task<Result<IReadOnlyList<CreneauOuvertDto>>> HandleAsync(ListerCreneauxOuverts query, CancellationToken cancellationToken)
    {
        if (!user.HasPermission(Permissions.PlanificationReserver) && !user.HasPermission(Permissions.PlanificationGerer))
        {
            return Error.Forbidden("planification.interdit", "Vous n'avez pas le droit d'effectuer cette opération.");
        }

        if (perimetre.EstExterne && perimetre.PersonneId is { } personne)
        {
            // Le travailleur ne consulte que les créneaux d'un affilié pour lequel il a une obligation.
            if (!(await obligations.ListerParPersonneAsync(personne, cancellationToken)).Any(o => o.AffilieId == query.AffilieId))
            {
                return Acces.PerimetreInterdit();
            }
        }
        else if (perimetre.EstExterne && !perimetre.PeutAccederAffilie(query.AffilieId))
        {
            return Acces.PerimetreInterdit();
        }

        if (query.Au <= query.Du || query.Au - query.Du > TimeSpan.FromDays(93))
        {
            return Error.Validation("creneaux.periode-invalide", "La période consultée couvre au plus 93 jours.");
        }

        var typeActe = Domaine.Essayer(() => CodeMetier.Normaliser(query.TypeActe, "Type d'acte"));
        if (!typeActe.IsSuccess)
        {
            return typeActe.Error!;
        }

        var lieuxAutorises = (await lieux.ListAsync(cancellationToken))
            .Where(l => l.Actif && (l.AffilieId is null || l.AffilieId == query.AffilieId))
            .Select(l => l.Id)
            .ToHashSet();
        var maintenant = horloge.GetUtcNow();
        var resultat = await creneaux.RechercherAsync(
            new CritereCreneaux(query.Du, query.Au) { TypeActe = typeActe.Value, LieuId = query.LieuId, Statut = StatutCreneau.Libre, OuvertEnLigne = true },
            cancellationToken);
        return resultat
            .Where(c => lieuxAutorises.Contains(c.LieuId) && c.EstOuvertAuxReservations(maintenant, parametres.Options.LiberationUrgence))
            .OrderBy(c => c.Debut)
            .Take(500)
            .Select(c => new CreneauOuvertDto(c.Id, c.LieuId, c.Debut, c.Fin, c.TypeActe))
            .ToList();
    }
}

/// <summary>
/// SAN-12 : réservation en ligne par le travailleur (lui-même, revendication <c>personne_id</c>) ou par l'employeur
/// (affiliés de sa revendication <c>affilie_id</c>). Le rendez-vous couvre les obligations dues de la personne pour le
/// type d'acte du créneau : sans obligation à planifier, la réservation est refusée (elle prouve aussi le lien entre la
/// personne et l'affilié).
/// </summary>
public sealed record ReserverEnLigne(Guid CreneauId, Guid PersonneId, Guid AffilieId, IReadOnlyList<Guid>? ObligationIds);

public sealed class ReserverEnLigneHandler(
    ICreneauRepository creneaux,
    ILieuRepository lieux,
    IObligationRepository obligations,
    PriseDeRendezVous prise,
    IPerimetreUtilisateur perimetre,
    ParametresPlanification parametres,
    IUnitOfWork unitOfWork,
    TimeProvider horloge,
    ICurrentUser user) : ICommandHandler<ReserverEnLigne, Guid>
{
    public async Task<Result<Guid>> HandleAsync(ReserverEnLigne command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationReserver) is { } interdit)
        {
            return interdit;
        }

        if (!Perimetre.PeutAgirPour(perimetre, command.PersonneId, command.AffilieId))
        {
            return Acces.PerimetreInterdit();
        }

        var creneau = await creneaux.GetAsync(command.CreneauId, cancellationToken);
        var lieu = creneau is null ? null : await lieux.GetAsync(creneau.LieuId, cancellationToken);
        if (creneau is null || lieu is null || !creneau.OuvertEnLigne || (lieu.AffilieId is { } chez && chez != command.AffilieId))
        {
            return Error.NotFound("creneau.inconnu", "Créneau inconnu ou non ouvert à la réservation en ligne.");
        }

        if (!creneau.EstOuvertAuxReservations(horloge.GetUtcNow(), parametres.Options.LiberationUrgence))
        {
            return Error.Conflict("creneau.indisponible", "Ce créneau n'est plus disponible.");
        }

        var dues = (await obligations.ListerParPersonneAsync(command.PersonneId, cancellationToken))
            .Where(o => o.AffilieId == command.AffilieId && o.EstAPlanifier && o.TypeExamen == creneau.TypeActe)
            .ToList();
        var couvertes = command.ObligationIds is { Count: > 0 } demandees
            ? dues.Where(o => demandees.Contains(o.ObligationId)).Select(o => o.ObligationId).ToList()
            : dues.Select(o => o.ObligationId).ToList();
        if (couvertes.Count == 0 || (command.ObligationIds is { Count: > 0 } ids && couvertes.Count != ids.Distinct().Count()))
        {
            return Error.Validation("reservation.aucune-obligation",
                "Aucune obligation à planifier de cette personne pour cet affilié ne correspond au type de ce créneau.");
        }

        var rdv = await prise.PlanifierAsync(creneau,
            new DemandeRendezVous(command.PersonneId, command.AffilieId, couvertes, OrigineRendezVous.ReservationEnLigne, false), cancellationToken);
        if (!rdv.IsSuccess)
        {
            return rdv.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return rdv.Value.Id;
    }
}

/// <summary>SAN-12 : rendez-vous visibles par un externe (le travailleur : les siens ; l'employeur : ceux d'un de ses affiliés).</summary>
public sealed record ListerMesRendezVous(Guid? AffilieId);

public sealed class ListerMesRendezVousHandler(IRendezVousRepository rendezVous, IPerimetreUtilisateur perimetre, ICurrentUser user)
    : IQueryHandler<ListerMesRendezVous, IReadOnlyList<RendezVousDto>>
{
    public async Task<Result<IReadOnlyList<RendezVousDto>>> HandleAsync(ListerMesRendezVous query, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationReserver) is { } interdit)
        {
            return interdit;
        }

        CritereRendezVous critere;
        if (perimetre.PersonneId is { } personne)
        {
            critere = new CritereRendezVous { PersonneId = personne, AffilieId = query.AffilieId };
        }
        else if (query.AffilieId is { } affilie && perimetre.PeutAccederAffilie(affilie))
        {
            critere = new CritereRendezVous { AffilieId = affilie };
        }
        else
        {
            return query.AffilieId is null
                ? Error.Validation("perimetre.affilie-obligatoire", "Précisez l'affilié consulté.")
                : Acces.PerimetreInterdit();
        }

        return (await rendezVous.RechercherAsync(critere, cancellationToken))
            .OrderByDescending(r => r.Debut).Take(500).Select(r => r.ToDto(externe: true)).ToList();
    }
}

/// <summary>
/// SAN-12, POR-11 : annulation en ligne, au plus tard 24 heures avant le rendez-vous (ensuite, contacter le SEPP).
/// <paramref name="Raison"/> est un code de <see cref="RaisonAnnulation"/> (facultatif pour la compatibilité des appelants
/// existants) ; <paramref name="Commentaire"/> est un texte libre court, sans donnée de santé, conservé dans Planification
/// et visible des seuls planificateurs (jamais publié).
/// </summary>
public sealed record AnnulerEnLigne(Guid RendezVousId, string? Raison = null, string? Commentaire = null);

public sealed class AnnulerEnLigneHandler(
    IRendezVousRepository rendezVous,
    Annulation annulation,
    IPerimetreUtilisateur perimetre,
    IUnitOfWork unitOfWork,
    TimeProvider horloge,
    ICurrentUser user) : ICommandHandler<AnnulerEnLigne, Unit>
{
    public static readonly TimeSpan DelaiMinimal = TimeSpan.FromHours(24);

    public async Task<Result<Unit>> HandleAsync(AnnulerEnLigne command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationReserver) is { } interdit)
        {
            return interdit;
        }

        var raison = Enumerations.Optionnel<RaisonAnnulation>(command.Raison, "Raison d'annulation");
        if (!raison.IsSuccess)
        {
            return raison.Error!;
        }

        if (command.Commentaire?.Trim().Length > RendezVous.LongueurMaxCommentaire)
        {
            return Error.Validation("rendez-vous.commentaire-trop-long",
                $"La précision de l'annulation compte au plus {RendezVous.LongueurMaxCommentaire} caractères.");
        }

        var rdv = await rendezVous.GetAsync(command.RendezVousId, cancellationToken);
        if (rdv is null || !Perimetre.PeutVoir(perimetre, rdv))
        {
            return RendezVousMapping.Inconnu();
        }

        if (rdv.Debut - horloge.GetUtcNow() < DelaiMinimal)
        {
            return Error.Conflict("rendez-vous.annulation-tardive", "Moins de 24 heures avant le rendez-vous, l'annulation se fait auprès du SEPP.");
        }

        var motif = perimetre.PersonneId is not null ? MotifAnnulation.DemandeTravailleur : MotifAnnulation.DemandeEmployeur;
        if (await annulation.AnnulerAsync(rdv, motif, cancellationToken, raison.Value, command.Commentaire) is { } erreur)
        {
            return erreur;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// POR-11, SAN-12 : déplacement atomique d'un rendez-vous vers un autre créneau ouvert à la réservation en ligne, du même
/// type d'acte. Un seul appel libère l'ancien créneau et réserve le nouveau (même transaction, même rendez-vous, mêmes
/// obligations couvertes) puis publie <c>RendezVousReplanifie</c> et une convocation de type <c>Replanification</c>.
/// Réservé au travailleur concerné (revendication <c>personne_id</c>) ou à l'employeur de l'affilié (<c>affilie_id</c>),
/// comme l'annulation. Garde-fous : délai minimal avant le rendez-vous et nombre maximal de déplacements (configuration,
/// à valider). Le concurrent qui perd la course sur le créneau reçoit un 409 (état du créneau, verrou optimiste ou contrainte
/// d'exclusion).
/// </summary>
public sealed record DeplacerEnLigne(Guid RendezVousId, Guid NouveauCreneauId);

public sealed class DeplacerEnLigneHandler(
    IRendezVousRepository rendezVous,
    ICreneauRepository creneaux,
    ILieuRepository lieux,
    PriseDeRendezVous prise,
    IIntegrationEventOutbox outbox,
    IPerimetreUtilisateur perimetre,
    ParametresPlanification parametres,
    IUnitOfWork unitOfWork,
    TimeProvider horloge,
    ICurrentUser user) : ICommandHandler<DeplacerEnLigne, RendezVousDto>
{
    public async Task<Result<RendezVousDto>> HandleAsync(DeplacerEnLigne command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationReserver) is { } interdit)
        {
            return interdit;
        }

        var rdv = await rendezVous.GetAsync(command.RendezVousId, cancellationToken);
        if (rdv is null || !Perimetre.PeutVoir(perimetre, rdv))
        {
            return RendezVousMapping.Inconnu();
        }

        var options = parametres.Options;
        var maintenant = horloge.GetUtcNow();
        if (rdv.Statut != StatutRendezVous.Planifie)
        {
            return Error.Conflict("rendez-vous.statut", $"Un rendez-vous {rdv.Statut} ne peut pas être déplacé.");
        }

        if (rdv.Debut - maintenant < options.DelaiMinimalDeplacement)
        {
            return Error.Conflict("rendez-vous.deplacement-tardif",
                $"Moins de {(int)options.DelaiMinimalDeplacement.TotalHours} heures avant le rendez-vous, le déplacement se fait auprès du SEPP.");
        }

        if (rdv.NombreDeplacements >= options.NombreMaximalDeplacements)
        {
            return Error.Conflict("rendez-vous.deplacements-epuises",
                $"Ce rendez-vous a déjà été déplacé {rdv.NombreDeplacements} fois : contactez le SEPP pour le déplacer à nouveau.");
        }

        var nouveau = await creneaux.GetAsync(command.NouveauCreneauId, cancellationToken);
        var lieu = nouveau is null ? null : await lieux.GetAsync(nouveau.LieuId, cancellationToken);
        if (nouveau is null || lieu is null || !nouveau.OuvertEnLigne || (lieu.AffilieId is { } chez && chez != rdv.AffilieId))
        {
            return Error.NotFound("creneau.inconnu", "Créneau inconnu ou non ouvert à la réservation en ligne.");
        }

        if (nouveau.Id == rdv.CreneauId || nouveau.TypeActe != rdv.TypeActe)
        {
            return Error.Validation("deplacement.creneau-invalide", "Le rendez-vous se déplace vers un autre créneau du même type d'acte.");
        }

        if (!nouveau.EstOuvertAuxReservations(maintenant, options.LiberationUrgence))
        {
            return Error.Conflict("creneau.indisponible", "Ce créneau n'est plus disponible.");
        }

        if (await prise.ChevaucheRendezVousDeLaPersonneAsync(rdv.PersonneId, nouveau.Debut, nouveau.Fin, rdv.Id, cancellationToken))
        {
            return Error.Conflict("rendez-vous.chevauchement", "La personne a déjà un rendez-vous à ce moment.");
        }

        var ancien = await creneaux.GetAsync(rdv.CreneauId, cancellationToken);
        if (ancien is null)
        {
            return Error.NotFound("creneau.inconnu", "Créneau du rendez-vous introuvable.");
        }

        DateTimeOffset ancienDebut;
        try
        {
            ancienDebut = rdv.DeplacerEnLigne(ancien, nouveau, maintenant, options.NombreMaximalDeplacements);
        }
        catch (DomainException ex)
        {
            return Error.Conflict("creneau.indisponible", ex.Message);
        }

        var motif = perimetre.PersonneId is not null ? MotifAnnulation.DemandeTravailleur : MotifAnnulation.DemandeEmployeur;
        outbox.Add(new RendezVousReplanifie(rdv.Id, rdv.PersonneId, rdv.AffilieId, ancienDebut, rdv.Debut, motif.ToString()));
        await prise.ConvoquerAsync(rdv, null, null, TypeConvocation.Replanification, null, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return rdv.ToDto(externe: true);
    }
}

/// <summary>
/// POR-11 : le travailleur confirme sa présence à un rendez-vous planifié à venir. La confirmation est enregistrée sur le
/// rendez-vous (<c>PresenceConfirmeeLe</c>) et visible du planificateur ; elle est idempotente. Aucun événement n'est publié :
/// aucun service n'en a l'usage (ni Communications ni Obligations). Réservée à la personne concernée — un employeur ne
/// confirme pas à la place de son travailleur.
/// </summary>
public sealed record ConfirmerPresence(Guid RendezVousId);

public sealed class ConfirmerPresenceHandler(
    IRendezVousRepository rendezVous,
    IPerimetreUtilisateur perimetre,
    IUnitOfWork unitOfWork,
    TimeProvider horloge,
    ICurrentUser user) : ICommandHandler<ConfirmerPresence, RendezVousDto>
{
    public async Task<Result<RendezVousDto>> HandleAsync(ConfirmerPresence command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationReserver) is { } interdit)
        {
            return interdit;
        }

        var rdv = await rendezVous.GetAsync(command.RendezVousId, cancellationToken);
        if (rdv is null || !Perimetre.PeutVoir(perimetre, rdv))
        {
            return RendezVousMapping.Inconnu();
        }

        if (perimetre.PersonneId != rdv.PersonneId)
        {
            return Error.Forbidden("rendez-vous.confirmation-travailleur", "Seul le travailleur concerné confirme sa présence.");
        }

        try
        {
            rdv.ConfirmerPresence(horloge.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Error.Conflict("rendez-vous.statut", ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return rdv.ToDto(externe: true);
    }
}
