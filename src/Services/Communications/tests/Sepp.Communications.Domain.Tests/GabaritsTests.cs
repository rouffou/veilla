using Sepp.BuildingBlocks.Domain;
using Sepp.Communications.Domain.Messages;

using Shouldly;

namespace Sepp.Communications.Domain.Tests;

/// <summary>DOC-03 : un e-mail ou un SMS ne contient jamais de donnée sensible.</summary>
public class GabaritsTests
{
    private const string Lien = "https://portail.exemple.test/messages/0198a000-0000-7000-8000-000000000001";
    private static readonly DateTimeOffset Debut = new(2026, 10, 12, 7, 30, 0, TimeSpan.Zero);

    private static readonly string[] MotsInterdits =
    [
        "santé", "gezondheid", "gesundheit", "médic", "medisch", "medizin", "examen", "onderzoek", "untersuchung", "psycho", "décision", "beslissing",
        "entscheidung", "apte", "geschikt", "tauglich", "document", "dossier", "rendez-vous", "afspraak", "termin", "convoqu", "oproeping", "einladung",
        "zone", "bris", "risque", "vaccin", "grossesse",
    ];

    public static TheoryData<TypeMessage, Canal, Language> EmailEtSms()
    {
        var data = new TheoryData<TypeMessage, Canal, Language>();
        foreach (var type in Enum.GetValues<TypeMessage>())
        {
            foreach (var canal in new[] { Canal.Email, Canal.Sms })
            {
                foreach (var langue in new[] { Language.Fr, Language.Nl, Language.De, Language.En })
                {
                    data.Add(type, canal, langue);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EmailEtSms))]
    public void Un_email_ou_un_sms_ne_porte_que_la_notification_generique_et_le_lien(TypeMessage type, Canal canal, Language langue)
    {
        var contenu = Gabarits.Construire(type, canal, langue, Debut, Lien);

        contenu.EstGenerique.ShouldBeTrue();
        var texte = $"{contenu.Sujet}\n{contenu.Corps}";
        texte.ShouldContain(Lien);
        foreach (var mot in MotsInterdits)
        {
            texte.ShouldNotContain(mot, Case.Insensitive);
        }

        // Aucune date ni heure de rendez-vous : le texte hors lien ne contient aucun chiffre.
        texte.Replace(Lien, string.Empty, StringComparison.Ordinal).Any(char.IsDigit).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(EmailEtSms))]
    public void Le_contenu_de_l_email_est_le_meme_quel_que_soit_le_type_de_message(TypeMessage type, Canal canal, Language langue)
    {
        var reference = Gabarits.Construire(TypeMessage.NotificationDocument, canal, langue, null, Lien);

        Gabarits.Construire(type, canal, langue, Debut, Lien).ShouldBe(reference);
    }

    [Fact]
    public void Un_sms_generique_tient_dans_un_sms()
    {
        foreach (var langue in new[] { Language.Fr, Language.Nl, Language.De })
        {
            Gabarits.Generique(Canal.Sms, langue, Lien).Corps.Length.ShouldBeLessThanOrEqualTo(Message.LongueurMaximaleSms);
        }
    }

    [Fact]
    public void Un_courrier_de_convocation_donne_la_date_en_heure_de_Bruxelles()
    {
        var contenu = Gabarits.Construire(TypeMessage.ConvocationRendezVous, Canal.Courrier, Language.Fr, Debut, Lien);

        contenu.EstGenerique.ShouldBeFalse();
        contenu.Corps.ShouldContain("lundi 12 octobre 2026 à 09:30");
        contenu.Corps.ShouldNotContain("07:30");
    }

    [Theory]
    [InlineData(Language.Nl, "maandag 12 oktober 2026 om 09:30")]
    [InlineData(Language.De, "Montag, 12. Oktober 2026 um 09:30 Uhr")]
    public void La_date_suit_la_langue_du_destinataire(Language langue, string attendu) =>
        Gabarits.Formater(Debut, langue).ShouldBe(attendu);

    [Fact]
    public void Un_message_d_annulation_sur_un_canal_securise_ne_donne_pas_de_date()
    {
        var contenu = Gabarits.Construire(TypeMessage.AnnulationRendezVous, Canal.Portail, Language.Fr, Debut, Lien);

        contenu.Corps.ShouldContain("annulé");
        contenu.Corps.ShouldNotContain("2026");
    }

    [Fact]
    public void Chaque_type_a_un_gabarit_pour_chaque_langue_sur_un_canal_securise()
    {
        foreach (var type in Enum.GetValues<TypeMessage>().Where(t => t != TypeMessage.Manuel))
        {
            foreach (var langue in new[] { Language.Fr, Language.Nl, Language.De })
            {
                var contenu = Gabarits.Construire(type, Canal.EBoxCitoyen, langue, Debut, Lien);

                contenu.Sujet.ShouldNotBeNullOrWhiteSpace();
                contenu.Corps.ShouldContain(Lien);
            }
        }
    }
}
