using Sepp.Bff.Employeur.Aval;

namespace Sepp.Bff.Employeur.Ecrans;

// Modèles de lecture orientés écran du portail employeur (ARC-35) : composés à la volée à partir des services aval.

public sealed record AffilieResume(Guid Id, string NumeroBce, string Denomination, string Statut);

/// <summary>Affiliés du jeton ; ceux que le service Affiliés ne connaît pas (ou plus) sont signalés à part.</summary>
public sealed record MesAffilies(IReadOnlyList<AffilieResume> Affilies, IReadOnlyList<Guid> Introuvables);

public sealed record AdresseEcran(string Rue, string Numero, string? Boite, string CodePostal, string Localite, string CodePays);

public sealed record SiteEcran(Guid Id, string Nom, string UniteEtablissement, string NumeroUniteEtablissement, AdresseEcran Adresse);

public sealed record ContactEcran(Guid Id, string Nom, string? Fonction, string Role, string? Email, string? Telephone, DateOnly ValideDu);

public sealed record FicheAffilie(
    Guid Id,
    string NumeroBce,
    string Denomination,
    string FormeJuridique,
    string CodeNace,
    string CommissionParitaire,
    string CategorieTarifaire,
    DateOnly DateAffiliation,
    DateOnly? DateFin,
    string Langue,
    string Statut,
    IReadOnlyList<SiteEcran> Sites,
    IReadOnlyList<ContactEcran> Contacts);

public sealed record PageEcran<T>(IReadOnlyList<T> Elements, int Total, int Page, int Taille);

/// <summary>Travailleur tel que le voit l'employeur : jamais de NISS, même masqué (DAT-06, minimisation).</summary>
public sealed record TravailleurEcran(Guid Id, string Nom, string Prenom, DateOnly DateNaissance);

public sealed record RisquePosteEcran(string Code, string Libelle, string? Categorie, string NiveauExposition, DateOnly ExposeDepuis);

public sealed record PosteEcran(Guid Id, string Intitule, string? Description, string Statut, bool Expose, IReadOnlyList<RisquePosteEcran> Risques);

public sealed record RisqueEcran(string Code, string Libelle, string Categorie);

public sealed record ListeNominativeEcran(Guid Id, string Type, int Version, DateOnly DateReference, DateTimeOffset DateGeneration, int NombreLignes, bool Derniere);

public sealed record LigneListeEcran(
    Guid PersonneId,
    string? Nom,
    string? Prenom,
    Guid PosteId,
    string? Poste,
    IReadOnlyList<string> CodesRisques,
    DateOnly? DateDerniereEvaluation,
    string Origine);

public sealed record ListeNominativeDetail(ListeNominativeEcran Liste, IReadOnlyList<LigneListeEcran> Lignes);

/// <summary>
/// Indicateur du tableau de bord (POR-02). <c>Disponible = false</c> n'est jamais accompagné d'une valeur :
/// <c>Raison</c> vaut <c>fonctionnalite-a-venir</c> (service pas encore livré) ou <c>service-indisponible</c> (panne).
/// </summary>
public sealed record Indicateur(string Code, bool Disponible, int? Valeur, string? Raison)
{
    public const string AVenir = "fonctionnalite-a-venir";
    public const string Indisponible = "service-indisponible";

    public static Indicateur Avec(string code, int valeur) => new(code, true, valeur, null);

    public static Indicateur NonDisponible(string code, string raison) => new(code, false, null, raison);
}

public sealed record TableauDeBord(Guid AffilieId, DateOnly Date, IReadOnlyList<Indicateur> Indicateurs);

public sealed record PropositionEcran(
    Guid Id,
    string Nature,
    Guid CibleId,
    string Cible,
    string Motif,
    string Statut,
    DateTimeOffset DateProposition,
    DateTimeOffset? DateDecision,
    string? MotifRefus);

/// <summary>POR-03 : proposition de modification du profil de risques d'un poste (AFF-14).</summary>
public sealed record PropositionPosteCorps(string Motif, DateOnly ValideDu, IReadOnlyList<LigneRisqueSaisie> Lignes);

/// <summary>POR-03 : proposition de modification d'une liste nominative (AFF-31).</summary>
public sealed record PropositionListeCorps(string Motif, IReadOnlyList<LigneListeSaisie> Lignes);

public sealed record PropositionSoumise(Guid Id, string Statut);

/// <summary>POR-04 : annonce d'une reprise ; l'affilié vient de la route, jamais du corps.</summary>
public sealed record RepriseAnnonceCorps(Guid PersonneId, DateOnly DateReprise, DateOnly DebutAbsence);

/// <summary>Réponse à l'annonce : <c>Cree</c> vaut false si la reprise était déjà connue (idempotence du service Obligations).</summary>
public sealed record RepriseAnnoncee(Guid RepriseId, bool Cree, string Statut);

/// <summary>
/// POR-04 : suivi d'une reprise pour l'employeur. Le statut et la date limite suffisent : aucun identifiant d'examen ou de
/// décision, aucune donnée médicale (ARC-06). Ce que voit l'employeur reste à valider (plan saga, §6).
/// </summary>
public sealed record RepriseEcran(
    Guid Id,
    Guid PersonneId,
    DateOnly DateReprise,
    DateOnly DebutAbsence,
    string Statut,
    DateOnly? DateLimite,
    bool EnRetard,
    bool HorsDelai);
