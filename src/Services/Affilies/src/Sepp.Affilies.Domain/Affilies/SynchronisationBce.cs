using Sepp.BuildingBlocks.Domain;

namespace Sepp.Affilies.Domain.Affilies;

/// <summary>Unité d'établissement telle que la BCE la décrit (données d'entreprise publiques, pas de donnée personnelle).</summary>
public sealed record UniteBce(string Numero, string Nom, string Rue, string NumeroRue, string? Boite, string CodePostal, string Localite, string CodePays, DateOnly? DateDebut);

/// <summary>Données BCE appliquées à un affilié : identification (AFF-01) et unités d'établissement (AFF-02).  <paramref name="Unites"/> vaut <c>null</c> quand les unités ne sont pas communiquées : elles restent inchangées.</summary>
public sealed record DonneesBce(string Denomination, string FormeJuridique, string CodeNace, IReadOnlyList<UniteBce>? Unites);

/// <summary>
/// Donnée BCE que la synchronisation ne sait pas résoudre automatiquement : elle est consignée pour le gestionnaire de
/// dossiers et n'a aucune autre conséquence sur l'affilié. <see cref="Reference"/> identifie l'objet concerné (numéro d'unité…).
/// </summary>
public sealed record EcartBce(string Code, string Reference, string Detail);

/// <summary>Codes des écarts relevés lors de la synchronisation avec la BCE.</summary>
public static class CodesEcartBce
{
    public const string NumeroInvalide = "numero-invalide";
    public const string AffilieInconnu = "affilie-inconnu";
    public const string AffilieCloture = "affilie-cloture";
    public const string FicheInvalide = "fiche-invalide";
    public const string DetailIndisponible = "detail-indisponible";
    public const string UniteInvalide = "unite-invalide";
    public const string UniteAutreAffilie = "unite-autre-affilie";
    public const string UniteFermee = "unite-fermee";
    public const string UniteAbsente = "unite-absente";
    public const string UnitesAbsentes = "unites-absentes";
}

/// <summary>
/// Mise à jour de l'affilié depuis la BCE (AFF-01, AFF-02, INT-04). Ne lève jamais d'exception métier : ce qui ne peut pas
/// être appliqué sans risque devient un <see cref="EcartBce"/>, le reste est appliqué (idempotent : des données identiques
/// ne modifient rien).
/// </summary>
public sealed partial class Affilie
{
    /// <summary>
    /// Applique les données BCE. Dénomination, forme juridique et NACE de la fiche sont ceux de la BCE ; chaque unité
    /// d'établissement est ajoutée, modifiée ou fermée (DAT-04 : jamais supprimée, fermée à <paramref name="dateExtraction"/>).
    /// Une unité absente de la BCE n'est fermée automatiquement que si aucun site n'y est ouvert, et jamais quand la BCE
    /// ne renvoie aucune unité (liste vide présumée incomplète).
    /// </summary>
    /// <param name="unitesAutresAffilies">Numéros d'unités déjà rattachés à un autre affilié (une unité n'appartient qu'à un seul affilié).</param>
    public IReadOnlyList<EcartBce> AppliquerDonneesBce(DonneesBce donnees, DateOnly dateExtraction, IReadOnlySet<string> unitesAutresAffilies)
    {
        ArgumentNullException.ThrowIfNull(donnees);
        ArgumentNullException.ThrowIfNull(unitesAutresAffilies);

        if (Statut is StatutAffilie.Absorbe or StatutAffilie.Scinde or StatutAffilie.Transfere)
        {
            return [new EcartBce(CodesEcartBce.AffilieCloture, NumeroBce.Value,
                $"L'affilié {NumeroBce.Formate} est clôturé (statut {Statut}) : les données BCE ne sont pas appliquées.")];
        }

        var ecarts = new List<EcartBce>();
        AppliquerFicheBce(donnees, ecarts);
        if (donnees.Unites is not null)
        {
            AppliquerUnitesBce(donnees.Unites, dateExtraction, unitesAutresAffilies, ecarts);
        }

        return ecarts;
    }

    private void AppliquerFicheBce(DonneesBce donnees, List<EcartBce> ecarts)
    {
        try
        {
            var fiche = new DonneesFiche(donnees.Denomination, donnees.FormeJuridique, donnees.CodeNace, CommissionParitaire, CategorieTarifaire, Langue, RegimeLinguistique);
            if (fiche.Denomination != Denomination || fiche.FormeJuridique != FormeJuridique || fiche.CodeNace != CodeNace)
            {
                ModifierFiche(fiche, GroupeId);
            }
        }
        catch (DomainException ex)
        {
            ecarts.Add(new EcartBce(CodesEcartBce.FicheInvalide, NumeroBce.Value, $"Identification BCE non applicable : {ex.Message}"));
        }
    }

    private void AppliquerUnitesBce(IReadOnlyList<UniteBce> unites, DateOnly dateExtraction, IReadOnlySet<string> unitesAutresAffilies, List<EcartBce> ecarts)
    {
        var presentes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var uniteBce in unites)
        {
            NumeroUniteEtablissement numero;
            try
            {
                numero = new NumeroUniteEtablissement(uniteBce.Numero);
            }
            catch (DomainException ex)
            {
                ecarts.Add(new EcartBce(CodesEcartBce.UniteInvalide, uniteBce.Numero, ex.Message));
                continue;
            }

            if (!presentes.Add(numero.Value))
            {
                continue;
            }

            try
            {
                AppliquerUniteBce(uniteBce, numero, dateExtraction, unitesAutresAffilies, ecarts);
            }
            catch (DomainException ex)
            {
                ecarts.Add(new EcartBce(CodesEcartBce.UniteInvalide, numero.Value, $"Unité {numero.Formate} non applicable : {ex.Message}"));
            }
        }

        if (presentes.Count == 0)
        {
            if (_unitesEtablissement.Any(u => u.Validite.IsOpen))
            {
                ecarts.Add(new EcartBce(CodesEcartBce.UnitesAbsentes, NumeroBce.Value,
                    "La BCE ne renvoie aucune unité d'établissement alors que l'affilié en a d'ouvertes : aucune fermeture automatique."));
            }

            return;
        }

        foreach (var unite in _unitesEtablissement.Where(u => u.Validite.IsOpen && !presentes.Contains(u.Numero.Value)).ToList())
        {
            FermerUniteAbsente(unite, dateExtraction, ecarts);
        }
    }

    private void AppliquerUniteBce(UniteBce uniteBce, NumeroUniteEtablissement numero, DateOnly dateExtraction, IReadOnlySet<string> unitesAutresAffilies, List<EcartBce> ecarts)
    {
        var existante = _unitesEtablissement.SingleOrDefault(u => u.Numero == numero);
        if (existante is null)
        {
            if (unitesAutresAffilies.Contains(numero.Value))
            {
                ecarts.Add(new EcartBce(CodesEcartBce.UniteAutreAffilie, numero.Value,
                    $"L'unité {numero.Formate} est déjà rattachée à un autre affilié : à arbitrer par le gestionnaire."));
                return;
            }

            AjouterUniteEtablissement(numero, uniteBce.Nom, AdresseDe(uniteBce), Langue, uniteBce.DateDebut ?? dateExtraction);
            return;
        }

        if (!existante.Validite.IsOpen)
        {
            ecarts.Add(new EcartBce(CodesEcartBce.UniteFermee, numero.Value,
                $"L'unité {numero.Formate} est fermée chez l'affilié (depuis le {existante.Validite.ValidTo:yyyy-MM-dd}) mais active à la BCE : à vérifier."));
            return;
        }

        var adresse = AdresseDe(uniteBce);
        var nom = Texte.Obligatoire(uniteBce.Nom, "Le nom de l'unité d'établissement", 200);
        if (existante.Nom != nom || existante.Adresse != adresse)
        {
            ModifierUniteEtablissement(existante.Id, nom, adresse, existante.Langue);
        }
    }

    private void FermerUniteAbsente(UniteEtablissement unite, DateOnly dateExtraction, List<EcartBce> ecarts)
    {
        if (unite.Sites.Any(s => s.Validite.IsOpen))
        {
            ecarts.Add(new EcartBce(CodesEcartBce.UniteAbsente, unite.Numero.Value,
                $"L'unité {unite.Numero.Formate} n'est plus connue de la BCE mais porte des sites ouverts : fermeture à décider par le gestionnaire."));
            return;
        }

        try
        {
            FermerUniteEtablissement(unite.Id, dateExtraction);
        }
        catch (DomainException ex)
        {
            ecarts.Add(new EcartBce(CodesEcartBce.UniteAbsente, unite.Numero.Value, $"Fermeture de l'unité {unite.Numero.Formate} impossible : {ex.Message}"));
        }
    }

    private static Adresse AdresseDe(UniteBce unite) =>
        new(unite.Rue, unite.NumeroRue, unite.Boite, unite.CodePostal, unite.Localite, unite.CodePays);
}
