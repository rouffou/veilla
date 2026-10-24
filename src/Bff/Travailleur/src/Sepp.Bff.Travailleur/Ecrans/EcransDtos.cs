namespace Sepp.Bff.Travailleur.Ecrans;

// Modèles de lecture orientés écran du portail travailleur (ARC-35) : composés à la volée à partir des services aval.
// Aucun dossier médical, aucune réponse de questionnaire, aucun NISS ne figure dans ces modèles (§3.3, ARC-06).

/// <summary>Rendez-vous du travailleur : jamais la ressource (conseiller, salle, appareil) ni l'état de la salle d'attente.</summary>
public sealed record RendezVousEcran(
    Guid Id,
    Guid AffilieId,
    Guid LieuId,
    DateTimeOffset Debut,
    DateTimeOffset Fin,
    string TypeActe,
    string Statut,
    string? MotifAnnulation,
    DateTimeOffset? PresenceConfirmeeLe = null,
    int NombreDeplacements = 0);

public sealed record CreneauEcran(Guid Id, Guid LieuId, DateTimeOffset Debut, DateTimeOffset Fin, string TypeActe);

/// <summary>Réservation d'un créneau ouvert ; la personne est celle du jeton, jamais celle du corps.</summary>
public sealed record ReservationCorps(Guid CreneauId, Guid AffilieId, IReadOnlyList<Guid>? ObligationIds);

public sealed record RendezVousReserve(Guid Id);

/// <summary>
/// Annulation demandée par le travailleur : <paramref name="Raison"/> est un code (ConflitHoraire, ObligationProfessionnelle,
/// ProblemeTransport, ChangementSituation, ExamenDejaEffectue, Autre) ; <paramref name="Commentaire"/> est un texte libre court
/// qui ne doit contenir aucune donnée de santé (POR-11, ARC-06).
/// </summary>
public sealed record AnnulationCorps(string? Raison, string? Commentaire);

public sealed record DeplacementCorps(Guid NouveauCreneauId);

public sealed record QuestionEcran(string Code, string Libelle, string TypeReponse, bool Obligatoire, IReadOnlyList<string> Choix);

/// <summary>Modèle de questionnaire de santé à remplir (SAN-22) : questions seulement, jamais de réponses déjà enregistrées.</summary>
public sealed record QuestionnaireEcran(string Code, int Version, string Titre, IReadOnlyList<QuestionEcran> Questions);

public sealed record ReponseSaisie(string CodeQuestion, string Valeur);

public sealed record QuestionnaireReponsesCorps(IReadOnlyList<ReponseSaisie> Reponses);

/// <summary>Accusé d'écriture seule : aucune donnée du dossier n'est renvoyée.</summary>
public sealed record QuestionnaireEnregistre(Guid Id);

/// <summary>Demande du travailleur sans passer par l'employeur : le motif n'est ni demandé ni conservé (§5.1).</summary>
public sealed record DemandeCorps(Guid AffilieId, string Type);

public sealed record DemandeEnregistree(Guid Id);

/// <summary>
/// Employeur du travailleur (occupation active). Sans dénomination : aucun service accessible au rôle travailleur ne la
/// fournit (Affiliés exige <c>affilie:lire</c>) ; le portail désigne l'employeur par son rang.
/// </summary>
public sealed record EmployeurEcran(Guid AffilieId, string TypeTravailleur, DateOnly DateDebut, DateOnly? DateFin);

/// <summary>Examen à planifier du travailleur : type, échéances et statut, jamais de contenu médical (ARC-06).</summary>
public sealed record ExamenAPlanifierEcran(
    Guid ObligationId, Guid AffilieId, string Type, string Libelle, DateOnly DateDue, DateOnly? DateLimite, string Statut, bool EnRetard);

/// <summary>Document publié pour le travailleur (POR-13) : ni zone, ni empreinte, ni emplacement de stockage.</summary>
public sealed record DocumentEcran(Guid Id, string Categorie, string CodeModele, string Langue, DateTimeOffset Date, string Format, long Taille);

/// <summary>
/// Accueil composite : une section à <c>null</c> est signalée dans <see cref="Indisponibles"/> (service aval indisponible)
/// sans faire échouer l'écran ; aucun chiffre n'est inventé.
/// </summary>
public sealed record AccueilEcran(
    Guid PersonneId,
    IReadOnlyList<RendezVousEcran>? ProchainsRendezVous,
    IReadOnlyList<DocumentEcran>? DocumentsRecents,
    int? QuestionnairesDisponibles,
    IReadOnlyList<string> Indisponibles);
