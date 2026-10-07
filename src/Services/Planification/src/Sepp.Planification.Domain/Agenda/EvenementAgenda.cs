using Sepp.BuildingBlocks.Domain.Calendar;

namespace Sepp.Planification.Domain.Agenda;

/// <summary>
/// Événement écrit dans l'agenda Microsoft 365 / Google d'une ressource (PLA-09). Ne contient <b>aucune donnée
/// médicale</b> ni d'identité : ni type d'acte (un « examen de reprise » révèle une incapacité), ni identifiant de
/// personne ou d'obligation, ni affilié. Seule une référence courte du rendez-vous permet de le retrouver dans le SEPP.
/// </summary>
public sealed record EvenementAgenda(string Titre, string Description, string? Lieu, DateTimeOffset Debut, DateTimeOffset Fin)
{
    public const string TitreRendezVous = "Rendez-vous SEPP";

    public static EvenementAgenda Pour(RendezVous rendezVous, string? nomLieu) => new(
        TitreRendezVous,
        $"Réf. {ReferenceCourte(rendezVous.Id)} — détails dans l'application du SEPP.",
        nomLieu,
        rendezVous.Debut,
        rendezVous.Fin);

    /// <summary>Huit derniers caractères hexadécimaux de l'identifiant (la partie aléatoire de l'UUID v7).</summary>
    public static string ReferenceCourte(Guid id) => id.ToString("N")[^8..].ToUpperInvariant();
}

/// <summary>
/// PLA-06 : échéances légales des urgences (examen de reprise, consultation spontanée, pré-reprise), calculées en jours
/// ouvrables belges (DAT-08) : le jour de départ n'est pas compté, week-ends et jours fériés sont exclus.
/// </summary>
public static class DelaiLegal
{
    public static DateOnly Echeance(DateOnly depart, int joursOuvrables, BusinessCalendar calendrier) =>
        calendrier.AddBusinessDays(depart, joursOuvrables);

    /// <summary>Le rendez-vous a lieu au plus tard le jour de l'échéance (heure belge).</summary>
    public static bool RespecteEcheance(DateTimeOffset debutRendezVous, DateOnly echeance) => HeureBelge.Jour(debutRendezVous) <= echeance;
}
