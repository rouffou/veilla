using System.Net;

using Sepp.Bff.Travailleur.Aval;
using Sepp.Contracts.Examens;

namespace Sepp.Bff.Travailleur.Ecrans;

/// <summary>
/// POR-11 : rendez-vous du travailleur, relayés vers la réservation en ligne du service Planification (SAN-12). Le BFF ne
/// décide rien : créneaux ouverts, obligations couvertes, périmètre et délai d'annulation (24 h) sont ceux du service.
/// </summary>
public sealed class EcransRendezVous(IPlanificationApi planification, TimeProvider clock)
{
    public async Task<IReadOnlyList<RendezVousEcran>> MesRendezVousAsync(Guid personneId, CancellationToken ct) =>
        Projeter(await planification.ListerMesRendezVousAsync(ct), personneId);

    /// <summary>Rendez-vous planifiés à venir (au plus <paramref name="n"/>), les plus proches d'abord.</summary>
    public async Task<IReadOnlyList<RendezVousEcran>> ProchainsAsync(Guid personneId, int n, CancellationToken ct)
    {
        var maintenant = clock.GetUtcNow();
        return (await MesRendezVousAsync(personneId, ct))
            .Where(r => r.Statut == "Planifie" && r.Debut >= maintenant)
            .Take(n)
            .ToList();
    }

    public async Task<IReadOnlyList<CreneauEcran>> CreneauxAsync(
        Guid affilieId, string typeActe, DateTimeOffset du, DateTimeOffset au, Guid? lieuId, CancellationToken ct) =>
        (await planification.ListerCreneauxOuvertsAsync(affilieId, typeActe, du, au, lieuId, ct))
        .Select(c => new CreneauEcran(c.Id, c.LieuId, c.Debut, c.Fin, c.TypeActe))
        .ToList();

    /// <summary>La personne réservée est celle du jeton : le portail ne peut pas réserver pour un tiers.</summary>
    public async Task<RendezVousReserve> ReserverAsync(Guid personneId, ReservationCorps corps, CancellationToken ct) =>
        new(await planification.ReserverAsync(new ReservationCorpsAval(corps.CreneauId, personneId, corps.AffilieId, corps.ObligationIds), ct));

    public Task AnnulerAsync(Guid rendezVousId, CancellationToken ct) => planification.AnnulerAsync(rendezVousId, ct);

    private static List<RendezVousEcran> Projeter(IReadOnlyList<RendezVousAval> rendezVous, Guid personneId) =>
        rendezVous
            .Where(r => r.PersonneId == personneId)
            .OrderBy(r => r.Debut)
            .Select(r => new RendezVousEcran(r.Id, r.AffilieId, r.LieuId, r.Debut, r.Fin, r.TypeActe, r.Statut, r.MotifAnnulation))
            .ToList();
}

/// <summary>
/// POR-12 : questionnaires de santé à remplir à l'avance (SAN-22, <b>écriture seule</b>) et demandes du travailleur. Le
/// travailleur ne lit jamais son dossier de santé par ce canal : le BFF n'appelle ni le dossier, ni les questionnaires déjà
/// remplis, ni le pré-remplissage par les dernières réponses (cahier §3.3 : copie sur demande, SAN-43, hors portail).
/// </summary>
public sealed class EcransQuestionnaires(ISurveillanceMedicaleApi surveillance, IObligationsApi obligations)
{
    /// <summary>Demandes que le travailleur peut adresser sans passer par son employeur (POR-12).</summary>
    public static readonly IReadOnlyList<string> TypesDemandes = [TypesExamen.ConsultationSpontanee, TypesExamen.VisitePreReprise];

    public async Task<IReadOnlyList<QuestionnaireEcran>> ModelesAsync(string langue, CancellationToken ct) =>
        (await surveillance.ListerModelesQuestionnaireAsync(langue, ct))
        .Select(m => new QuestionnaireEcran(m.Code, m.Version, m.Titre,
            m.Questions.Select(q => new QuestionEcran(q.Code, q.Libelle, q.TypeReponse, q.Obligatoire, q.Choix)).ToList()))
        .ToList();

    /// <summary>Le service valide le modèle et les réponses ; la personne est celle du jeton.</summary>
    public async Task<QuestionnaireEnregistre> RemplirAsync(Guid personneId, string modeleCode, QuestionnaireReponsesCorps corps, CancellationToken ct) =>
        new(await surveillance.PreRemplirQuestionnaireAsync(
            new PreRemplissageCorpsAval(personneId, modeleCode, (corps.Reponses ?? []).Select(r => new ReponseAval(r.CodeQuestion, r.Valeur)).ToList()), ct));

    /// <summary>
    /// Demande de consultation spontanée ou de pré-reprise, relayée au service Obligations (§5.1). Seuls ces deux types sont
    /// offerts par le portail ; l'obligation, son échéance et les droits restent décidés par le service.
    /// </summary>
    public async Task<DemandeEnregistree> DemanderAsync(Guid personneId, DemandeCorps corps, CancellationToken ct)
    {
        var type = (corps.Type ?? string.Empty).Trim().ToUpperInvariant();
        if (!TypesDemandes.Contains(type))
        {
            throw new ErreurAvalException(NomsServices.Obligations, HttpStatusCode.BadRequest, "demande.type-invalide",
                "Le portail n'accepte que la consultation spontanée et la visite de pré-reprise.");
        }

        return new(await obligations.EnregistrerDemandeAsync(new DemandeCorpsAval(personneId, corps.AffilieId, type), ct));
    }
}

/// <summary>
/// POR-13 : documents publiés pour le travailleur par le service Documents (formulaire d'évaluation de santé, exemplaire du
/// travailleur). Ce sont ses propres documents (§3.3) ; Documents journalise chaque lecture de contenu (NF-04).
/// </summary>
public sealed class EcransDocuments(IDocumentsApi documents)
{
    public const string CategorieEvaluationSante = "evaluation-sante";
    public const string CategorieAutre = "autre";

    public async Task<IReadOnlyList<DocumentEcran>> MesDocumentsAsync(Guid personneId, CancellationToken ct) =>
        (await documents.ListerDocumentsDeLaPersonneAsync(personneId, ct))
        .Where(d => EstPourLaPersonne(d, personneId))
        .OrderByDescending(d => d.Date)
        .Select(ToEcran)
        .ToList();

    public async Task<(byte[] Contenu, string NomFichier)> ContenuAsync(Guid personneId, Guid documentId, CancellationToken ct)
    {
        // Un document d'un autre destinataire ou non publié est traité comme inconnu, sans lire son contenu.
        var document = await documents.ObtenirDocumentAsync(documentId, ct);
        if (!EstPourLaPersonne(document, personneId))
        {
            throw new ErreurAvalException(NomsServices.Documents, HttpStatusCode.NotFound, "document.inconnu", "Document inconnu.");
        }

        var (contenu, nom) = await documents.LireContenuAsync(documentId, ct);
        return (contenu, string.IsNullOrWhiteSpace(nom) ? $"document-{documentId}.pdf" : nom);
    }

    private static bool EstPourLaPersonne(DocumentAval d, Guid personneId) =>
        d.Statut == "Publie" && d.TypeDestinataire == "Personne" && d.DestinataireId == personneId;

    internal static DocumentEcran ToEcran(DocumentAval d) =>
        new(d.Id, d.CodeModele.StartsWith("SANTE.EVALUATION", StringComparison.Ordinal) ? CategorieEvaluationSante : CategorieAutre,
            d.CodeModele, d.Langue, d.PublieLe ?? d.Date, d.Format, d.Taille);
}

/// <summary>
/// Accueil du travailleur (composite) : prochains rendez-vous, documents récents, questionnaires disponibles. Un service
/// indisponible n'empêche pas l'écran : sa section est signalée à part.
/// </summary>
public sealed class EcransAccueil(EcransRendezVous rendezVous, EcransDocuments documents, EcransQuestionnaires questionnaires)
{
    public const int NombreAffiche = 3;

    public async Task<AccueilEcran> AccueilAsync(Guid personneId, string langue, CancellationToken ct)
    {
        var prochains = Tolerer(() => rendezVous.ProchainsAsync(personneId, NombreAffiche, ct));
        var recents = Tolerer(async () => (IReadOnlyList<DocumentEcran>)(await documents.MesDocumentsAsync(personneId, ct)).Take(NombreAffiche).ToList());
        var modeles = Tolerer(() => questionnaires.ModelesAsync(langue, ct));
        await Task.WhenAll(prochains, recents, modeles);

        var indisponibles = new List<string>();
        if (prochains.Result is null)
        {
            indisponibles.Add("rendezVous");
        }

        if (recents.Result is null)
        {
            indisponibles.Add("documents");
        }

        if (modeles.Result is null)
        {
            indisponibles.Add("questionnaires");
        }

        return new AccueilEcran(personneId, prochains.Result, recents.Result, modeles.Result?.Count, indisponibles);
    }

    private static async Task<T?> Tolerer<T>(Func<Task<T>> appel)
        where T : class
    {
        try
        {
            return await appel();
        }
        catch (ServiceIndisponibleException)
        {
            return null;
        }
    }
}
