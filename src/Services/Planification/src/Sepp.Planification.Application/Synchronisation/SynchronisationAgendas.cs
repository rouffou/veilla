using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Planification.Application.Ressources;
using Sepp.Planification.Domain;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Ressources;

namespace Sepp.Planification.Application.Synchronisation;

public sealed record SynchronisationDto(int Ressources, int EvenementsEcrits, int EvenementsSupprimes, int OccupationsImportees, int CreneauxBloques, int RessourcesEnErreur);

/// <summary>
/// PLA-09 : synchronisation des agendas Microsoft 365 / Google des ressources humaines.
/// <list type="bullet">
/// <item><b>Écriture</b> : chaque rendez-vous actif ou terminé de la période est écrit dans l'agenda du conseiller sous
/// l'intitulé neutre « Rendez-vous SEPP » (<see cref="EvenementAgenda"/> : ni type d'acte, ni identité, ni affilié, ni
/// obligation) ; un rendez-vous annulé ou manqué en est retiré.</item>
/// <item><b>Lecture</b> : les occupations déjà présentes dans l'agenda externe (hors événements écrits par le SEPP) deviennent
/// des indisponibilités (<see cref="SourceAbsence.AgendaExterne"/>) qui bloquent les créneaux libres. Les occupations
/// connues sont actualisées sans doublon (clé : ressource + référence de l'événement).</item>
/// </list>
/// Un agenda injoignable est compté en erreur sans empêcher la synchronisation des autres ressources.
/// </summary>
public sealed class SynchronisationAgendas(
    IRessourceRepository ressources,
    IRendezVousRepository rendezVous,
    ILieuRepository lieux,
    IAbsenceRepository absences,
    IAgendaExterne agendaExterne,
    Indisponibilites indisponibilites,
    IUnitOfWork unitOfWork)
{
    public async Task<SynchronisationDto> ExecuterAsync(DateOnly du, DateOnly au, CancellationToken cancellationToken)
    {
        var debut = HeureBelge.VersUtc(du, TimeOnly.MinValue);
        var fin = HeureBelge.VersUtc(au.AddDays(1), TimeOnly.MinValue);
        var noms = (await lieux.ListAsync(cancellationToken)).ToDictionary(l => l.Id, l => l.Nom);
        var concernees = (await ressources.ListAsync(null, cancellationToken))
            .Where(r => r is { Active: true, FournisseurAgenda: not null, CompteAgenda: not null })
            .ToList();

        int ecrits = 0, supprimes = 0, importees = 0, bloques = 0, enErreur = 0;
        foreach (var ressource in concernees)
        {
            try
            {
                var fournisseur = ressource.FournisseurAgenda!.Value;
                var compte = ressource.CompteAgenda!;
                var rdvs = await rendezVous.RechercherAsync(new CritereRendezVous { RessourceId = ressource.Id, Du = debut, Au = fin }, cancellationToken);
                foreach (var rdv in rdvs.OrderBy(r => r.Debut))
                {
                    if (rdv.Statut is StatutRendezVous.Annule or StatutRendezVous.Absent)
                    {
                        if (rdv.ReferenceAgendaExterne is { } reference)
                        {
                            await agendaExterne.SupprimerAsync(fournisseur, compte, reference, cancellationToken);
                            rdv.LierAgendaExterne(null);
                            supprimes++;
                        }

                        continue;
                    }

                    var evenement = EvenementAgenda.Pour(rdv, noms.GetValueOrDefault(rdv.LieuId));
                    rdv.LierAgendaExterne(await agendaExterne.EcrireAsync(fournisseur, compte, rdv.ReferenceAgendaExterne, evenement, cancellationToken));
                    ecrits++;
                }

                var ecrites = rdvs.Select(r => r.ReferenceAgendaExterne).OfType<string>().ToHashSet(StringComparer.Ordinal);
                foreach (var occupation in await agendaExterne.LireOccupationsAsync(fournisseur, compte, debut, fin, cancellationToken))
                {
                    if (ecrites.Contains(occupation.Reference) || occupation.Fin <= occupation.Debut)
                    {
                        continue;
                    }

                    var cle = $"{ressource.Id:N}:{occupation.Reference}";
                    var existante = await absences.GetParReferenceAsync(SourceAbsence.AgendaExterne, cle, cancellationToken);
                    if (existante is null)
                    {
                        absences.Add(Absence.Creer(ressource.Id, occupation.Debut, occupation.Fin, SourceAbsence.AgendaExterne, cle));
                    }
                    else if (!existante.Actualiser(occupation.Debut, occupation.Fin))
                    {
                        continue;
                    }

                    importees++;
                    bloques += (await indisponibilites.BloquerAsync(ressource.Id, occupation.Debut, occupation.Fin, cancellationToken)).Bloques;
                }
            }
            catch (AgendaExterneIndisponibleException)
            {
                enErreur++;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new SynchronisationDto(concernees.Count, ecrits, supprimes, importees, bloques, enErreur);
    }
}

/// <summary>PLA-09 : synchronisation à la demande des agendas externes sur une période (92 jours au plus).</summary>
public sealed record SynchroniserAgendas(DateOnly Du, DateOnly Au);

public sealed class SynchroniserAgendasHandler(SynchronisationAgendas synchronisation, ICurrentUser user) : ICommandHandler<SynchroniserAgendas, SynchronisationDto>
{
    public async Task<Result<SynchronisationDto>> HandleAsync(SynchroniserAgendas command, CancellationToken cancellationToken)
    {
        if (Acces.Exiger(user, Permissions.PlanificationRessources) is { } interdit)
        {
            return interdit;
        }

        if (command.Au < command.Du || command.Au.DayNumber - command.Du.DayNumber > 92)
        {
            return Error.Validation("synchronisation.periode-invalide", "La synchronisation couvre au plus 92 jours.");
        }

        return await synchronisation.ExecuterAsync(command.Du, command.Au, cancellationToken);
    }
}
