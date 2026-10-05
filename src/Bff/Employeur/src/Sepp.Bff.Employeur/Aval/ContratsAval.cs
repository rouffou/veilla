namespace Sepp.Bff.Employeur.Aval;

// Contrats JSON des services aval, recopiés de leurs documents OpenAPI (ARC-30). Le BFF ne référence aucun
// assembly de service (ARC-02, ARC-43) : seuls les champs utiles aux écrans sont déclarés, les énumérations
// sont lues comme des chaînes et relayées telles quelles.

// --- Service Affiliés (port 5111) ---

public sealed record AffilieAval(
    Guid Id,
    int Version,
    FicheAval Fiche,
    IReadOnlyList<UniteEtablissementAval> UnitesEtablissement,
    IReadOnlyList<ContactAval> Contacts);

public sealed record FicheAval(
    string NumeroBce,
    string Denomination,
    string FormeJuridique,
    string CodeNace,
    string CommissionParitaire,
    string CategorieTarifaire,
    DateOnly DateAffiliation,
    DateOnly? DateFin,
    string Langue,
    string RegimeLinguistique,
    string Statut);

public sealed record AdresseAval(string Rue, string Numero, string? Boite, string CodePostal, string Localite, string? CodePays);

public sealed record UniteEtablissementAval(
    Guid Id,
    string Numero,
    string Nom,
    AdresseAval Adresse,
    DateOnly ValideDu,
    DateOnly? ValideJusquAu,
    IReadOnlyList<SiteAval> Sites);

public sealed record SiteAval(Guid Id, string Nom, AdresseAval Adresse, DateOnly ValideDu, DateOnly? ValideJusquAu);

public sealed record ContactAval(
    Guid Id,
    string Nom,
    string? Fonction,
    string Role,
    string? Email,
    string? Telephone,
    DateOnly ValideDu,
    DateOnly? ValideJusquAu);

// --- Service Personnes et occupations (port 5112) ---

/// <summary>Résumé d'une personne ; le NISS masqué renvoyé par le service n'est pas relayé au portail (DAT-06).</summary>
public sealed record TravailleurAval(Guid Id, string Nom, string Prenom, DateOnly DateNaissance);

// --- Service Postes et risques (port 5113) ---

public sealed record PosteAval(
    Guid Id,
    Guid AffilieId,
    string Intitule,
    string? Description,
    string? MetierTypeCode,
    string Statut,
    IReadOnlyList<LienRisqueAval> Risques);

public sealed record LienRisqueAval(string RisqueCode, string NiveauExposition, DateOnly ValideDu, DateOnly? ValideJusquAu);

public sealed record RisqueAval(string Code, string Categorie, string Libelle);

public sealed record ListeNominativeAval(
    Guid Id,
    Guid AffilieId,
    string Type,
    int Version,
    DateOnly DateReference,
    DateTimeOffset DateGeneration,
    Guid? DocumentId,
    DateOnly ConserverJusquAu,
    Guid? PropositionId,
    int NombreLignes,
    IReadOnlyList<LigneListeAval>? Lignes);

public sealed record LigneListeAval(Guid PersonneId, Guid PosteId, IReadOnlyList<string> CodesRisques, DateOnly? DateDerniereEvaluation, string Origine);

public sealed record PropositionPosteRisqueAval(
    Guid Id,
    Guid PosteId,
    Guid AffilieId,
    string Origine,
    DateTimeOffset DateProposition,
    string Motif,
    DateOnly ValideDu,
    string Statut,
    DateTimeOffset? DateDecision,
    string? MotifRefus);

public sealed record PropositionListeAval(
    Guid Id,
    Guid ListeNominativeId,
    Guid AffilieId,
    string TypeListe,
    int VersionListe,
    string Origine,
    DateTimeOffset DateProposition,
    string Motif,
    string Statut,
    DateTimeOffset? DateDecision,
    string? MotifRefus,
    Guid? ListeResultanteId);

/// <summary>Corps de <c>POST /api/v1/postes/{id}/propositions</c> (AFF-14, AFF-31).</summary>
public sealed record PropositionPosteRisqueCorpsAval(string Motif, DateOnly ValideDu, IReadOnlyList<LigneRisqueSaisie> Lignes, object? AvisCppt);

public sealed record LigneRisqueSaisie(string Type, string RisqueCode, string? NiveauExposition);

/// <summary>Corps de <c>POST /api/v1/listes-nominatives/{id}/propositions</c> (AFF-31).</summary>
public sealed record PropositionListeCorpsAval(string Motif, IReadOnlyList<LigneListeSaisie> Lignes);

public sealed record LigneListeSaisie(string Type, Guid PersonneId, Guid PosteId);

public sealed record IdentifiantAval(Guid Id);
