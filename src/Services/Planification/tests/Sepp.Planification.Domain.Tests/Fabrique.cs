using Sepp.Planification.Domain;
using Sepp.Planification.Domain.Agenda;

namespace Sepp.Planification.Domain.Tests;

/// <summary>Données de test : un lundi d'hiver ouvrable, sans jour férié autour (heure belge = UTC+1).</summary>
internal static class Fabrique
{
    public static readonly Guid Conseiller = Guid.CreateVersion7();
    public static readonly Guid Lieu = Guid.CreateVersion7();
    public static readonly Guid Personne = Guid.CreateVersion7();
    public static readonly Guid Affilie = Guid.CreateVersion7();

    /// <summary>Lundi 15 mars 2027.</summary>
    public static readonly DateOnly Lundi = new(2027, 3, 15);

    /// <summary>Instant de référence : trois semaines avant le lundi de test.</summary>
    public static DateTimeOffset Maintenant => Instant(Lundi.AddDays(-21), 10);

    public static DateTimeOffset Instant(DateOnly jour, int heure, int minute = 0) => HeureBelge.VersUtc(jour, new TimeOnly(heure, minute));

    public static Creneau Creneau(DateOnly? jour = null, int heure = 9, int minutes = 30, string typeActe = "EVALUATION_PERIODIQUE", bool urgence = false,
        bool enLigne = false, IEnumerable<Guid>? associees = null, Guid? ressource = null)
    {
        var debut = Instant(jour ?? Lundi, heure);
        return Agenda.Creneau.Creer(ressource ?? Conseiller, Lieu, debut, debut.AddMinutes(minutes), typeActe, urgence, enLigne, associees);
    }

    public static RendezVous RendezVous(Creneau? creneau = null, DateTimeOffset? maintenant = null, IEnumerable<Guid>? obligations = null) =>
        Agenda.RendezVous.Planifier(creneau ?? Creneau(), Personne, Affilie, obligations ?? [Guid.CreateVersion7()], OrigineRendezVous.Planificateur, false,
            maintenant ?? Maintenant);
}
