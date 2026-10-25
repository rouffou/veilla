using System.Text.RegularExpressions;

using Shouldly;

namespace Sepp.BuildingBlocks.Tests;

/// <summary>
/// ARC-21 : aucune durée légale n'est codée en dur dans le domaine ni l'application d'un service. Un délai légal vient d'un
/// paramètre du service Référentiels (reçu par <c>ParametreLegalModifie</c>) ou, à défaut, de sa valeur initiale dans
/// <c>ParametresLegauxParDefaut</c> (Sepp.Contracts).
/// <para>
/// Règle de détection, appliquée au code source de <c>src/Services/*/src/*.Domain</c> et <c>*.Application</c> (commentaires et
/// chaînes retirés), hors tests :
/// </para>
/// <list type="number">
/// <item><c>TimeSpan.FromDays(n)</c> avec un littéral entier ;</item>
/// <item><c>.AddDays(n)</c>, <c>.AddMonths(n)</c>, <c>.AddYears(n)</c> avec un littéral entier de valeur absolue au moins 2
/// (<c>AddDays(1)</c> et <c>AddDays(-1)</c> sont des bornes de journée, jamais un délai) ;</item>
/// <item>un membre dont le nom contient Jours, Delai(s), Semaines, Mois, Annees ou Duree, initialisé par un littéral entier
/// d'au moins 2 (propriété, champ, constante ou paramètre par défaut).</item>
/// </list>
/// Une occurrence légitime (réglage technique ou d'organisation, constante de conversion d'unité, constante légale hors
/// paramétrage) se déclare dans <see cref="Exceptions"/> avec sa justification : l'exception est liée au fichier et à l'extrait
/// exact de l'instruction, donc elle ne couvre pas une nouvelle durée ajoutée dans le même fichier.
/// </summary>
public sealed partial class DureesLegalesCodeesEnDurTests
{
    /// <summary>Exceptions : (fichier relatif à la racine du dépôt, extrait exact de l'instruction) → justification.</summary>
    private static readonly IReadOnlyDictionary<(string Fichier, string Instruction), string> Exceptions = new Dictionary<(string, string), string>
    {

        // Réglages d'organisation du suivi des reprises (SAN-65, à valider), injectés par configuration.
        [("src/Services/Obligations/src/Sepp.Obligations.Domain/Reprises/PolitiqueSuiviReprise.cs", "int AlerteAvantEcheanceJoursOuvrables = 2")] = "réglage d'organisation (alerte anticipée, SAN-65), configurable",

        // Réglages d'organisation d'Obligations et fenêtres de calcul (options du moteur, configurables, pas des délais légaux).
        [("src/Services/Obligations/src/Sepp.Obligations.Application/Commun.cs", "public int FenetreRegroupementJours { get; set; } = 30")] = "fenêtre de regroupement des échéances (SAN-03), configurable",
        [("src/Services/Obligations/src/Sepp.Obligations.Application/Commun.cs", "public int HorizonAlertesJours { get; set; } = 30")] = "horizon d'affichage des alertes, configurable",
        [("src/Services/Obligations/src/Sepp.Obligations.Application/Commun.cs", "public int HorizonDuesJours { get; set; } = 90")] = "horizon d'affichage des échéances à venir, configurable",
        [("src/Services/Obligations/src/Sepp.Obligations.Application/Reprises/SynchronisationProcessusReprise.cs", "public int AlerteAvantEcheanceJoursOuvrables { get; set; } = 2")] = "réglage d'organisation (alerte anticipée, SAN-65), configurable",
        [("src/Services/Obligations/src/Sepp.Obligations.Domain/Calcul/MoteurEcheances.cs", "public int AnterioritePrealableMois { get; init; } = 3")] = "tolérance d'antériorité d'une évaluation préalable (§5.1) : option du moteur, à valider, non fixée par la loi",
        [("src/Services/Obligations/src/Sepp.Obligations.Domain/Calcul/Regroupement.cs", "public const int FenetreMaximaleJours = 365")] = "borne de validation d'une saisie, pas un délai",
        [("src/Services/PostesRisques/src/Sepp.PostesRisques.Domain/Risques/Risque.cs", "public const int FrequenceMaximaleMois = 120")] = "borne de validation d'une saisie (fréquence de surveillance), pas un délai",
        [("src/Services/Integrations/src/Sepp.Integrations.Application/Flux/FluxUseCases.cs", "public const int JoursMaximum = 366")] = "borne de pagination d'API",
        [("src/Services/SurveillanceMedicale/src/Sepp.SurveillanceMedicale.Application/Vaccinations/VaccinationsUseCases.cs", "public const int AlertePeremptionJours = 30")] = "seuil d'alerte de stock de vaccins, d'organisation",

        // Planchers légaux : un paramètre de Référentiels plus court est ignoré, jamais l'inverse (NF-04, SAN-44).
        [("src/Services/Audit/src/Sepp.Audit.Domain/Journal/Conservation.cs", "public const int MinimumAnnees = 10")] = "plancher légal de conservation du journal (NF-04), non abaissable par un paramètre",
        [("src/Services/SurveillanceMedicale/src/Sepp.SurveillanceMedicale.Domain/Conservation/Conservation.cs", "public const int PlancherAnnees = 15")] = "plancher légal de conservation du dossier (SAN-44, §2.1), non abaissable par un paramètre",

        // Constantes légales non paramétrables : majorité civile et fenêtre de lecture d'un test cutané.
        [("src/Services/Personnes/src/Sepp.Personnes.Domain/Personnes/Personne.cs", "if (type == TypeEtatParticulier.Jeune && dateDebut >= DateNaissance.AddYears(18))")] = "âge de la majorité (définition d'un état particulier, hors paramétrage)",
        [("src/Services/SurveillanceMedicale/src/Sepp.SurveillanceMedicale.Domain/Dossiers/ElementsDossier.cs", "if (dateLecture < DatePose || dateLecture > DatePose.AddDays(7))")] = "fenêtre de plausibilité de saisie de la lecture d'un test, pas un délai légal",

        // Réglages techniques (rétention des journaux d'intégration, bornes d'API) et fenêtres d'organisation.
        [("src/Services/Integrations/src/Sepp.Integrations.Application/Flux/FluxUseCases.cs", "var debut = du ?? fin.AddDays(-29)")] = "fenêtre d'affichage par défaut d'un tableau de bord technique",
        [("src/Services/Integrations/src/Sepp.Integrations.Application/Ports.cs", "public TimeSpan ApresTraitement { get; set; } = TimeSpan.FromDays(1)")] = "rétention technique des charges utiles, configurable",
        [("src/Services/Integrations/src/Sepp.Integrations.Application/Ports.cs", "public TimeSpan ApresEchec { get; set; } = TimeSpan.FromDays(30)")] = "rétention technique des charges utiles, configurable",
        [("src/Services/Planification/src/Sepp.Planification.Application/Agenda/AgendaUseCases.cs", "if (query.Au <= query.Du || query.Au - query.Du > TimeSpan.FromDays(93))")] = "borne de pagination d'API",
        [("src/Services/Planification/src/Sepp.Planification.Application/Reservations/ReservationsUseCases.cs", "if (query.Au <= query.Du || query.Au - query.Du > TimeSpan.FromDays(93))")] = "borne de pagination d'API",
        [("src/Services/Planification/src/Sepp.Planification.Application/Replanification/ReplanificationUseCases.cs", "if (command.Fin <= command.Debut || command.Fin - command.Debut > TimeSpan.FromDays(92))")] = "borne de pagination d'API",
        [("src/Services/Planification/src/Sepp.Planification.Application/Commun.cs", "public int HorizonRechercheJours { get; set; } = 60")] = "réglage d'organisation de la recherche de créneaux, configurable",
        [("src/Services/Planification/src/Sepp.Planification.Application/Commun.cs", "public int FenetreRegroupementJours { get; set; } = 90")] = "réglage d'organisation du regroupement en sessions, configurable",
        [("src/Services/Reintegration/src/Sepp.Reintegration.Application/Commun.cs", "public int AlerteAvantEcheanceJoursOuvrables { get; set; } = 5")] = "réglage d'organisation du suivi (SAN-65, à valider), configurable",
        [("src/Services/SurveillanceMedicale/src/Sepp.SurveillanceMedicale.Application/Commun.cs", "public int DelaiJours { get; set; } = 15")] = "délai de traitement d'une copie de dossier (SAN-43) : valeur non fixée par le cahier des charges, à valider ; hors paramètres de Référentiels",
    };

    [GeneratedRegex(@"TimeSpan\s*\.\s*FromDays\s*\(\s*\d+(\.\d+)?\s*\)")]
    private static partial Regex FromDaysLitteral();

    [GeneratedRegex(@"\.\s*Add(Days|Months|Years)\s*\(\s*(?<n>-?\d+)\s*\)")]
    private static partial Regex AjoutLitteral();

    [GeneratedRegex(@"\b\w*(Jours|Delais?|Semaines|Mois|Annees|Duree)\w*\s*(\{\s*get;\s*(set|init);\s*\})?\s*=\s*(?<n>\d+)\s*([;,)]|$)")]
    private static partial Regex MembreInitialiseParLitteral();

    [GeneratedRegex(@"""(\\.|[^""\\])*""")]
    private static partial Regex Chaine();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espaces();

    [Fact]
    public void Aucune_duree_legale_n_est_codee_en_dur_dans_le_domaine_ou_l_application_des_services()
    {
        var racine = RacineDepot();
        var violations = new List<string>();
        var exceptionsUtilisees = new HashSet<(string, string)>();

        foreach (var (fichier, instruction) in Instructions(racine))
        {
            if (!EstSuspecte(instruction))
            {
                continue;
            }

            var exception = Exceptions.Keys.FirstOrDefault(e => e.Item1 == fichier && instruction.Contains(e.Item2, StringComparison.Ordinal));
            if (exception != default)
            {
                exceptionsUtilisees.Add(exception);
                continue;
            }

            violations.Add($"{fichier} : {instruction}");
        }

        violations.ShouldBeEmpty(
            "Durée légale codée en dur (ARC-21) : utiliser un paramètre de Référentiels (ParametresLegauxParDefaut) ou, pour un réglage "
            + "technique ou d'organisation, déclarer une exception justifiée dans DureesLegalesCodeesEnDurTests.Exceptions.\n"
            + string.Join('\n', violations));

        var obsoletes = Exceptions.Keys.Except(exceptionsUtilisees).Select(k => $"{k.Item1} : {k.Item2}").ToList();
        obsoletes.ShouldBeEmpty("Exceptions devenues sans objet : à retirer de DureesLegalesCodeesEnDurTests.Exceptions.\n" + string.Join('\n', obsoletes));
    }

    [Theory]
    [InlineData("var delai = TimeSpan.FromDays(30)", true)]
    [InlineData("var limite = depart.AddDays(14)", true)]
    [InlineData("var limite = depart.AddMonths(-6)", true)]
    [InlineData("var fin = depart.AddYears(10)", true)]
    [InlineData("public int DelaiRecoursJours { get; set; } = 10", true)]
    [InlineData("public const int ConservationAnnees = 15", true)]
    [InlineData("var lendemain = jour.AddDays(1)", false)]
    [InlineData("var veille = jour.AddDays(-1)", false)]
    [InlineData("var limite = jour.AddDays(parametre.Valeur)", false)]
    [InlineData("public int Tentatives { get; set; } = 5", false)]
    [InlineData("public int DelaiJours { get; set; } = 1", false)]
    [InlineData("var delai = TimeSpan.FromHours(24)", false)]
    public void La_regle_de_detection_distingue_une_duree_legale_d_une_borne_de_journee(string instruction, bool suspecte) =>
        EstSuspecte(instruction).ShouldBe(suspecte);

    private static bool EstSuspecte(string instruction) =>
        FromDaysLitteral().IsMatch(instruction)
        || AjoutLitteral().Matches(instruction).Any(m => Math.Abs(int.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture)) >= 2)
        || MembreInitialiseParLitteral().Matches(instruction).Any(m => int.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture) >= 2);

    /// <summary>Instructions (texte normalisé, commentaires et chaînes retirés) du domaine et de l'application de chaque service.</summary>
    private static IEnumerable<(string Fichier, string Instruction)> Instructions(string racine)
    {
        var services = Path.Combine(racine, "src", "Services");
        var fichiers = Directory.EnumerateFiles(services, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(racine, f).Replace('\\', '/'))
            .Where(f => (f.Contains(".Domain/", StringComparison.Ordinal) || f.Contains(".Application/", StringComparison.Ordinal))
                && f.Contains("/src/", StringComparison.Ordinal)
                && !f.Contains("/obj/", StringComparison.Ordinal)
                && !f.Contains("/bin/", StringComparison.Ordinal)
                && !f.Contains("/Migrations/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        foreach (var fichier in fichiers)
        {
            var code = SansCommentairesNiChaines(File.ReadAllText(Path.Combine(racine, fichier)));

            // Une instruction se termine par « ; » ou « { » / « } » : on la reconstitue pour tolérer les retours à la ligne.
            foreach (var morceau in code.Split(['\n'], StringSplitOptions.RemoveEmptyEntries).Aggregate(
                         new List<string> { string.Empty },
                         (liste, ligne) =>
                         {
                             liste[^1] += " " + ligne.Trim();
                             if (ligne.TrimEnd().EndsWith(';') || ligne.TrimEnd().EndsWith('{') || ligne.TrimEnd().EndsWith('}'))
                             {
                                 liste.Add(string.Empty);
                             }

                             return liste;
                         }))
            {
                var normalisee = Espaces().Replace(morceau, " ").Trim().TrimEnd(';', '{', '}', ',').Trim();
                if (normalisee.Length > 0)
                {
                    yield return (fichier, normalisee);
                }
            }
        }
    }

    private static string RacineDepot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Sepp.slnx")) && Directory.Exists(Path.Combine(dir.FullName, "src", "Services")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Racine du dépôt introuvable (Sepp.slnx).");
    }

    private static string SansCommentairesNiChaines(string source)
    {
        // Les chaînes sont remplacées par une chaîne vide (les littéraux « 10 » d'un message ne comptent pas), puis les commentaires retirés.
        var sansBlocs = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        var lignes = sansBlocs.Split('\n').Select(l => Chaine().Replace(l, "\"\""));
        return string.Join('\n', lignes.Select(l =>
        {
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));
    }
}
