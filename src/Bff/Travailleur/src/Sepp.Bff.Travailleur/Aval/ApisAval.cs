namespace Sepp.Bff.Travailleur.Aval;

/// <summary>Service Planification : réservation en ligne du travailleur (SAN-12, POR-11).</summary>
public interface IPlanificationApi
{
    Task<IReadOnlyList<RendezVousAval>> ListerMesRendezVousAsync(CancellationToken ct);

    Task<IReadOnlyList<CreneauOuvertAval>> ListerCreneauxOuvertsAsync(
        Guid affilieId, string typeActe, DateTimeOffset du, DateTimeOffset au, Guid? lieuId, CancellationToken ct);

    Task<Guid> ReserverAsync(ReservationCorpsAval corps, CancellationToken ct);

    Task AnnulerAsync(Guid rendezVousId, CancellationToken ct);
}

/// <summary>Service Surveillance médicale : modèles de questionnaire et pré-remplissage en écriture seule (SAN-22, POR-12).</summary>
public interface ISurveillanceMedicaleApi
{
    Task<IReadOnlyList<ModeleQuestionnaireAval>> ListerModelesQuestionnaireAsync(string langue, CancellationToken ct);

    Task<Guid> PreRemplirQuestionnaireAsync(PreRemplissageCorpsAval corps, CancellationToken ct);
}

/// <summary>Service Obligations : demandes du travailleur (consultation spontanée, pré-reprise) (§5.1, POR-12).</summary>
public interface IObligationsApi
{
    Task<Guid> EnregistrerDemandeAsync(DemandeCorpsAval corps, CancellationToken ct);
}

/// <summary>Service Documents : documents publiés pour le travailleur (POR-13).</summary>
public interface IDocumentsApi
{
    Task<IReadOnlyList<DocumentAval>> ListerDocumentsDeLaPersonneAsync(Guid personneId, CancellationToken ct);

    Task<DocumentAval> ObtenirDocumentAsync(Guid documentId, CancellationToken ct);

    Task<(byte[] Contenu, string? NomFichier)> LireContenuAsync(Guid documentId, CancellationToken ct);
}

public static class NomsServices
{
    public const string Planification = "planification";
    public const string SurveillanceMedicale = "surveillance-medicale";
    public const string Obligations = "obligations";
    public const string Documents = "documents";
}

internal sealed class PlanificationApi(HttpClient http) : ClientAval(http, NomsServices.Planification), IPlanificationApi
{
    public Task<IReadOnlyList<RendezVousAval>> ListerMesRendezVousAsync(CancellationToken ct) =>
        GetAsync<IReadOnlyList<RendezVousAval>>("api/v1/reservations/rendez-vous", ct);

    public Task<IReadOnlyList<CreneauOuvertAval>> ListerCreneauxOuvertsAsync(
        Guid affilieId, string typeActe, DateTimeOffset du, DateTimeOffset au, Guid? lieuId, CancellationToken ct) =>
        GetAsync<IReadOnlyList<CreneauOuvertAval>>(
            $"api/v1/reservations/creneaux?affilieId={affilieId}&typeActe={Uri.EscapeDataString(typeActe)}"
            + $"&du={Uri.EscapeDataString(du.ToString("O", System.Globalization.CultureInfo.InvariantCulture))}"
            + $"&au={Uri.EscapeDataString(au.ToString("O", System.Globalization.CultureInfo.InvariantCulture))}"
            + (lieuId is { } lieu ? $"&lieuId={lieu}" : string.Empty),
            ct);

    public async Task<Guid> ReserverAsync(ReservationCorpsAval corps, CancellationToken ct) =>
        (await PostAsync<IdentifiantAval>("api/v1/reservations", corps, ct)).Id;

    public Task AnnulerAsync(Guid rendezVousId, CancellationToken ct) =>
        PostSansReponseAsync($"api/v1/reservations/rendez-vous/{rendezVousId}/annulation", ct);
}

internal sealed class SurveillanceMedicaleApi(HttpClient http) : ClientAval(http, NomsServices.SurveillanceMedicale), ISurveillanceMedicaleApi
{
    public Task<IReadOnlyList<ModeleQuestionnaireAval>> ListerModelesQuestionnaireAsync(string langue, CancellationToken ct) =>
        GetAsync<IReadOnlyList<ModeleQuestionnaireAval>>($"api/v1/protocoles/questionnaires?langue={Uri.EscapeDataString(langue)}", ct);

    public async Task<Guid> PreRemplirQuestionnaireAsync(PreRemplissageCorpsAval corps, CancellationToken ct) =>
        (await PostAsync<IdentifiantAval>("api/v1/questionnaires/pre-remplissage", corps, ct)).Id;
}

internal sealed class ObligationsApi(HttpClient http) : ClientAval(http, NomsServices.Obligations), IObligationsApi
{
    public async Task<Guid> EnregistrerDemandeAsync(DemandeCorpsAval corps, CancellationToken ct) =>
        (await PostAsync<IdentifiantAval>("api/v1/demandes-travailleur", corps, ct)).Id;
}

internal sealed class DocumentsApi(HttpClient http) : ClientAval(http, NomsServices.Documents), IDocumentsApi
{
    public Task<IReadOnlyList<DocumentAval>> ListerDocumentsDeLaPersonneAsync(Guid personneId, CancellationToken ct) =>
        GetAsync<IReadOnlyList<DocumentAval>>($"api/v1/documents?typeDestinataire=Personne&destinataireId={personneId}", ct);

    public Task<DocumentAval> ObtenirDocumentAsync(Guid documentId, CancellationToken ct) =>
        GetAsync<DocumentAval>($"api/v1/documents/{documentId}", ct);

    public Task<(byte[] Contenu, string? NomFichier)> LireContenuAsync(Guid documentId, CancellationToken ct) =>
        GetOctetsAsync($"api/v1/documents/{documentId}/contenu", ct);
}
