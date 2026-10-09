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

    // Intégrations — rôle technique du compte de service (client credentials OIDC) du service Intégrations,
    // jamais attribué à une personne : alimentation DIMONA du service Personnes (AFF-20).
    public const string Integrations = "integrations";

    // Documents — rôle technique du compte de service du service Documents (client credentials OIDC), jamais attribué
    // à une personne : lecture de la langue de l'affilié et du travailleur pour la langue des documents (NF-41).
    public const string Documents = "documents";

    // Communications — rôle technique du compte de service du service Communications (client credentials OIDC), jamais
    // attribué à une personne : résolution des coordonnées et préférences de canal des destinataires (DOC-03, SAN-10).
    public const string Communications = "communications";

    // Affiliés — rôle technique du compte de service du service Affiliés (client credentials OIDC), jamais attribué à une
    // personne : lecture des données d'entreprise de la BCE chez Intégrations pour mettre l'affilié à jour (AFF-01, AFF-02).
    public const string Affilies = "affilies";
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

    // Audit (NF-04, PSY-20) : périmètre de consultation du journal d'audit par zone de sensibilité (ARC-04).
    public const string AuditZoneStandard = "audit:zone-standard";
    public const string AuditZoneMedicale = "audit:zone-medicale";
    public const string AuditZonePsychosociale = "audit:zone-psychosociale";

    // Personnes — identité, occupations, affectations et états particuliers des travailleurs (§3.3 « Données de l'affilié »).
    public const string PersonneLire = "personne:lire";
    public const string PersonneEcrire = "personne:ecrire";

    // Postes et risques (AFF-10 à AFF-14, AFF-30, AFF-31).
    public const string PosteLire = "poste:lire";
    public const string PosteEcrire = "poste:ecrire";
    public const string RisquePosteValider = "risque-poste:valider";
    public const string SurchargeFrequenceLire = "surcharge-frequence:lire";
    public const string SurchargeFrequenceEcrire = "surcharge-frequence:ecrire";

    // Intégrations (INT-02) : tableau de suivi des flux, relance manuelle, lancement à la demande, correspondances.
    public const string IntegrationsAdministrer = "integrations:administrer";

    // Lecture des seules données d'entreprise de la BCE (publiques) par le compte technique du service Affiliés.
    public const string IntegrationsBceLire = "integrations:bce-lire";

    // Surveillance médicale (§3.3, zone médicale) : destruction validée par le responsable du traitement (SAN-44, NF-22),
    // protocoles médicaux (valeurs de référence, schémas vaccinaux, questionnaires, durées de conservation),
    // stock de vaccins par centre (SAN-51) et remplissage d'un questionnaire de santé sans lecture du dossier (SAN-22).
    public const string DossierSantePurger = "dossier-sante:purger";
    public const string ProtocolesMedicauxAdministrer = "protocoles-medicaux:administrer";
    public const string StockVaccinsGerer = "stock-vaccins:gerer";
    public const string QuestionnaireSanteRemplir = "questionnaire-sante:remplir";

    // Planification (§8 PLA-01 à PLA-09, §5.3 SAN-10 à SAN-13) : agendas, rendez-vous, convocations, tournées.
    public const string PlanificationLire = "planification:lire";
    public const string PlanificationGerer = "planification:gerer";
    public const string PlanificationRessources = "planification:ressources";
    public const string PlanificationModelesAgenda = "planification:modeles-agenda";
    public const string PlanificationSalleAttente = "planification:salle-attente";
    public const string PlanificationReserver = "planification:reserver";

    // Obligations (SAN-01 à SAN-04, AFF-32) : échéances de surveillance de la santé (types d'examens, dates, statuts ;
    // aucune donnée médicale), statuts manuels, demandes du travailleur et recalcul.
    public const string ObligationLire = "obligation:lire";
    public const string ObligationGerer = "obligation:gerer";

    // Documents (DOC-01, DOC-02, NF-21) : modèles, génération, lecture des documents de la zone standard et vérification
    // d'intégrité. Les documents des zones médicale et psychosociale exigent en plus dossier-sante:* ou dossier-psy:*.
    public const string DocumentsModeleLire = "documents:modele-lire";
    public const string DocumentsModeleGerer = "documents:modele-gerer";
    public const string DocumentsModeleValider = "documents:modele-valider";
    public const string DocumentsModeleValiderMedical = "documents:modele-valider-medical";
    public const string DocumentsModeleValiderPsychosocial = "documents:modele-valider-psychosocial";
    public const string DocumentsGenerer = "documents:generer";
    public const string DocumentsLire = "documents:lire";
    public const string DocumentsVerifierIntegrite = "documents:verifier-integrite";

    // Communications (DOC-03 à DOC-05) : journal des envois par dossier, envoi manuel, relance des échecs.
    public const string CommunicationsLire = "communications:lire";
    public const string CommunicationsEnvoyer = "communications:envoyer";
    public const string CommunicationsAdministrer = "communications:administrer";

    // Reprise (saga examen de reprise, POR-04) : annonce d'une reprise du travail, suivi du processus (statut, jalons,
    // alertes d'échéance : identifiants, dates et statuts, aucune donnée médicale) et gestion (modification, annulation).
    public const string RepriseAnnoncer = "reprise:annoncer";
    public const string RepriseLire = "reprise:lire";
    public const string RepriseGerer = "reprise:gerer";
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

        // Audit (NF-04, PSY-20) : le DPO voit tout le journal, le CPMT dirigeant la zone médicale,
        // le CPAP dirigeant la zone psychosociale.
        Grant(Roles.Dpo, Permissions.AuditZoneStandard, Permissions.AuditZoneMedicale, Permissions.AuditZonePsychosociale);
        Grant(Roles.CpmtDirigeant, Permissions.AuditZoneMedicale);
        Grant(Roles.CpapDirigeant, Permissions.AuditZonePsychosociale);

        // §3.3 — Externes : périmètre restreint à leur affilié / à eux-mêmes, vérifié par le service propriétaire.
        Grant(Roles.Employeur, Permissions.DecisionLire, Permissions.AnalyseRisquesLire, Permissions.AffilieLire,
            Permissions.AffilieEcrire);
        Grant(Roles.Sipp, Permissions.DecisionLire, Permissions.AnalyseRisquesLire, Permissions.AffilieLire,
            Permissions.AffilieEcrire);
        Grant(Roles.Travailleur, Permissions.DecisionLire);

        // Personnes — §3.3 « Données de l'affilié » : lecture CPMT/infirmier, CPAP, autres CP ; lecture/écriture
        // gestionnaire ; écriture partielle de l'employeur (et du SIPP) limitée à son affilié (claim affilie_id,
        // vérifiée par le service Personnes). Assistant médical et planificateur : lecture pour l'accueil et les convocations.
        foreach (var role in new[]
                 {
                     Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier, Roles.AssistantMedical, Roles.Cpap, Roles.CpapDirigeant,
                     Roles.ConseillerSecurite, Roles.ConseillerErgonome, Roles.ConseillerHygieniste, Roles.GestionnaireDossiers,
                     Roles.Planificateur, Roles.Employeur, Roles.Sipp,
                 })
        {
            Grant(role, Permissions.PersonneLire);
        }

        // Postes et risques — §3.3 « Données de l'affilié » : lecture pour les profils qui lisent l'affilié ;
        // écriture (catalogue, propositions) pour le gestionnaire et l'employeur/SIPP (périmètre de leur affilié) ;
        // validation du lien poste ↔ risque (AFF-14, AFF-31) et surcharges de fréquence (AFF-13) réservées au CPMT.
        foreach (var role in new[]
                 {
                     Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier, Roles.Cpap, Roles.CpapDirigeant, Roles.ConseillerSecurite,
                     Roles.ConseillerErgonome, Roles.ConseillerHygieniste, Roles.GestionnaireDossiers, Roles.Planificateur,
                     Roles.ResponsableCentre, Roles.Direction, Roles.AdministrateurFonctionnel, Roles.Employeur, Roles.Sipp,
                 })
        {
            Grant(role, Permissions.PosteLire);
        }

        foreach (var role in new[] { Roles.GestionnaireDossiers, Roles.Employeur, Roles.Sipp })
        {
            Grant(role, Permissions.PersonneEcrire);
            Grant(role, Permissions.PosteEcrire);
        }

        foreach (var role in new[] { Roles.Cpmt, Roles.CpmtDirigeant })
        {
            Grant(role, Permissions.RisquePosteValider, Permissions.SurchargeFrequenceLire, Permissions.SurchargeFrequenceEcrire);
        }

        Grant(Roles.Infirmier, Permissions.SurchargeFrequenceLire);

        // Intégrations — suivi et relance des flux (INT-02) par le gestionnaire et l'administrateur fonctionnel ;
        // le compte technique du service n'écrit que les occupations issues de DIMONA (AFF-20), sans lecture des fiches.
        Grant(Roles.GestionnaireDossiers, Permissions.IntegrationsAdministrer);
        Grant(Roles.AdministrateurFonctionnel, Permissions.IntegrationsAdministrer);
        Grant(Roles.Integrations, Permissions.PersonneEcrire);
        Grant(Roles.Affilies, Permissions.IntegrationsBceLire);

        // Surveillance médicale — le CPMT dirigeant, responsable du traitement, valide la destruction des dossiers et les
        // protocoles médicaux ; CPMT et infirmiers gèrent le stock de vaccins de leur centre. Le questionnaire de santé
        // est rempli à l'avance par le travailleur (portail, claim personne_id) ou sur tablette tendue par l'assistant
        // médical : écriture seule, sans aucune lecture du dossier (§3.3).
        Grant(Roles.CpmtDirigeant, Permissions.DossierSantePurger, Permissions.ProtocolesMedicauxAdministrer);
        foreach (var role in new[] { Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier })
        {
            Grant(role, Permissions.StockVaccinsGerer);
        }

        Grant(Roles.AssistantMedical, Permissions.QuestionnaireSanteRemplir);
        Grant(Roles.Travailleur, Permissions.QuestionnaireSanteRemplir);

        // Planification — §3.1 : le planificateur gère agendas, convocations et tournées ; le responsable de centre pilote
        // et affecte les ressources (ressources, lieux, congés) ; l'assistant médical tient l'accueil et la salle d'attente
        // (PLA-08). Le CPMT gère ses propres modèles d'agenda et durées standard (PLA-03, limité à sa ressource par le
        // service). Employeur et travailleur réservent en ligne dans les créneaux ouverts (SAN-12), dans leur périmètre
        // (claims affilie_id / personne_id, vérifiés par le service Planification).
        foreach (var role in new[]
                 {
                     Roles.Planificateur, Roles.ResponsableCentre, Roles.AssistantMedical, Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier,
                 })
        {
            Grant(role, Permissions.PlanificationLire, Permissions.PlanificationSalleAttente);
        }

        Grant(Roles.Planificateur, Permissions.PlanificationGerer, Permissions.PlanificationModelesAgenda);
        Grant(Roles.ResponsableCentre, Permissions.PlanificationGerer, Permissions.PlanificationModelesAgenda, Permissions.PlanificationRessources);
        Grant(Roles.Cpmt, Permissions.PlanificationModelesAgenda);
        Grant(Roles.CpmtDirigeant, Permissions.PlanificationModelesAgenda);
        Grant(Roles.Employeur, Permissions.PlanificationReserver);
        Grant(Roles.Travailleur, Permissions.PlanificationReserver);

        // Obligations — lecture des échéances par les profils qui organisent la surveillance de la santé (CPMT, infirmier,
        // assistant médical, planificateur, gestionnaire, responsable de centre) ; employeur et SIPP limités à leur affilié
        // (claim affilie_id, vérifiée par le service Obligations, qui leur masque les types confidentiels). Gestion des
        // statuts (convocation, absence, report, excuse, annulation), des demandes du travailleur et du recalcul : profils internes.
        foreach (var role in new[]
                 {
                     Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier, Roles.AssistantMedical, Roles.Planificateur,
                     Roles.GestionnaireDossiers, Roles.ResponsableCentre, Roles.Employeur, Roles.Sipp,
                 })
        {
            Grant(role, Permissions.ObligationLire);
        }

        foreach (var role in new[]
                 {
                     Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier, Roles.AssistantMedical, Roles.Planificateur, Roles.GestionnaireDossiers,
                 })
        {
            Grant(role, Permissions.ObligationGerer);
        }

        // Documents — DOC-01 : l'administrateur fonctionnel gère, valide et publie les modèles ; les modèles médicaux sont
        // validés par le CPMT dirigeant et les modèles psychosociaux par le CPAP dirigeant. Génération et lecture de la zone
        // standard par les internes qui produisent des courriers et rapports ; externes limités à leur périmètre (affilié du
        // jeton, documents adressés au travailleur lui-même), vérifié par le service Documents.
        foreach (var role in new[]
                 {
                     Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier, Roles.AssistantMedical, Roles.Cpap, Roles.CpapDirigeant,
                     Roles.ConseillerSecurite, Roles.ConseillerErgonome, Roles.ConseillerHygieniste, Roles.GestionnaireDossiers,
                     Roles.Planificateur, Roles.AdministrateurFonctionnel,
                 })
        {
            Grant(role, Permissions.DocumentsModeleLire, Permissions.DocumentsGenerer, Permissions.DocumentsLire);
        }

        Grant(Roles.AdministrateurFonctionnel, Permissions.DocumentsModeleGerer, Permissions.DocumentsModeleValider,
            Permissions.DocumentsVerifierIntegrite);
        Grant(Roles.CpmtDirigeant, Permissions.DocumentsModeleValiderMedical);
        Grant(Roles.CpapDirigeant, Permissions.DocumentsModeleValiderPsychosocial);
        Grant(Roles.Dpo, Permissions.DocumentsVerifierIntegrite);
        foreach (var role in new[] { Roles.Employeur, Roles.Sipp, Roles.Travailleur })
        {
            Grant(role, Permissions.DocumentsLire);
        }

        Grant(Roles.Documents, Permissions.AffilieLire, Permissions.PersonneLire);

        // Communications — DOC-05 : journal consultable par les internes qui suivent les dossiers ; envoi manuel par le
        // gestionnaire, le planificateur et l'assistant médical ; relance des échecs par le gestionnaire et l'administrateur.
        foreach (var role in new[]
                 {
                     Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier, Roles.AssistantMedical, Roles.Cpap, Roles.CpapDirigeant,
                     Roles.ConseillerSecurite, Roles.ConseillerErgonome, Roles.ConseillerHygieniste, Roles.GestionnaireDossiers,
                     Roles.Planificateur, Roles.ResponsableCentre, Roles.AdministrateurFonctionnel, Roles.Dpo,
                 })
        {
            Grant(role, Permissions.CommunicationsLire);
        }

        foreach (var role in new[] { Roles.GestionnaireDossiers, Roles.Planificateur, Roles.AssistantMedical })
        {
            Grant(role, Permissions.CommunicationsEnvoyer);
        }

        Grant(Roles.GestionnaireDossiers, Permissions.CommunicationsAdministrer);
        Grant(Roles.AdministrateurFonctionnel, Permissions.CommunicationsAdministrer);
        Grant(Roles.Communications, Permissions.AffilieLire, Permissions.PersonneLire);

        // Reprise (saga examen de reprise, POR-04). Répartition proposée, à valider (§3.3) : l'employeur et le SIPP annoncent
        // et suivent les reprises de leur affilié (périmètre claim affilie_id vérifié par le service) ; le gestionnaire et le
        // planificateur annoncent pour le compte d'un employeur et gèrent (modification, annulation, alertes) ; le CPMT, le
        // CPMT dirigeant, l'infirmier et l'assistant médical lisent le suivi.
        foreach (var role in new[] { Roles.Employeur, Roles.Sipp, Roles.GestionnaireDossiers, Roles.Planificateur })
        {
            Grant(role, Permissions.RepriseAnnoncer);
        }

        foreach (var role in new[]
                 {
                     Roles.Employeur, Roles.Sipp, Roles.GestionnaireDossiers, Roles.Planificateur,
                     Roles.Cpmt, Roles.CpmtDirigeant, Roles.Infirmier, Roles.AssistantMedical,
                 })
        {
            Grant(role, Permissions.RepriseLire);
        }

        foreach (var role in new[] { Roles.GestionnaireDossiers, Roles.Planificateur })
        {
            Grant(role, Permissions.RepriseGerer);
        }

        return map;
    }
}
