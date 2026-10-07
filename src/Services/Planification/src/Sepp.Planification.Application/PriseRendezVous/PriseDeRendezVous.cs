using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Planification;
using Sepp.Planification.Domain;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Convocations;

namespace Sepp.Planification.Application.PriseRendezVous;

/// <summary>Demande de rendez-vous dans un créneau donné.</summary>
public sealed record DemandeRendezVous(
    Guid PersonneId,
    Guid AffilieId,
    IReadOnlyList<Guid> ObligationIds,
    OrigineRendezVous Origine,
    bool Urgent)
{
    public bool Convoquer { get; init; } = true;

    public CanalConvocation? Canal { get; init; }

    public bool? Recommande { get; init; }

    public TypeConvocation TypeConvocation { get; init; } = TypeConvocation.Convocation;

    public Guid? LotId { get; init; }

    public Guid? ReconvocationDeId { get; init; }
}

/// <summary>Recherche d'un créneau libre pour un type d'acte, entre deux instants.</summary>
public sealed record RechercheCreneau(string TypeActe, DateTimeOffset Du, DateTimeOffset Au, bool Urgent)
{
    public Guid? LieuId { get; init; }

    /// <summary>Ressource à éviter (conseiller absent, PLA-07).</summary>
    public Guid? SaufRessource { get; init; }

    /// <summary>Personne à convoquer : ses rendez-vous actifs ne doivent pas chevaucher le créneau.</summary>
    public Guid? PersonneId { get; init; }

    /// <summary>Rendez-vous de la personne à ignorer dans le contrôle de chevauchement (celui qu'on déplace).</summary>
    public Guid? SaufRendezVousId { get; init; }
}

/// <summary>
/// Cœur commun de la prise de rendez-vous : réservation du créneau, couverture des obligations (SAN-03), convocation
/// (SAN-10, SAN-11) et événements d'intégration (outbox, ARC-32). N'enregistre pas : le cas d'usage appelant valide la
/// transaction.
/// </summary>
public sealed class PriseDeRendezVous(
    IRendezVousRepository rendezVous,
    ICreneauRepository creneaux,
    IRessourceRepository ressources,
    IObligationRepository obligations,
    IConvocationRepository convocations,
    IPreferenceConvocationRepository preferences,
    IIntegrationEventOutbox outbox,
    ParametresPlanification parametres,
    TimeProvider horloge)
{
    public async Task<Result<RendezVous>> PlanifierAsync(Creneau creneau, DemandeRendezVous demande, CancellationToken cancellationToken)
    {
        if (await ChevaucheRendezVousDeLaPersonneAsync(demande.PersonneId, creneau.Debut, creneau.Fin, null, cancellationToken))
        {
            return Error.Conflict("rendez-vous.chevauchement", "La personne a déjà un rendez-vous à ce moment.");
        }

        RendezVous rdv;
        try
        {
            rdv = RendezVous.Planifier(creneau, demande.PersonneId, demande.AffilieId, demande.ObligationIds, demande.Origine,
                demande.Urgent, horloge.GetUtcNow(), demande.ReconvocationDeId);
        }
        catch (DomainException ex)
        {
            return Error.Conflict("creneau.indisponible", ex.Message);
        }

        rendezVous.Add(rdv);
        foreach (var obligation in await obligations.ListAsync(rdv.ObligationIds, cancellationToken))
        {
            obligation.Couvrir(rdv.Id);
        }

        outbox.Add(new RendezVousPlanifie(rdv.Id, rdv.PersonneId, rdv.AffilieId, rdv.Debut, rdv.ObligationIds));
        if (demande.Convoquer)
        {
            await ConvoquerAsync(rdv, demande.Canal, demande.Recommande, demande.TypeConvocation, demande.LotId, cancellationToken);
        }

        return rdv;
    }

    /// <summary>SAN-10, SAN-11 : crée la convocation (canal résolu, recommandé si la loi l'exige) et publie l'événement d'envoi.</summary>
    public async Task<Convocation> ConvoquerAsync(RendezVous rdv, CanalConvocation? canal, bool? recommande, TypeConvocation type,
        Guid? lotId, CancellationToken cancellationToken)
    {
        var estRecommande = recommande ?? parametres.ExigeRecommande(rdv.TypeActe);
        var preference = await preferences.GetAsync(rdv.AffilieId, cancellationToken);
        var canalRetenu = PolitiqueCanal.Resoudre(canal, preference?.Canal, parametres.Options.CanalParDefaut, estRecommande);
        var convocation = Convocation.Emettre(rdv, canalRetenu, estRecommande, type, lotId, horloge.GetUtcNow());
        convocations.Add(convocation);
        outbox.Add(new ConvocationEmise(convocation.Id, rdv.Id, rdv.PersonneId, rdv.AffilieId, rdv.LieuId, rdv.TypeActe, rdv.Debut,
            convocation.Canal.ToString(), convocation.Recommande, convocation.Type.ToString(), lotId));
        return convocation;
    }

    /// <summary>
    /// Premier créneau libre convenant à la recherche, au plus tôt. Une urgence accepte les créneaux du même type d'acte et
    /// les créneaux réservés aux urgences dont la ressource a la compétence ; à horaire égal, le créneau d'urgence est
    /// préféré. Une demande ordinaire n'accepte que les créneaux ouverts aux réservations (urgences libérées comprises).
    /// </summary>
    public async Task<Creneau?> TrouverCreneauAsync(RechercheCreneau recherche, CancellationToken cancellationToken)
    {
        var maintenant = horloge.GetUtcNow();
        var du = recherche.Du > maintenant ? recherche.Du : maintenant;
        var candidats = await creneaux.RechercherAsync(
            new CritereCreneaux(du, recherche.Au)
            {
                Statut = StatutCreneau.Libre,
                LieuId = recherche.LieuId,
                TypeActe = recherche.Urgent ? null : recherche.TypeActe,
            },
            cancellationToken);

        var competences = new Dictionary<Guid, bool>();
        foreach (var creneau in candidats
                     .Where(c => c.Debut > maintenant && c.Statut == StatutCreneau.Libre)
                     .Where(c => recherche.SaufRessource is not { } exclue || !c.Mobilise(exclue))
                     .OrderBy(c => c.Debut)
                     .ThenByDescending(c => c.ReserveUrgence)
                     .ThenBy(c => c.Id))
        {
            if (recherche.Urgent)
            {
                if (creneau.TypeActe != recherche.TypeActe)
                {
                    if (!creneau.ReserveUrgence)
                    {
                        continue;
                    }

                    if (!competences.TryGetValue(creneau.RessourceId, out var competent))
                    {
                        competent = (await ressources.GetAsync(creneau.RessourceId, cancellationToken))?.PossedeCompetence(recherche.TypeActe) == true;
                        competences[creneau.RessourceId] = competent;
                    }

                    if (!competent)
                    {
                        continue;
                    }
                }
            }
            else if (!creneau.EstOuvertAuxReservations(maintenant, parametres.Options.LiberationUrgence))
            {
                continue;
            }

            if (recherche.PersonneId is { } personne &&
                await ChevaucheRendezVousDeLaPersonneAsync(personne, creneau.Debut, creneau.Fin, recherche.SaufRendezVousId, cancellationToken))
            {
                continue;
            }

            return creneau;
        }

        return null;
    }

    public async Task<bool> ChevaucheRendezVousDeLaPersonneAsync(Guid personneId, DateTimeOffset debut, DateTimeOffset fin, Guid? sauf,
        CancellationToken cancellationToken)
    {
        var existants = await rendezVous.RechercherAsync(
            new CritereRendezVous
            {
                PersonneId = personneId,
                Du = debut.AddDays(-1),
                Au = fin,
                Statuts = RendezVous.StatutsActifs,
            },
            cancellationToken);
        return existants.Any(r => r.Id != sauf && r.Debut < fin && debut < r.Fin);
    }
}
