using static Sepp.Pseudonymisation.Technique;

namespace Sepp.Pseudonymisation;

/// <summary>
/// Règles de pseudonymisation déclarées par service (NF-14). Les colonnes d'audit <c>created_by</c> et <c>updated_by</c>
/// de toutes les tables sont traitées à part par le moteur. Ce qui n'est pas couvert est listé par le rapport.
/// </summary>
public static class CatalogueRegles
{
    public const string Personnes = "personnes";
    public const string Affilies = "affilies";
    public const string SurveillanceMedicale = "surveillance-medicale";
    public const string Reintegration = "reintegration";

    private static readonly string[] OutboxInbox = ["outbox_message", "inbox_message", "republication"];

    public static IReadOnlyList<ReglesService> Tous { get; } = [PersonnesRegles, AffiliesRegles, SurveillanceMedicaleRegles, ReintegrationRegles];

    public static ReglesService Pour(string nom) =>
        Tous.FirstOrDefault(s => string.Equals(s.Nom, nom, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Service inconnu : « {nom} » (attendus : {string.Join(", ", Tous.Select(s => s.Nom))}).");

    private static ReglesService PersonnesRegles => new(Personnes,
    [
        new("personne",
        [
            new("sexe", Conserver),
            new("nom", Nom),
            new("prenom", Prenom),
            new("date_naissance", DateDecalee),
            new("date_deces", DateDecalee),
            new("niss_chiffre", NissChiffre),
            new("niss_hash", NissIndexAveugle),
            new("email", Email),
            new("telephone", Telephone),
            new("adresse_rue", Rue),
            new("adresse_numero", NumeroRue),
            new("adresse_boite", Boite),
        ]),
        new("etat_particulier", [new("type_chiffre", ChiffreRechiffre)]),
        new("mutation_registre_national", [new("avant_chiffre", ChiffreNul), new("apres_chiffre", ChiffreNul)]),
    ], OutboxInbox, Chiffrement: true, IndexAveugle: true);

    private static ReglesService AffiliesRegles => new(Affilies,
    [
        new("groupe", [new("nom", NomLieu, "Groupe")]),
        new("affilie",
        [
            new("numero_bce", NumeroBce),
            new("denomination", Denomination),
            new("denomination_bce", Denomination),
        ]),
        new("unite_etablissement",
        [
            new("numero_ue_bce", NumeroUniteEtablissement),
            new("nom", NomLieu, "Unité"),
            new("adresse_rue", Rue),
            new("adresse_numero", NumeroRue),
            new("adresse_boite", Boite),
        ]),
        new("site",
        [
            new("nom", NomLieu, "Site"),
            new("adresse_rue", Rue),
            new("adresse_numero", NumeroRue),
            new("adresse_boite", Boite),
            new("latitude", Coordonnee, "lat"),
            new("longitude", Coordonnee, "lon"),
        ]),
        new("contact",
        [
            new("nom", NomComplet),
            new("email", Email),
            new("telephone", Telephone),
        ]),
        new("modification_affilie", [new("auteur", Utilisateur), new("avant", JsonVide), new("apres", JsonVide)]),
        new("ecart_synchronisation", [new("numero_bce", NumeroBce), new("detail", TexteSupprime), new("resolu_par", Utilisateur)]),
    ], OutboxInbox, Chiffrement: false);

    private static ReglesService SurveillanceMedicaleRegles => new(SurveillanceMedicale,
    [
        new("dossier_sante", [new("gestionnaire_cpmt_id", Utilisateur)]),
        new("piece_jointe", [new("titre_chiffre", ChiffreSupprime), new("description_chiffre", ChiffreSupprime)]),
        new("questionnaire_rempli", [new("reponses_chiffre", ChiffreJson, "valeur")]),
        new("vaccination", [new("remarque_chiffre", ChiffreSupprime), new("administre_par", Utilisateur)]),
        new("test_tuberculinique", [new("realise_par", Utilisateur), new("lecture_chiffre", ChiffreRechiffre)]),
        new("examen", [new("professionnel_id", Utilisateur)]),
        new("observation_clinique", [new("anamnese_chiffre", ChiffreSupprime), new("examen_clinique_chiffre", ChiffreSupprime)]),
        new("resultat_acte", [new("valeurs_chiffre", ChiffreJson, "texte"), new("commentaire_chiffre", ChiffreSupprime)]),
        new("proposition_frequence", [new("decide_par", Utilisateur)]),
        new("decision",
        [
            new("justification_chiffre", ChiffreSupprime),
            new("recommandations_chiffre", ChiffreSupprime),
            new("auteur_id", Utilisateur),
        ]),
        new("recours", [new("commentaire_chiffre", ChiffreSupprime)]),
        new("declaration_mp",
        [
            new("auteur_id", Utilisateur),
            new("contenu_chiffre", ChiffreJson, "description,agentCausal,derniereDecision"),
        ]),
        new("demande_information_fedris", [new("objet_chiffre", ChiffreSupprime), new("reponse_chiffre", ChiffreSupprime)]),
        new("transfert_dossier", [new("paquet_chiffre", ChiffreSupprime), new("demande_par", Utilisateur)]),
        new("preuve_destruction", [new("validee_par", Utilisateur), new("motif_validation", TexteSupprime)]),
        new("demande_copie_dossier", [new("traitee_par", Utilisateur)]),
        new("modele_texte", [new("cpmt_id", Utilisateur)]),
    ], OutboxInbox, Chiffrement: true);

    private static ReglesService ReintegrationRegles => new(Reintegration,
    [
        new("incapacite", [new("gestionnaire_id", Utilisateur)]),
        new("estimation_potentiel", [new("auteur_id", Utilisateur), new("commentaire_chiffre", ChiffreSupprime)]),
        new("trajet", [new("evaluateur_id", Utilisateur), new("note_evaluation_chiffre", ChiffreSupprime)]),
        new("echange_ehealth", [new("contenu_chiffre", ChiffreSupprime), new("reference_destinataire", TexteSupprime)]),
    ], OutboxInbox, Chiffrement: true);
}
