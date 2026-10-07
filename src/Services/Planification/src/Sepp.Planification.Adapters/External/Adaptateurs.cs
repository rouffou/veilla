using System.Collections.Concurrent;

using Sepp.Planification.Application;
using Sepp.Planification.Domain.Agenda;
using Sepp.Planification.Domain.Ressources;

namespace Sepp.Planification.Adapters.External;

// Adaptateurs des systèmes externes du service Planification : outil RH (PLA-03, lecture seule) et agendas Microsoft 365 /
// Google (PLA-09). L'outil RH réel n'est pas encore identifié et les inscriptions d'application (Microsoft Entra,
// Google Cloud) restent à obtenir : les adaptateurs « Reel » sont des squelettes qui documentent ce qu'il faut obtenir ;
// les simulateurs produisent des données fictives pour le développement, la démonstration et les tests.
// Sélection par configuration : Planification:Adaptateurs:OutilRh et Planification:Adaptateurs:AgendaExterne = Simulateur | Reel.

public static class DocumentationAObtenir
{
    public const string OutilRh =
        "Adaptateur de l'outil RH non implémenté (PLA-03) : identifier l'outil RH utilisé par le SEPP et obtenir un accès en " +
        "lecture seule (export fichier CSV planifié, ou API de consultation des absences), puis traduire les congés vers CongeRh " +
        "(référence du congé, référence de la ressource, période). Rien n'est jamais écrit vers l'outil RH.";

    public const string AgendaExterne =
        "Adaptateur d'agenda externe non implémenté (PLA-09) : Microsoft 365 requiert une inscription d'application Microsoft Entra " +
        "(autorisation d'application Calendars.ReadWrite limitée aux boîtes des conseillers, consentement de l'administrateur) ; " +
        "Google requiert un compte de service avec délégation à l'échelle du domaine (portée calendar). Secrets dans Azure Key Vault (CTR-16).";
}

/// <summary>Squelette de l'outil RH réel (PLA-03).</summary>
public sealed class OutilRhAdaptateurReel : IOutilRh
{
    public Task<IReadOnlyList<CongeRh>> LireCongesAsync(DateOnly du, DateOnly au, CancellationToken cancellationToken) =>
        throw new NotImplementedException(DocumentationAObtenir.OutilRh);
}

/// <summary>Squelette des agendas externes réels (PLA-09) : signale l'indisponibilité plutôt que d'échouer en erreur serveur.</summary>
public sealed class AgendaExterneAdaptateurReel : IAgendaExterne
{
    public Task<string> EcrireAsync(FournisseurAgenda fournisseur, string compte, string? reference, EvenementAgenda evenement, CancellationToken cancellationToken) =>
        throw new AgendaExterneIndisponibleException(DocumentationAObtenir.AgendaExterne);

    public Task SupprimerAsync(FournisseurAgenda fournisseur, string compte, string reference, CancellationToken cancellationToken) =>
        throw new AgendaExterneIndisponibleException(DocumentationAObtenir.AgendaExterne);

    public Task<IReadOnlyList<OccupationExterne>> LireOccupationsAsync(FournisseurAgenda fournisseur, string compte, DateTimeOffset du, DateTimeOffset au,
        CancellationToken cancellationToken) =>
        throw new AgendaExterneIndisponibleException(DocumentationAObtenir.AgendaExterne);
}

/// <summary>Congé fictif de la configuration <c>Planification:Simulateurs:Conges</c>.</summary>
public sealed class CongeSimule
{
    public string ReferenceExterne { get; set; } = string.Empty;

    public string ReferenceRessource { get; set; } = string.Empty;

    public DateTimeOffset Debut { get; set; }

    public DateTimeOffset Fin { get; set; }
}

/// <summary>Simulateur de l'outil RH : congés fictifs lus dans la configuration, ou ajoutés par les tests.</summary>
public sealed class SimulateurOutilRh : IOutilRh
{
    private readonly ConcurrentQueue<CongeRh> _conges = new();

    public SimulateurOutilRh(IEnumerable<CongeSimule>? conges = null)
    {
        foreach (var conge in conges ?? [])
        {
            Ajouter(new CongeRh(conge.ReferenceExterne, conge.ReferenceRessource, conge.Debut, conge.Fin));
        }
    }

    public void Ajouter(CongeRh conge) => _conges.Enqueue(conge);

    public Task<IReadOnlyList<CongeRh>> LireCongesAsync(DateOnly du, DateOnly au, CancellationToken cancellationToken)
    {
        var debut = Domain.HeureBelge.VersUtc(du, TimeOnly.MinValue);
        var fin = Domain.HeureBelge.VersUtc(au.AddDays(1), TimeOnly.MinValue);
        IReadOnlyList<CongeRh> resultat = _conges.Where(c => c.Debut < fin && c.Fin > debut).ToList();
        return Task.FromResult(resultat);
    }
}

/// <summary>
/// Simulateur d'agenda Microsoft 365 / Google : événements en mémoire par compte, conservés pour inspection (les tests
/// vérifient que les intitulés ne contiennent aucune donnée médicale), et occupations fictives à lire.
/// </summary>
public sealed class SimulateurAgendaExterne : IAgendaExterne
{
    private readonly ConcurrentDictionary<(FournisseurAgenda Fournisseur, string Compte), ConcurrentDictionary<string, EvenementAgenda>> _agendas = new();
    private readonly ConcurrentDictionary<(FournisseurAgenda Fournisseur, string Compte), ConcurrentQueue<OccupationExterne>> _occupations = new();

    /// <summary>Événements écrits dans l'agenda d'un compte.</summary>
    public IReadOnlyDictionary<string, EvenementAgenda> Evenements(FournisseurAgenda fournisseur, string compte) =>
        Agenda(fournisseur, compte).ToDictionary(e => e.Key, e => e.Value);

    /// <summary>Tous les événements de tous les agendas simulés.</summary>
    public IReadOnlyList<EvenementAgenda> ToutesLesEvenements() => _agendas.Values.SelectMany(a => a.Values).ToList();

    /// <summary>Ajoute une occupation que le planificateur ne connaît pas (réunion, congé saisi dans l'agenda).</summary>
    public void AjouterOccupation(FournisseurAgenda fournisseur, string compte, OccupationExterne occupation) =>
        _occupations.GetOrAdd((fournisseur, compte), _ => new ConcurrentQueue<OccupationExterne>()).Enqueue(occupation);

    public Task<string> EcrireAsync(FournisseurAgenda fournisseur, string compte, string? reference, EvenementAgenda evenement, CancellationToken cancellationToken)
    {
        var cle = string.IsNullOrWhiteSpace(reference) ? $"sim-{Guid.CreateVersion7():N}" : reference;
        Agenda(fournisseur, compte)[cle] = evenement;
        return Task.FromResult(cle);
    }

    public Task SupprimerAsync(FournisseurAgenda fournisseur, string compte, string reference, CancellationToken cancellationToken)
    {
        Agenda(fournisseur, compte).TryRemove(reference, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OccupationExterne>> LireOccupationsAsync(FournisseurAgenda fournisseur, string compte, DateTimeOffset du, DateTimeOffset au,
        CancellationToken cancellationToken)
    {
        var fictives = _occupations.TryGetValue((fournisseur, compte), out var file) ? file.ToList() : [];
        var ecrites = Agenda(fournisseur, compte)
            .Select(e => new OccupationExterne(e.Key, e.Value.Debut, e.Value.Fin));
        IReadOnlyList<OccupationExterne> resultat = fictives.Concat(ecrites).Where(o => o.Debut < au && o.Fin > du).ToList();
        return Task.FromResult(resultat);
    }

    private ConcurrentDictionary<string, EvenementAgenda> Agenda(FournisseurAgenda fournisseur, string compte) =>
        _agendas.GetOrAdd((fournisseur, compte), _ => new ConcurrentDictionary<string, EvenementAgenda>(StringComparer.Ordinal));
}
