using System.Globalization;

using Sepp.BuildingBlocks.Domain;
using Sepp.Integrations.Application;
using Sepp.Integrations.Application.Externe;
using Sepp.Integrations.Domain;
using Sepp.Integrations.Domain.Bce;
using Sepp.Integrations.Domain.Correspondances;

namespace Sepp.Integrations.Adapters.External;

// Simulateurs des organismes externes (INT-04 : formats réels à confirmer). Ils produisent des données FICTIVES et
// DÉTERMINISTES au format canonique interne — aucune structure de message d'un organisme n'est reproduite ici —
// pour le développement local, les démonstrations et les tests (NF-14 : jamais de données réelles hors production).
// Sélection par configuration : Integrations:Adaptateurs:<Flux> = Simulateur | Reel.

/// <summary>Générateur de données fictives déterministes partagé par les simulateurs.</summary>
public static class DonneesFictives
{
    private static readonly string[] Noms = ["Simule-Dupont", "Simule-Peeters", "Simule-Janssens", "Simule-Maes", "Simule-Lambert", "Simule-Dubois"];
    private static readonly string[] Prenoms = ["Alex", "Camille", "Sam", "Charlie", "Dominique", "Claude"];

    /// <summary>
    /// NISS fictif valide (date de naissance au XXe siècle, numéro d'ordre, clé modulo 97) dérivé de la graine ;
    /// le numéro d'ordre 998 est réservé aux personnes « inconnues » du registre simulé.
    /// </summary>
    public static string Niss(long graine)
    {
        var naissance = new DateOnly(1970, 1, 1).AddDays((int)(Math.Abs(graine) % 10_000));
        var ordre = 1 + (int)(Math.Abs(graine) % 997);
        return NissDe(naissance, ordre);
    }

    public static string NissDe(DateOnly naissance, int ordre)
    {
        var corps = $"{naissance:yyMMdd}{ordre:D3}";
        var controle = 97 - (long.Parse(corps, CultureInfo.InvariantCulture) % 97);
        return $"{corps}{controle:D2}";
    }

    /// <summary>Identité fictive cohérente avec le NISS (date de naissance et sexe selon la parité du numéro d'ordre).</summary>
    public static IdentiteRegistreNational? Identite(string niss)
    {
        var chiffres = new string(niss.Where(char.IsAsciiDigit).ToArray());
        if (chiffres.Length != 11
            || !DateOnly.TryParseExact("19" + chiffres[..6], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var naissance))
        {
            return null;
        }

        var ordre = int.Parse(chiffres[6..9], CultureInfo.InvariantCulture);
        if (ordre == 998 || NissDe(naissance, ordre) != chiffres)
        {
            return null;
        }

        return new IdentiteRegistreNational(
            Noms[ordre % Noms.Length], Prenoms[ordre % Prenoms.Length], naissance,
            ordre % 2 == 1 ? Sexe.Masculin : Sexe.Feminin, ordre % 3 == 0 ? Language.Nl : Language.Fr);
    }

    public static long Graine(string numero) => long.Parse(numero[..8], CultureInfo.InvariantCulture);
}

/// <summary>
/// Simulateur BCE : toute entreprise au numéro valide existe, avec une dénomination, une forme juridique, un code NACE
/// et une ou deux unités d'établissement fictifs dérivés du numéro. Les numéros dont la base se termine par 999 sont
/// inconnus (cas d'erreur reproductible).
/// </summary>
public sealed class SimulateurBce(TimeProvider horloge) : IRegistreBce
{
    private static readonly string[] Formes = ["SRL", "SA", "SC", "ASBL", "SNC"];
    private static readonly string[] Naces = ["62.010", "86.210", "41.201", "47.110", "56.101"];
    private static readonly (string CodePostal, string Localite)[] Localites = [("1000", "Bruxelles"), ("4000", "Liège"), ("5000", "Namur"), ("7000", "Mons"), ("9000", "Gent")];

    public Task<DonneesEntreprise?> ConsulterEntrepriseAsync(string numeroBce, CancellationToken cancellationToken)
    {
        if (!NumerosBce.EstEntrepriseValide(numeroBce, out var numero) || numero[5..8] == "999")
        {
            return Task.FromResult<DonneesEntreprise?>(null);
        }

        var graine = DonneesFictives.Graine(numero);
        var unites = Enumerable.Range(0, 1 + (int)(graine % 2)).Select(i =>
        {
            var (codePostal, localite) = Localites[(graine + i) % Localites.Length];
            return new DonneesUniteEtablissement(
                NumerosBce.AvecControle($"{2 + i}{numero[1..8]}"),
                $"Siège simulé {i + 1}",
                new AdresseBce("Rue de la Simulation", (1 + ((graine + i) % 200)).ToString(CultureInfo.InvariantCulture), null, codePostal, localite),
                new DateOnly(2010, 1, 1).AddYears(i));
        }).ToList();

        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");
        var aujourdhui = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(horloge.GetUtcNow(), zone).DateTime);
        return Task.FromResult<DonneesEntreprise?>(new DonneesEntreprise(
            numero, $"Entreprise simulée {NumerosBce.Formater(numero)}", Formes[graine % Formes.Length], Naces[graine % Naces.Length], aujourdhui, unites));
    }
}

/// <summary>
/// Simulateur DIMONA : un lot unique et déterministe (position « lot-1 ») pour les trois premiers affiliés connus —
/// entrée d'un salarié, entrée d'un étudiant à durée déterminée, sortie du salarié — plus une entrée pour un
/// employeur non affilié (rejet attendu). Les lectures suivantes sont vides.
/// </summary>
public sealed class SimulateurDimona(ICorrespondanceRepository correspondances) : IFluxDimona
{
    public const string PositionApresLot = "lot-1";

    /// <summary>Employeur fictif jamais affilié (numéro BCE valide).</summary>
    public static readonly string EmployeurNonAffilie = NumerosBce.AvecControle("09999990");

    public async Task<LotFlux<DeclarationDimona>> RecupererDeclarationsAsync(string? position, CancellationToken cancellationToken)
    {
        if (position == PositionApresLot)
        {
            return new LotFlux<DeclarationDimona>([], PositionApresLot);
        }

        var employeurs = await correspondances.ListerAsync(TypeIdentifiantExterne.NumeroBce, null, 3, cancellationToken);
        var declarations = new List<DeclarationDimona>();
        foreach (var employeur in employeurs.Select(e => e.ValeurExterne).Where(e => e != EmployeurNonAffilie))
        {
            declarations.AddRange(Declarations(employeur));
        }

        declarations.Add(new DeclarationDimona($"SIM{EmployeurNonAffilie}01", TypeDeclarationDimona.Entree,
            DonneesFictives.Niss(DonneesFictives.Graine(EmployeurNonAffilie)), EmployeurNonAffilie, null,
            TypeTravailleur.Salarie, TypeContrat.DureeIndeterminee, new DateOnly(2026, 1, 5), null));
        return new LotFlux<DeclarationDimona>(declarations, PositionApresLot);
    }

    /// <summary>Déclarations fictives d'un employeur (exposées pour les tests).</summary>
    public static IReadOnlyList<DeclarationDimona> Declarations(string numeroBceEmployeur)
    {
        var graine = DonneesFictives.Graine(numeroBceEmployeur) * 10;
        return
        [
            new($"SIM{numeroBceEmployeur}01", TypeDeclarationDimona.Entree, DonneesFictives.Niss(graine + 1), numeroBceEmployeur, null,
                TypeTravailleur.Salarie, TypeContrat.DureeIndeterminee, new DateOnly(2026, 1, 5), null),
            new($"SIM{numeroBceEmployeur}02", TypeDeclarationDimona.Entree, DonneesFictives.Niss(graine + 2), numeroBceEmployeur, null,
                TypeTravailleur.Etudiant, TypeContrat.Etudiant, new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31)),
            new($"SIM{numeroBceEmployeur}01", TypeDeclarationDimona.Sortie, DonneesFictives.Niss(graine + 1), numeroBceEmployeur, null,
                TypeTravailleur.Salarie, TypeContrat.DureeIndeterminee, new DateOnly(2026, 1, 5), new DateOnly(2026, 6, 30)),
        ];
    }
}

/// <summary>
/// Simulateur du registre national (BCSS, identification et mutations) : identité fictive dérivée de tout NISS valide
/// (numéro d'ordre 998 : inconnu) ; un lot unique et déterministe de mutations (position « lot-1 ») pour les travailleurs
/// fictifs du simulateur DIMONA des trois premiers affiliés connus — changements d'adresse, de nom, de prénom et de langue du
/// salarié, décès de l'étudiant —, plus un changement de nom d'une personne qu'aucun service ne suit (ignoré par Personnes).
/// </summary>
public sealed class SimulateurRegistreNational(ICorrespondanceRepository correspondances) : IRegistreNational
{
    public const string PositionApresLot = "lot-1";

    /// <summary>Référence de la mutation de la personne non suivie.</summary>
    public const string ReferenceMutationInconnue = "SIM-RN-INCONNU";

    /// <summary>NISS fictif d'une personne que le SEPP ne suit pas.</summary>
    public static readonly string NissMutation = DonneesFictives.Niss(4242);

    /// <summary>Date du décès fictif de l'étudiant : elle tombe pendant son occupation DIMONA simulée (1er juillet au 31 août 2026).</summary>
    public static readonly DateOnly DateDeces = new(2026, 7, 15);

    public Task<IdentiteRegistreNational?> ConsulterIdentiteAsync(string niss, CancellationToken cancellationToken) =>
        Task.FromResult(DonneesFictives.Identite(niss));

    public async Task<LotFlux<MutationRegistreNational>> RecupererMutationsAsync(string? position, CancellationToken cancellationToken)
    {
        if (position == PositionApresLot)
        {
            return new LotFlux<MutationRegistreNational>([], PositionApresLot);
        }

        var employeurs = await correspondances.ListerAsync(TypeIdentifiantExterne.NumeroBce, null, 3, cancellationToken);
        var mutations = new List<MutationRegistreNational>();
        foreach (var employeur in employeurs.Select(e => e.ValeurExterne).Where(e => e != SimulateurDimona.EmployeurNonAffilie))
        {
            mutations.AddRange(Mutations(employeur));
        }

        mutations.Add(new MutationRegistreNational(ReferenceMutationInconnue, NissMutation, TypeMutationRegistreNational.ChangementNom,
            new DateOnly(2026, 9, 1), Nom: "Simule-Nouveau-Nom"));
        return new LotFlux<MutationRegistreNational>(mutations, PositionApresLot);
    }

    /// <summary>Mutations fictives des travailleurs DIMONA simulés d'un employeur (exposées pour les tests).</summary>
    public static IReadOnlyList<MutationRegistreNational> Mutations(string numeroBceEmployeur)
    {
        var declarations = SimulateurDimona.Declarations(numeroBceEmployeur);
        var salarie = declarations[0].Niss;
        var etudiant = declarations[1].Niss;
        var effet = new DateOnly(2026, 3, 1);
        return
        [
            new($"SIMRN{numeroBceEmployeur}01", salarie, TypeMutationRegistreNational.ChangementAdresse, effet,
                new AdresseRegistreNational("Rue de la Mutation Simulée", "12", "B", "5000", "Namur")),
            new($"SIMRN{numeroBceEmployeur}02", salarie, TypeMutationRegistreNational.ChangementNom, effet, Nom: "Simule-Nouveau-Nom"),
            new($"SIMRN{numeroBceEmployeur}03", salarie, TypeMutationRegistreNational.ChangementPrenom, effet, Prenom: "Simule-Nouveau-Prenom"),
            new($"SIMRN{numeroBceEmployeur}04", salarie, TypeMutationRegistreNational.ChangementLangue, effet, Langue: Language.De),
            new($"SIMRN{numeroBceEmployeur}05", etudiant, TypeMutationRegistreNational.Deces, DateDeces),
        ];
    }
}

