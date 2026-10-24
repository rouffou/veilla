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

    /// <summary>Annulation avec raison codée et précision libre courte, relayées telles quelles : le service les valide (jamais publiées).</summary>
    public Task AnnulerAsync(Guid rendezVousId, AnnulationCorps? corps, CancellationToken ct) =>
        planification.AnnulerAsync(rendezVousId, new AnnulationCorpsAval(corps?.Raison, corps?.Commentaire), ct);

    /// <summary>
    /// Déplacement atomique relayé à Planification (une seule requête : ancien créneau libéré et nouveau réservé, ou 409) ;
    /// délai minimal, nombre de déplacements et périmètre sont ceux du service. Jamais rejoué automatiquement.
    /// </summary>
    public async Task<RendezVousEcran> DeplacerAsync(Guid personneId, Guid rendezVousId, DeplacementCorps corps, CancellationToken ct) =>
        Projeter(await planification.DeplacerAsync(rendezVousId, corps.NouveauCreneauId, ct), personneId);

    /// <summary>Le travailleur confirme sa présence (enregistrée par le service, visible du planificateur).</summary>
    public async Task<RendezVousEcran> ConfirmerAsync(Guid personneId, Guid rendezVousId, CancellationToken ct) =>
        Projeter(await planification.ConfirmerAsync(rendezVousId, ct), personneId);

    private static RendezVousEcran Projeter(RendezVousAval r, Guid personneId) =>
        r.PersonneId == personneId
            ? Ecran(r)
            : throw new ErreurAvalException(NomsServices.Planification, HttpStatusCode.NotFound, "rendez-vous.inconnu", "Rendez-vous inconnu.");

    private static RendezVousEcran Ecran(RendezVousAval r) =>
        new(r.Id, r.AffilieId, r.LieuId, r.Debut, r.Fin, r.TypeActe, r.Statut, r.MotifAnnulation, r.PresenceConfirmeeLe, r.NombreDeplacements);

    private static List<RendezVousEcran> Projeter(IReadOnlyList<RendezVousAval> rendezVous, Guid personneId) =>
        rendezVous
            .Where(r => r.PersonneId == personneId)
            .OrderBy(r => r.Debut)
            .Select(Ecran)
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

        // La personne n'est pas transmise : Obligations la lit dans le jeton propagé (claim personne_id).
        return new(await obligations.EnregistrerDemandeAsync(new MaDemandeCorpsAval(corps.AffilieId, type), ct));
    }
}

/// <summary>
/// POR-11 : employeurs du travailleur (ses occupations actives) et examens à planifier. Ces deux listes permettent une première
/// réservation sans lien de convocation. Les services bornent déjà leurs réponses à la personne du jeton ; le BFF écarte en
/// plus tout examen dont la personne n'est pas celle du jeton (défense en profondeur).
/// </summary>
public sealed class EcransEmployeursEtExamens(IPersonnesApi personnes, IAffiliesApi affilies, IObligationsApi obligations)
{
    /// <summary>
    /// Employeurs distincts (un par affilié), le plus ancien d'abord, nommés par Affiliés. Les deux appels partent ensemble ;
    /// sans réponse d'Affiliés (indisponible, refus), l'employeur reste listé sans dénomination (ARC-35) et seules les
    /// dénominations des affiliés de ses occupations sont retenues (défense en profondeur). Le BFF ne décide d'aucun périmètre.
    /// </summary>
    public async Task<IReadOnlyList<EmployeurEcran>> MesEmployeursAsync(CancellationToken ct)
    {
        var occupations = personnes.ListerMesOccupationsAsync(ct);
        var denominations = DenominationsAsync(ct);
        var liste = await occupations;
        var noms = (await denominations).GroupBy(e => e.AffilieId).ToDictionary(g => g.Key, g => g.First());

        return liste
            .GroupBy(o => o.AffilieId)
            .Select(g => g.OrderBy(o => o.DateDebut).First())
            .OrderBy(o => o.DateDebut)
            .ThenBy(o => o.AffilieId)
            .Select(o =>
            {
                noms.TryGetValue(o.AffilieId, out var employeur);
                return new EmployeurEcran(o.AffilieId, employeur?.Denomination, employeur?.Langue, o.TypeTravailleur, o.DateDebut, o.DateFin);
            })
            .ToList();
    }

    private async Task<IReadOnlyList<EmployeurAval>> DenominationsAsync(CancellationToken ct)
    {
        try
        {
            return await affilies.ListerMesEmployeursAsync(ct);
        }
        catch (Exception ex) when (ex is ServiceIndisponibleException or ErreurAvalException)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<ExamenAPlanifierEcran>> MesExamensAPlanifierAsync(Guid personneId, string langue, CancellationToken ct) =>
        (await obligations.ListerMesExamensAPlanifierAsync(langue, ct))
        .Where(e => e.PersonneId == personneId)
        .OrderBy(e => e.DateLimite ?? e.DateDue)
        .ThenBy(e => e.Id)
        .Select(e => new ExamenAPlanifierEcran(e.Id, e.AffilieId, e.Type, e.TypeLibelle, e.DateDue, e.DateLimite, e.Statut, e.EnRetard))
        .ToList();
}

/// <summary>
/// POR-13 : documents publiés pour le travailleur par le service Documents (formulaire d'évaluation de santé, exemplaire du
/// travailleur). Ce sont ses propres documents (§3.3) ; Documents journalise chaque lecture de contenu (NF-04).
/// </summary>
public sealed class EcransDocuments(IDocumentsApi documents)
{
    public const string CategorieEvaluationSante = "evaluation-sante";
    public const string CategorieCarnetVaccination = "carnet-vaccination";
    public const string CategorieCopieDossier = "copie-dossier";
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
        new(d.Id, Categorie(d.CodeModele), d.CodeModele, d.Langue, d.PublieLe ?? d.Date, d.Format, d.Taille);

    /// <summary>Catégorie affichée (POR-13) : formulaire d'évaluation de santé, carnet de vaccination (SAN-50), copie du dossier (SAN-43).</summary>
    internal static string Categorie(string codeModele) => codeModele switch
    {
        _ when codeModele.StartsWith("SANTE.EVALUATION", StringComparison.Ordinal) => CategorieEvaluationSante,
        "SANTE.CARNET_VACCINATION" => CategorieCarnetVaccination,
        "SANTE.COPIE_DOSSIER" => CategorieCopieDossier,
        _ => CategorieAutre,
    };
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
