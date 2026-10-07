using System.Globalization;

using Sepp.BuildingBlocks.Domain;

namespace Sepp.Communications.Domain.Messages;

/// <summary>
/// Gabarits des messages (DOC-03), en français, néerlandais et allemand. Règle centrale : l'e-mail et le SMS ne reçoivent
/// <b>jamais</b> que la notification générique et le lien vers l'espace sécurisé, quel que soit le type de message : ni
/// type d'examen, ni nature du document, ni zone, ni date de rendez-vous. Seuls les canaux authentifiés (portail, eBox) ou
/// nominatifs (courrier, recommandé) portent un contenu détaillé, et celui-ci ne reprend que ce que l'événement
/// d'origine transporte (date de rendez-vous), jamais de donnée de santé (ARC-06).
/// </summary>
public static class Gabarits
{
    private static readonly TimeZoneInfo Bruxelles = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    /// <summary>Contenu d'un message pour un canal et une langue ; <paramref name="debut"/> est le début du rendez-vous, s'il y en a un.</summary>
    public static ContenuMessage Construire(TypeMessage type, Canal canal, Language langue, DateTimeOffset? debut, string lien)
    {
        // Canaux non sécurisés : notification générique unique.
        if (!canal.PeutPorterUnContenuDetaille())
        {
            return Generique(canal, langue, lien);
        }

        var date = debut is { } d ? Formater(d, langue) : null;
        var l = Textes(langue);
        return type switch
        {
            TypeMessage.NotificationDocument => Detaille(l.SujetDocument, $"{l.CorpsDocument} {lien}"),
            TypeMessage.ConvocationRendezVous when date is not null => Detaille(l.SujetConvocation, $"{string.Format(CultureInfo.InvariantCulture, l.CorpsConvocation, date)} {l.Detail} {lien}"),
            TypeMessage.ConvocationRendezVous => Detaille(l.SujetConvocation, $"{l.CorpsConvocationSansDate} {l.Detail} {lien}"),
            TypeMessage.RappelRendezVous when date is not null => Detaille(l.SujetRappel, $"{string.Format(CultureInfo.InvariantCulture, l.CorpsRappel, date)} {l.Detail} {lien}"),
            TypeMessage.RappelRendezVous => Detaille(l.SujetRappel, $"{l.CorpsRappelSansDate} {l.Detail} {lien}"),
            TypeMessage.AnnulationRendezVous => Detaille(l.SujetAnnulation, $"{l.CorpsAnnulation} {lien}"),
            TypeMessage.AlerteBrisDeGlace => Detaille(l.SujetAlerte, $"{l.CorpsAlerte} {lien}"),
            _ => Generique(canal, langue, lien),
        };
    }

    /// <summary>Notification générique : seule forme admise pour l'e-mail et le SMS.</summary>
    public static ContenuMessage Generique(Canal canal, Language langue, string lien)
    {
        var l = Textes(langue);
        return canal == Canal.Sms
            ? new ContenuMessage(l.SujetGenerique, $"{l.Sms} {lien}", EstGenerique: true)
            : new ContenuMessage(l.SujetGenerique, $"{l.CorpsGenerique}\n{lien}\n\n{l.PiedGenerique}", EstGenerique: true);
    }

    /// <summary>Date et heure en heure de Bruxelles, dans la langue du destinataire.</summary>
    public static string Formater(DateTimeOffset debut, Language langue)
    {
        var local = TimeZoneInfo.ConvertTime(debut, Bruxelles);
        var culture = CultureInfo.GetCultureInfo(langue switch { Language.Nl => "nl-BE", Language.De => "de-BE", _ => "fr-BE" });
        var jour = local.ToString(langue == Language.De ? "dddd, d. MMMM yyyy" : "dddd d MMMM yyyy", culture);
        var heure = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        return langue switch
        {
            Language.Nl => $"{jour} om {heure}",
            Language.De => $"{jour} um {heure} Uhr",
            _ => $"{jour} à {heure}",
        };
    }

    private static ContenuMessage Detaille(string sujet, string corps) => new(sujet, corps, EstGenerique: false);

    private sealed record Libelles(
        string SujetGenerique, string CorpsGenerique, string PiedGenerique, string Sms,
        string SujetDocument, string CorpsDocument,
        string SujetConvocation, string CorpsConvocation, string CorpsConvocationSansDate,
        string SujetRappel, string CorpsRappel, string CorpsRappelSansDate,
        string SujetAnnulation, string CorpsAnnulation,
        string SujetAlerte, string CorpsAlerte,
        string Detail);

    private static Libelles Textes(Language langue) => langue switch
    {
        Language.Nl => new(
            "Nieuw bericht", "U hebt een nieuw bericht ontvangen. Log in op uw beveiligde omgeving om het te raadplegen:",
            "Dit bericht is een eenvoudige melding en bevat geen persoonlijke informatie. Gelieve niet te antwoorden.",
            "Nieuw bericht beschikbaar. Log in:",
            "Nieuw document beschikbaar", "Een nieuw document staat voor u klaar in uw beveiligde omgeving:",
            "Oproeping voor een afspraak", "U bent opgeroepen voor een afspraak op {0}.", "U bent opgeroepen voor een afspraak.",
            "Herinnering aan uw afspraak", "Herinnering: u hebt een afspraak op {0}.", "Herinnering: u hebt een afspraak.",
            "Annulering van een afspraak", "Uw afspraak is geannuleerd. De details vindt u hier:",
            "Beveiligingsmelding", "Een uitzonderlijke toegang (noodtoegang) werd geregistreerd. Raadpleeg het auditlogboek:",
            "De details van de afspraak vindt u in uw beveiligde omgeving:"),
        Language.De => new(
            "Neue Nachricht", "Sie haben eine neue Nachricht erhalten. Melden Sie sich in Ihrem geschützten Bereich an, um sie einzusehen:",
            "Diese Nachricht ist eine einfache Benachrichtigung und enthält keine persönlichen Angaben. Bitte nicht antworten.",
            "Neue Nachricht verfügbar. Anmelden:",
            "Neues Dokument verfügbar", "Ein neues Dokument steht in Ihrem geschützten Bereich bereit:",
            "Einladung zu einem Termin", "Sie sind zu einem Termin am {0} eingeladen.", "Sie sind zu einem Termin eingeladen.",
            "Erinnerung an Ihren Termin", "Erinnerung: Sie haben einen Termin am {0}.", "Erinnerung: Sie haben einen Termin.",
            "Absage eines Termins", "Ihr Termin wurde abgesagt. Die Einzelheiten finden Sie hier:",
            "Sicherheitsmeldung", "Ein Ausnahmezugriff (Notfallzugriff) wurde protokolliert. Sehen Sie das Auditprotokoll ein:",
            "Die Einzelheiten des Termins finden Sie in Ihrem geschützten Bereich:"),
        _ => new(
            "Nouveau message", "Vous avez reçu un nouveau message. Connectez-vous à votre espace sécurisé pour le consulter :",
            "Ce message est une simple notification : il ne contient aucune information personnelle. Merci de ne pas y répondre.",
            "Nouveau message disponible. Connectez-vous :",
            "Nouveau document disponible", "Un nouveau document est disponible dans votre espace sécurisé :",
            "Convocation à un rendez-vous", "Vous êtes convoqué(e) à un rendez-vous le {0}.", "Vous êtes convoqué(e) à un rendez-vous.",
            "Rappel de votre rendez-vous", "Rappel : vous avez rendez-vous le {0}.", "Rappel : vous avez un rendez-vous.",
            "Annulation d'un rendez-vous", "Votre rendez-vous a été annulé. Le détail est disponible ici :",
            "Alerte de sécurité", "Un accès exceptionnel (bris de glace) a été journalisé. Consultez le journal d'audit :",
            "Le détail du rendez-vous est disponible dans votre espace sécurisé :"),
    };
}
