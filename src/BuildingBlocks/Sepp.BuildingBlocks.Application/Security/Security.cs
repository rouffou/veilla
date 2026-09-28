namespace Sepp.BuildingBlocks.Application.Security;

/// <summary>Utilisateur authentifié à l'origine du cas d'usage (ARC-40).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Identifiant stable (claim <c>sub</c>) ; « system » pour les traitements techniques.</summary>
    string UserId { get; }

    IReadOnlySet<string> Roles { get; }

    bool HasPermission(string permission);
}

/// <summary>Profils internes et externes (§3.1, §3.2). Les valeurs sont les noms de rôles portés par le jeton.</summary>
public static class Roles
{
    public const string Cpmt = "cpmt";
    public const string CpmtDirigeant = "cpmt-dirigeant";
    public const string Infirmier = "infirmier";
    public const string AssistantMedical = "assistant-medical";
    public const string ConseillerSecurite = "cp-securite";
    public const string ConseillerErgonome = "cp-ergonome";
    public const string ConseillerHygieniste = "cp-hygieniste";
    public const string Cpap = "cpap";
    public const string CpapDirigeant = "cpap-dirigeant";
    public const string GestionnaireDossiers = "gestionnaire";
    public const string Planificateur = "planificateur";
    public const string ResponsableCentre = "responsable-centre";
    public const string Direction = "direction";
    public const string AdministrateurFonctionnel = "admin-fonctionnel";
    public const string Dpo = "dpo";

    public const string Employeur = "employeur";
    public const string Sipp = "sipp";
    public const string Travailleur = "travailleur";
}

/// <summary>
/// Permissions issues de la matrice des droits (§3.3). Les services n'évaluent que des permissions ;
/// la correspondance rôle → permissions est définie une seule fois dans <see cref="RolePermissions"/> (ARC-41).
/// </summary>
public static class Permissions
{
    public const string DossierSanteLire = "dossier-sante:lire";
    public const string DossierSanteEcrire = "dossier-sante:ecrire";
    public const string DecisionEcrire = "decision:ecrire";
    public const string DecisionLire = "decision:lire";
    public const string DossierPsyLire = "dossier-psy:lire";
    public const string DossierPsyEcrire = "dossier-psy:ecrire";
    public const string AnalyseRisquesLire = "analyse-risques:lire";
    public const string AnalyseRisquesEcrire = "analyse-risques:ecrire";
    public const string AffilieLire = "affilie:lire";
    public const string AffilieEcrire = "affilie:ecrire";
    public const string ReferentielsLire = "referentiels:lire";
    public const string ReferentielsAdministrer = "referentiels:administrer";
    public const string AuditLire = "audit:lire";
}

public static class RolePermissions
{
    private static readonly string[] ConseillersNonMedicaux =
        [Roles.ConseillerSecurite, Roles.ConseillerErgonome, Roles.ConseillerHygieniste];

    private static readonly Dictionary<string, HashSet<string>> Map = Build();

    public static IReadOnlySet<string> For(IEnumerable<string> roles) =>
        roles.SelectMany(r => Map.TryGetValue(r, out var p) ? p : []).ToHashSet(StringComparer.Ordinal);

    private static Dictionary<string, HashSet<string>> Build()
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Grant(string role, params string[] permissions)
        {
            if (!map.TryGetValue(role, out var set))
            {
                map[role] = set = new HashSet<string>(StringComparer.Ordinal);
            }

            set.UnionWith(permissions);
        }

        // Tous les internes lisent les référentiels.
        foreach (var role in new[]
                 {
                     Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier, Roles.AssistantMedical, Roles.ConseillerSecurite,
                     Roles.ConseillerErgonome, Roles.ConseillerHygieniste, Roles.Cpap, Roles.CpapDirigeant,
                     Roles.GestionnaireDossiers, Roles.Planificateur, Roles.ResponsableCentre, Roles.Direction,
                     Roles.AdministrateurFonctionnel, Roles.Dpo,
                 })
        {
            Grant(role, Permissions.ReferentielsLire);
        }

        // §3.3 — Dossier de santé et décision : CPMT / infirmier.
        foreach (var role in new[] { Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier })
        {
            Grant(role, Permissions.DossierSanteLire, Permissions.DossierSanteEcrire, Permissions.AnalyseRisquesLire,
                Permissions.AnalyseRisquesEcrire, Permissions.AffilieLire, Permissions.DecisionLire);
        }

        Grant(Roles.Cpmt, Permissions.DecisionEcrire);
        Grant(Roles.CpmtDirigeant, Permissions.DecisionEcrire);

        // §3.3 — Dossier psychosocial : CPAP uniquement.
        foreach (var role in new[] { Roles.Cpap, Roles.CpapDirigeant })
        {
            Grant(role, Permissions.DossierPsyLire, Permissions.DossierPsyEcrire, Permissions.AnalyseRisquesLire,
                Permissions.AnalyseRisquesEcrire, Permissions.AffilieLire);
        }

        // §3.3 — Autres conseillers en prévention.
        foreach (var role in ConseillersNonMedicaux)
        {
            Grant(role, Permissions.DecisionLire, Permissions.AnalyseRisquesLire, Permissions.AnalyseRisquesEcrire,
                Permissions.AffilieLire);
        }

        // §3.3 — Gestionnaire de dossiers.
        Grant(Roles.GestionnaireDossiers, Permissions.DecisionLire, Permissions.AnalyseRisquesLire,
            Permissions.AffilieLire, Permissions.AffilieEcrire);

        Grant(Roles.Planificateur, Permissions.AffilieLire);
        Grant(Roles.ResponsableCentre, Permissions.AffilieLire);
        Grant(Roles.Direction, Permissions.AffilieLire);
        Grant(Roles.AdministrateurFonctionnel, Permissions.ReferentielsAdministrer, Permissions.AffilieLire);
        Grant(Roles.Dpo, Permissions.AuditLire);
        Grant(Roles.CpmtDirigeant, Permissions.AuditLire);
        Grant(Roles.CpapDirigeant, Permissions.AuditLire);

        // §3.3 — Externes : périmètre restreint à leur affilié / à eux-mêmes, vérifié par le service propriétaire.
        Grant(Roles.Employeur, Permissions.DecisionLire, Permissions.AnalyseRisquesLire, Permissions.AffilieLire,
            Permissions.AffilieEcrire);
        Grant(Roles.Sipp, Permissions.DecisionLire, Permissions.AnalyseRisquesLire, Permissions.AffilieLire,
            Permissions.AffilieEcrire);
        Grant(Roles.Travailleur, Permissions.DecisionLire);

        return map;
    }
}
