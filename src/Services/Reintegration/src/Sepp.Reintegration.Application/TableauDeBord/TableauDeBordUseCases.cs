using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.Reintegration.Domain;

namespace Sepp.Reintegration.Application.TableauDeBord;

/// <summary>Ligne du tableau de bord : une échéance légale d'un dossier ou d'un trajet (identifiants, dates, statuts).</summary>
public sealed record LigneDelaiDto(
    Guid IncapaciteId, Guid? TrajetId, Guid PersonneId, Guid AffilieId, TypeEcheance Type, DateOnly Date, StatutEcheance Statut, DateOnly DebutAlerte,
    string BaseLegale, bool AlerteEmise);

public sealed record TableauDeBordDto(DateOnly Au, int Depassees, int Proches, int AVenir, IReadOnlyList<LigneDelaiDto> Lignes);

/// <summary>
/// SAN-65 : tableau de bord des délais légaux de réintégration, avec alerte avant dépassement (<c>Proche</c> : N jours ouvrables
/// avant l'échéance, réglage <c>Reintegration:Suivi:AlerteAvantEcheanceJoursOuvrables</c>) et dépassements. Réservé à la zone
/// médicale (l'existence d'une incapacité est une donnée de santé) ; chaque dossier affiché est tracé. Les échéances respectées
/// sont omises sauf demande explicite.
/// </summary>
public sealed record ObtenirTableauDeBord(Guid? AffilieId, StatutEcheance? Statut, bool InclureRespectees = false);

public sealed class ObtenirTableauDeBordHandler(
    GardeReintegration garde, IIncapaciteRepository incapacites, ITrajetRepository trajets, ParametresReintegration parametres, TimeProvider horloge)
    : IQueryHandler<ObtenirTableauDeBord, TableauDeBordDto>
{
    public const int Maximum = 1000;

    public async Task<Result<TableauDeBordDto>> HandleAsync(ObtenirTableauDeBord query, CancellationToken cancellationToken)
    {
        if (garde.VerifierPermission(ActionAudit.Lecture) is { } interdit)
        {
            return interdit;
        }

        var aujourdHui = horloge.Aujourdhui();
        var lignes = new List<LigneDelaiDto>();
        foreach (var i in await incapacites.ListerEnCoursAsync(query.AffilieId, Maximum, cancellationToken))
        {
            var politique = await parametres.PolitiqueAsync(i.DateDebut, cancellationToken);
            lignes.AddRange(i.Echeances(politique).Select(e => Ligne(i, i.Id, null, i.PersonneId, i.AffilieId, e, politique, aujourdHui)));
        }

        foreach (var t in await trajets.ListerOuvertsAsync(query.AffilieId, Maximum, cancellationToken))
        {
            var politique = await parametres.PolitiqueAsync(t.DateDemande, cancellationToken);
            lignes.AddRange(t.Echeances(politique).Select(e => Ligne(t, t.IncapaciteId, t.Id, t.PersonneId, t.AffilieId, e, politique, aujourdHui)));
        }

        var retenues = lignes
            .Where(l => (query.InclureRespectees || l.Statut != StatutEcheance.Respectee) && (query.Statut is null || l.Statut == query.Statut))
            .OrderBy(l => l.Statut switch { StatutEcheance.Depassee => 0, StatutEcheance.Proche => 1, StatutEcheance.AVenir => 2, _ => 3 })
            .ThenBy(l => l.Date)
            .ToList();
        foreach (var incapaciteId in retenues.Select(l => l.IncapaciteId).Distinct())
        {
            garde.Journaliser(ActionAudit.Lecture, PartiesDossier.TableauDeBord, incapaciteId);
        }

        await garde.ValiderTracesAsync(cancellationToken);
        return new TableauDeBordDto(
            aujourdHui,
            retenues.Count(l => l.Statut == StatutEcheance.Depassee),
            retenues.Count(l => l.Statut == StatutEcheance.Proche),
            retenues.Count(l => l.Statut == StatutEcheance.AVenir),
            retenues);
    }

    private static LigneDelaiDto Ligne(
        ProcessusAEcheances processus, Guid incapaciteId, Guid? trajetId, Guid personneId, Guid affilieId, Echeance e, PolitiqueReintegration politique, DateOnly aujourdHui) =>
        new(incapaciteId, trajetId, personneId, affilieId, e.Type, e.Date, e.StatutAu(aujourdHui, politique), e.DebutAlerte(politique), e.BaseLegale,
            processus.AlerteDeclenchee(e, StatutEcheance.Proche) || processus.AlerteDeclenchee(e, StatutEcheance.Depassee));
}
