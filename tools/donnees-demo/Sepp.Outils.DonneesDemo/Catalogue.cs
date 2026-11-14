using System.Globalization;
using System.Text;

namespace Sepp.Outils.DonneesDemo;

/// <summary>Risque d'un poste : code du jeu d'exemple (risques-exemple.csv) et niveau d'exposition.</summary>
internal sealed record LigneRisque(string Code, string Niveau);

internal sealed record PosteDef(string Intitule, string Description, IReadOnlyList<LigneRisque> Risques);

internal sealed record AffilieDef(
    string Cle,
    string Denomination,
    string FormeJuridique,
    string CodeNace,
    string CommissionParitaire,
    string Categorie,
    string Langue,
    string Regime,
    string BceBase,
    string UniteBase,
    string Rue,
    string Numero,
    string CodePostal,
    string Localite,
    string SiteNom,
    int NombreTravailleurs,
    IReadOnlyList<PosteDef> Postes)
{
    /// <summary>Numéro BCE fictif de dix chiffres, deux derniers chiffres de contrôle modulo 97.</summary>
    public string Bce => Catalogue.AvecControle(BceBase);

    public string UniteEtablissement => Catalogue.AvecControle(UniteBase);
}

internal sealed record PersonneDef(
    string Cle,
    string AffilieCle,
    int Index,
    string Prenom,
    string Nom,
    string Sexe,
    string Langue,
    DateOnly Naissance,
    string Niss,
    string PosteIntitule,
    int JoursDepuisDebut,
    string TypeTravailleur,
    string TypeContrat,
    string Rue,
    string Numero,
    string CodePostal,
    string Localite,
    string Canal)
{
    public string NomComplet => $"{Prenom} {Nom}";

    public string Email => $"{Catalogue.Ascii(Prenom)}.{Catalogue.Ascii(Nom)}@demo.example";
}

/// <summary>
/// Jeu de données fictif et déterministe : mêmes entités à chaque exécution, seules les dates suivent le jour d'exécution.
/// Aucun nom, NISS ni numéro BCE n'est réel : les NISS portent un contrôle modulo 97 valide mais un numéro d'ordre arbitraire.
/// </summary>
internal static class Catalogue
{
    public const string GroupeNom = "Groupe Demo Benelux";

    public static readonly IReadOnlyList<AffilieDef> Affilies =
    [
        new("boulangerie", "Boulangerie Démo SRL", "SRL", "10.711", "118.03", "B", "Fr", "BruxellesCapitale", "09990101", "29990101",
            "Rue des Fournils", "12", "1000", "Bruxelles", "Fournil central", 14,
            [
                new("Boulanger de nuit", "Fabrication du pain en horaire de nuit", [new("EX.NUIT.TRAVAIL", "Eleve"), new("EX.ERGO.CHARGES", "Moyen"), new("EX.ALIM.DENREES", "Faible")]),
                new("Pâtissier", "Pâtisserie et viennoiserie", [new("EX.ALIM.DENREES", "Moyen"), new("EX.ERGO.CHARGES", "Faible")]),
                new("Vendeur en boutique", "Vente et accueil de la clientèle", [new("EX.ALIM.DENREES", "Faible")]),
                new("Livreur", "Livraison des commandes aux professionnels", [new("EX.SECU.CONDUITE", "Moyen"), new("EX.ERGO.CHARGES", "Moyen")]),
                new("Responsable de production", "Coordination de la production", [new("EX.ERGO.CHARGES", "Faible")]),
                new("Employé administratif", "Gestion administrative", []),
            ]),
        new("zorg", "Zorg Demo vzw", "VZW", "87.101", "331", "C", "Nl", "Neerlandais", "09990202", "29990202",
            "Korenmarkt", "5", "9000", "Gent", "Woonzorgcentrum Demo", 14,
            [
                new("Verpleegkundige", "Zorg voor bewoners", [new("EX.BIO.HEPATITE_B", "Eleve"), new("EX.ERGO.CHARGES", "Moyen"), new("EX.NUIT.TRAVAIL", "Moyen")]),
                new("Zorgkundige", "Dagelijkse zorg", [new("EX.BIO.HEPATITE_B", "Moyen"), new("EX.ERGO.CHARGES", "Eleve")]),
                new("Poetspersoneel", "Onderhoud en hygiëne", [new("EX.CHIM.SOLVANTS", "Moyen")]),
                new("Kok", "Keuken", [new("EX.ALIM.DENREES", "Moyen")]),
                new("Opvoeder", "Begeleiding van bewoners", [new("EX.PSY.CHARGE", "Moyen")]),
                new("Administratief medewerker", "Administratie", []),
            ]),
        new("construct", "Construct Démo SA", "SA", "41.200", "124", "A", "Fr", "Francais", "09990303", "29990303",
            "Boulevard d'Avroy", "40", "4000", "Liège", "Chantier-dépôt de Liège", 12,
            [
                new("Maçon", "Gros œuvre", [new("EX.PHYS.BRUIT", "Eleve"), new("EX.PHYS.VIBRATIONS", "Moyen"), new("EX.ERGO.CHARGES", "Eleve")]),
                new("Grutier", "Conduite de grue à tour", [new("EX.SECU.POSTE", "Eleve"), new("EX.PHYS.VIBRATIONS", "Moyen")]),
                new("Chauffeur poids lourd", "Transport de matériaux", [new("EX.SECU.CONDUITE", "Eleve")]),
                new("Peintre", "Peinture et finitions", [new("EX.CHIM.SOLVANTS", "Eleve"), new("EX.CHIM.CANCEROGENES", "Moyen")]),
                new("Conducteur de travaux", "Pilotage de chantier", [new("EX.PSY.CHARGE", "Faible")]),
                new("Employé administratif", "Gestion administrative", []),
            ]),
    ];

    private static readonly string[] PrenomsFrF = ["Nadia", "Léa", "Sophie", "Camille", "Inès", "Julie", "Manon", "Clara", "Émilie", "Amélie"];
    private static readonly string[] PrenomsFrM = ["Karim", "Thomas", "Lucas", "Mehdi", "Antoine", "Hugo", "Yanis", "Julien", "Maxime", "Olivier"];
    private static readonly string[] NomsFr = ["Lambert", "Benali", "Dubois", "Martens", "Leroy", "Haddad", "Vincent", "Mercier", "Fontaine", "Lecomte", "Dumont", "Renard", "Colin", "Gérard", "Bastin", "Delvaux"];
    private static readonly string[] PrenomsNlF = ["Emma", "Lotte", "Fien", "Marie", "Sarah", "Elise", "Hanne", "Femke"];
    private static readonly string[] PrenomsNlM = ["Joris", "Jens", "Bram", "Thibault", "Wouter", "Pieter", "Sander", "Lars"];
    private static readonly string[] NomsNl = ["Peeters", "Janssens", "Maes", "Willems", "Claes", "Vermeulen", "De Smet", "Goossens", "Hermans", "Wouters", "Mertens", "Dewitte"];
    private static readonly string[] PrenomsDeF = ["Anna", "Katrin", "Lena", "Julia"];
    private static readonly string[] PrenomsDeM = ["Jonas", "Lukas", "Matthias", "Stefan"];
    private static readonly string[] NomsDe = ["Schmitz", "Müller", "Hoffmann", "Keller", "Zimmermann", "Weber"];

    private static readonly string[] RuesFr = ["Rue des Tilleuls", "Rue du Moulin", "Avenue des Acacias", "Rue de la Station", "Rue Neuve", "Chaussée de Mons"];
    private static readonly string[] RuesNl = ["Kerkstraat", "Molenstraat", "Stationsstraat", "Dorpsstraat", "Lindenlaan"];
    private static readonly Dictionary<string, string[]> CodesPostaux = new(StringComparer.Ordinal)
    {
        ["boulangerie"] = ["1000|Bruxelles", "1030|Schaerbeek", "1050|Ixelles", "1060|Saint-Gilles", "1080|Molenbeek-Saint-Jean", "1090|Jette"],
        ["zorg"] = ["9000|Gent", "9030|Mariakerke", "9040|Sint-Amandsberg", "9050|Ledeberg"],
        ["construct"] = ["4000|Liège", "4020|Liège", "4030|Grivegnée", "4040|Herstal"],
    };

    /// <summary>Les quelque 40 travailleurs, dans un ordre fixe : la première travailleuse de la Boulangerie est la travailleuse « à chercher ».</summary>
    public static IReadOnlyList<PersonneDef> Personnes(DateOnly aujourdhui)
    {
        var resultat = new List<PersonneDef>();
        var compteurs = new Dictionary<string, int>(StringComparer.Ordinal);
        var global = 0;
        foreach (var affilie in Affilies)
        {
            for (var i = 0; i < affilie.NombreTravailleurs; i++)
            {
                global++;
                var langue = LangueDe(affilie.Cle, i);
                var masculin = i % 2 == 1;
                var prenoms = (langue, masculin) switch
                {
                    ("Nl", false) => PrenomsNlF,
                    ("Nl", true) => PrenomsNlM,
                    ("De", false) => PrenomsDeF,
                    ("De", true) => PrenomsDeM,
                    (_, false) => PrenomsFrF,
                    _ => PrenomsFrM,
                };
                var noms = langue switch { "Nl" => NomsNl, "De" => NomsDe, _ => NomsFr };
                var clePrenom = $"{langue}{masculin}";
                var cleNom = $"nom{langue}";
                var prenom = prenoms[Suivant(compteurs, clePrenom) % prenoms.Length];
                var nom = noms[Suivant(compteurs, cleNom) % noms.Length];

                var naissance = new DateOnly(1965 + ((global * 7) % 40), 1 + ((global * 5) % 12), 1 + ((global * 11) % 28));
                var rues = langue == "Nl" ? RuesNl : RuesFr;
                var postal = CodesPostaux[affilie.Cle][i % CodesPostaux[affilie.Cle].Length].Split('|');
                var poste = affilie.Postes[i % affilie.Postes.Count];
                var debut = 1095 - (int)Math.Round(i * 1005.0 / Math.Max(1, affilie.NombreTravailleurs - 1));
                var stagiaire = i % 11 == 10;

                resultat.Add(new PersonneDef(
                    $"{affilie.Cle}-{i}", affilie.Cle, i, prenom, nom, masculin ? "Masculin" : "Feminin", langue, naissance,
                    GenererNiss(naissance, global, masculin), poste.Intitule, debut,
                    stagiaire ? "Stagiaire" : "Salarie", stagiaire ? "Stage" : "DureeIndeterminee",
                    rues[(global * 3) % rues.Length], (10 + ((global * 7) % 80)).ToString(CultureInfo.InvariantCulture), postal[0], postal[1],
                    i % 2 == 0 ? "Email" : "Courrier"));
            }
        }

        _ = aujourdhui;
        return resultat;
    }

    /// <summary>Ajoute deux chiffres de contrôle (97 − base modulo 97) à une base de huit chiffres (BCE).</summary>
    public static string AvecControle(string base8) =>
        base8 + (97 - (long.Parse(base8, CultureInfo.InvariantCulture) % 97)).ToString("00", CultureInfo.InvariantCulture);

    /// <summary>NISS fictif : AAMMJJ, numéro d'ordre (impair pour un homme) et contrôle modulo 97 (préfixe « 2 » dès 2000).</summary>
    public static string GenererNiss(DateOnly naissance, int rang, bool masculin)
    {
        var ordre = 100 + (rang * 2) + (masculin ? 1 : 0);
        var corps = long.Parse(
            $"{naissance.Year % 100:00}{naissance.Month:00}{naissance.Day:00}{ordre:000}", CultureInfo.InvariantCulture);
        var base97 = naissance.Year >= 2000 ? 2_000_000_000L + corps : corps;
        return $"{corps:000000000}{97 - (base97 % 97):00}";
    }

    public static string Ascii(string texte)
    {
        var decompose = texte.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decompose)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && c != ' ' && c != '\'')
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    private static string LangueDe(string affilie, int index) => affilie switch
    {
        "boulangerie" => index % 7 == 3 ? "Nl" : "Fr",
        "zorg" => index % 5 == 2 ? "Fr" : "Nl",
        _ => index is 4 or 8 or 11 ? "De" : "Fr",
    };

    private static int Suivant(Dictionary<string, int> compteurs, string cle)
    {
        compteurs.TryGetValue(cle, out var valeur);
        compteurs[cle] = valeur + 1;
        return valeur;
    }
}
