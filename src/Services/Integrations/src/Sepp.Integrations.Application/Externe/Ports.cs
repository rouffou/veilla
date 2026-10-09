using Sepp.BuildingBlocks.Domain;
using Sepp.Integrations.Domain.Bce;

namespace Sepp.Integrations.Application.Externe;

// Couche anti-corruption (§12, ARC-05) : chaque flux externe est vu par le cœur à travers un port et un format
// canonique interne. Les formats réels des organismes (versions, schémas) sont à confirmer lors de l'analyse détaillée
// (INT-04) : la traduction est entièrement confinée à l'adaptateur de chaque organisme (Adapters/External).

/// <summary>Sexe administratif, aligné sur l'API du service Personnes.</summary>
public enum Sexe
{
    Masculin,
    Feminin,
    Inconnu,
}

/// <summary>Catégorie de travailleur (AFF-23), alignée sur l'API interne DIMONA du service Personnes.</summary>
public enum TypeTravailleur
{
    Salarie,
    Interimaire,
    Etudiant,
    Stagiaire,
    Detache,
    Benevole,
}

/// <summary>Type de contrat (AFF-20), aligné sur l'API interne DIMONA du service Personnes.</summary>
public enum TypeContrat
{
    DureeIndeterminee,
    DureeDeterminee,
    TravailNettementDefini,
    Remplacement,
    Interim,
    Etudiant,
    Flexijob,
    Occasionnel,
    Stage,
    Benevolat,
    Autre,
}

public enum TypeDeclarationDimona
{
    Entree,
    Sortie,
}

/// <summary>Lot d'éléments d'un flux par lots et position à fournir pour lire le lot suivant.</summary>
public sealed record LotFlux<T>(IReadOnlyList<T> Elements, string? PositionSuivante);

/// <summary>Identité d'une personne selon la source authentique (registre national via la BCSS).</summary>
public sealed record IdentiteRegistreNational(string Nom, string Prenom, DateOnly DateNaissance, Sexe Sexe, Language Langue);

/// <summary>
/// Déclaration DIMONA (format canonique interne) : l'employeur et, pour un intérimaire, l'entreprise utilisatrice
/// sont désignés par leur numéro BCE, traduit en identifiant d'affilié par la correspondance d'identifiants.
/// </summary>
public sealed record DeclarationDimona(
    string ReferenceDimona,
    TypeDeclarationDimona Type,
    string Niss,
    string NumeroBceEmployeur,
    string? NumeroBceUtilisateur,
    TypeTravailleur TypeTravailleur,
    TypeContrat TypeContrat,
    DateOnly DateDebut,
    DateOnly? DateFin)
{
    public override string ToString() => $"DeclarationDimona {{ ReferenceDimona = {ReferenceDimona}, Type = {Type}, Niss = masqué }}";
}

/// <summary>Nature d'une mutation du registre national, alignée sur l'API interne du service Personnes (AFF-20, AFF-22).</summary>
public enum TypeMutationRegistreNational
{
    ChangementAdresse,
    ChangementNom,
    ChangementPrenom,
    ChangementLangue,
    Deces,
}

/// <summary>Adresse postale communiquée par le registre national (format canonique interne).</summary>
public sealed record AdresseRegistreNational(string Rue, string Numero, string? Boite, string CodePostal, string Localite, string Pays = "BE")
{
    public override string ToString() => "AdresseRegistreNational { masquée }";
}

/// <summary>
/// Mutation signalée par le registre national (format canonique interne) : changement d'adresse, de nom, de prénom, de
/// langue ou décès. <see cref="ReferenceMutation"/> est la clé d'idempotence (jamais un NISS) ; <see cref="DateEffet"/> est la
/// date d'effet (pour un décès : la date du décès) ; seule la valeur propre au <see cref="Type"/> est renseignée.
/// </summary>
public sealed record MutationRegistreNational(
    string ReferenceMutation,
    string Niss,
    TypeMutationRegistreNational Type,
    DateOnly DateEffet,
    AdresseRegistreNational? Adresse = null,
    string? Nom = null,
    string? Prenom = null,
    Language? Langue = null)
{
    // ARC-06, DAT-06 : ni le NISS ni les données d'identité ne doivent fuir par une trace.
    public override string ToString() => $"MutationRegistreNational {{ ReferenceMutation = {ReferenceMutation}, Type = {Type}, Niss = masqué }}";
}

/// <summary>Flux BCE : données d'une entreprise et de ses unités d'établissement.</summary>
public interface IRegistreBce
{
    /// <returns><c>null</c> si l'entreprise est inconnue de la BCE.</returns>
    Task<DonneesEntreprise?> ConsulterEntrepriseAsync(string numeroBce, CancellationToken cancellationToken);
}

/// <summary>Flux DIMONA / DmfA reçu via la BCSS (AFF-20).</summary>
public interface IFluxDimona
{
    Task<LotFlux<DeclarationDimona>> RecupererDeclarationsAsync(string? position, CancellationToken cancellationToken);
}

/// <summary>Flux BCSS d'identification : consultation du registre national et mutations.</summary>
public interface IRegistreNational
{
    /// <returns><c>null</c> si le NISS est inconnu.</returns>
    Task<IdentiteRegistreNational?> ConsulterIdentiteAsync(string niss, CancellationToken cancellationToken);

    Task<LotFlux<MutationRegistreNational>> RecupererMutationsAsync(string? position, CancellationToken cancellationToken);
}

public enum IssueAppel
{
    Succes,

    /// <summary>Refus métier du service appelé (données invalides, conflit) : l'échange est rejeté.</summary>
    Rejet,

    /// <summary>Échec technique (indisponibilité, délai, autorisation) : l'échange est en erreur, relançable.</summary>
    ErreurTechnique,
}

/// <summary>Résultat d'un appel à un service interne ; le message ne contient aucune donnée personnelle.</summary>
public sealed record ResultatAppel<T>(IssueAppel Issue, T? Valeur, string? Code, string? Message)
{
    public static ResultatAppel<T> Succes(T valeur) => new(IssueAppel.Succes, valeur, null, null);

    public static ResultatAppel<T> Rejet(string code, string message) => new(IssueAppel.Rejet, default, code, message);

    public static ResultatAppel<T> Erreur(string code, string message) => new(IssueAppel.ErreurTechnique, default, code, message);
}

/// <summary>Entrée DIMONA transmise au service Personnes (le NISS ne voyage que par cet appel interne, DAT-06).</summary>
public sealed record EntreeDimonaPersonnes(
    string ReferenceDimona,
    string Niss,
    IdentiteRegistreNational Identite,
    Guid AffilieId,
    Guid? AffilieUtilisateurId,
    TypeTravailleur TypeTravailleur,
    TypeContrat TypeContrat,
    DateOnly DateDebut,
    DateOnly? DateFin)
{
    public override string ToString() => $"EntreeDimonaPersonnes {{ ReferenceDimona = {ReferenceDimona}, Niss = masqué }}";
}

public sealed record OccupationDimona(Guid PersonneId, Guid OccupationId, bool DejaEnregistree);

/// <summary>Issue d'une mutation côté Personnes : <c>Appliquee</c>, <c>DejaAppliquee</c> ou <c>PersonneInconnue</c>.</summary>
public sealed record MutationPersonnes(Guid? PersonneId, string Statut);

/// <summary>API interne du service Personnes (appel HTTP authentifié par le compte technique du service).</summary>
public interface IPersonnesClient
{
    Task<ResultatAppel<OccupationDimona>> EnregistrerEntreeDimonaAsync(EntreeDimonaPersonnes entree, CancellationToken cancellationToken);

    Task<ResultatAppel<bool>> EnregistrerSortieDimonaAsync(string referenceDimona, DateOnly dateFin, CancellationToken cancellationToken);

    /// <summary>
    /// Mutation du registre national (adresse, nom, prénom, langue, décès) appliquée à la personne désignée par son NISS
    /// (dans le corps de l'appel uniquement). Idempotent sur la référence de la mutation.
    /// </summary>
    Task<ResultatAppel<MutationPersonnes>> AppliquerMutationAsync(MutationRegistreNational mutation, CancellationToken cancellationToken);
}
