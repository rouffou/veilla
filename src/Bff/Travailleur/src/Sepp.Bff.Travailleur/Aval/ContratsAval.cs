namespace Sepp.Bff.Travailleur.Aval;

// Contrats JSON des services aval, recopiés localement (le BFF ne référence aucun service, ARC-43). Seuls les champs utiles
// au portail travailleur sont déclarés : les autres sont ignorés à la lecture, donc jamais relayés. Les énumérations sont lues
// comme des chaînes.

public sealed record IdentifiantAval(Guid Id);

// --- Service Planification (port 5117), routes /api/v1/reservations (SAN-12) ---

/// <summary>
/// Rendez-vous visible par le travailleur. Ressource, salle, arrivée, appel en salle et obligations couvertes ne sont pas
/// déclarés : ce sont des informations internes qui ne vont pas au portail.
/// </summary>
public sealed record RendezVousAval(
    Guid Id,
    Guid PersonneId,
    Guid AffilieId,
    Guid LieuId,
    DateTimeOffset Debut,
    DateTimeOffset Fin,
    string TypeActe,
    string Statut,
    string? MotifAnnulation);

/// <summary>Créneau ouvert à la réservation en ligne (sans ressource ni information interne).</summary>
public sealed record CreneauOuvertAval(Guid Id, Guid LieuId, DateTimeOffset Debut, DateTimeOffset Fin, string TypeActe);

/// <summary>Corps de <c>POST /api/v1/reservations</c> : la personne est celle du jeton, jamais celle du corps du portail.</summary>
public sealed record ReservationCorpsAval(Guid CreneauId, Guid PersonneId, Guid AffilieId, IReadOnlyList<Guid>? ObligationIds);

// --- Service Surveillance médicale (port 5118), écriture seule (SAN-22) ---

public sealed record QuestionModeleAval(string Code, string Libelle, string TypeReponse, bool Obligatoire, IReadOnlyList<string> Choix);

public sealed record ModeleQuestionnaireAval(Guid Id, string Code, int Version, string Titre, IReadOnlyList<QuestionModeleAval> Questions);

public sealed record ReponseAval(string CodeQuestion, string Valeur);

/// <summary>Corps de <c>POST /api/v1/questionnaires/pre-remplissage</c> : la personne est celle du jeton.</summary>
public sealed record PreRemplissageCorpsAval(Guid PersonneId, string ModeleCode, IReadOnlyList<ReponseAval> Reponses);

// --- Service Obligations (port 5116) ---

/// <summary>
/// Corps de <c>POST /api/v1/mes-demandes</c> : type et affilié seulement, jamais le motif (§5.1). Aucune personne : le service
/// la lit dans le claim <c>personne_id</c> du jeton propagé.
/// </summary>
public sealed record MaDemandeCorpsAval(Guid AffilieId, string Type);

/// <summary>Examen à planifier de la personne du jeton (<c>GET /api/v1/mes-examens-a-planifier</c>) ; seuls les champs utiles au portail.</summary>
public sealed record ExamenAPlanifierAval(
    Guid Id,
    Guid PersonneId,
    Guid AffilieId,
    string Type,
    string TypeLibelle,
    DateOnly DateDue,
    DateOnly? DateLimite,
    string Statut,
    bool EnRetard);

// --- Service Personnes (port 5112) ---

/// <summary>
/// Occupation active de la personne du jeton (<c>GET /api/v1/mes-occupations</c>) : l'affilié, le type de travailleur et les
/// dates. Personnes ne connaît pas la dénomination de l'employeur ; ni NISS ni identité ne sont déclarés.
/// </summary>
public sealed record OccupationAval(Guid OccupationId, Guid AffilieId, string TypeTravailleur, DateOnly DateDebut, DateOnly? DateFin);

// --- Service Documents (port 5119) ---

/// <summary>Métadonnées d'un document : ni empreinte, ni zone, ni stockage, ni horodatage ne sont relayés.</summary>
public sealed record DocumentAval(
    Guid Id,
    string CodeModele,
    string Langue,
    string TypeDestinataire,
    Guid DestinataireId,
    string? Exemplaire,
    string Format,
    long Taille,
    DateTimeOffset Date,
    string Statut,
    DateTimeOffset? PublieLe);
