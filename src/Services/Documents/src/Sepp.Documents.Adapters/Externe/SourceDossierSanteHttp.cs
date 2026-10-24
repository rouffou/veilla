using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Sepp.Documents.Application.DossierSante;

namespace Sepp.Documents.Adapters.Externe;

/// <summary>
/// SAN-43, SAN-50 : lecture, auprès du service Surveillance médicale (zone médicale), du contenu à publier au travailleur
/// — copie validée (<c>GET /api/v1/demandes-copie/{id}/contenu</c>) et carnet de vaccination
/// (<c>GET /api/v1/vaccinations/{id}/carnet</c>) — avec le jeton du compte technique (rôle <c>documents</c>, permission
/// <c>dossier-sante:transmettre-documents</c>). Chaque lecture est journalisée par la Surveillance médicale. Le contenu
/// n'est ni journalisé ni conservé ici : il est fusionné, chiffré avec la clé de la zone médicale et archivé.
/// </summary>
internal sealed class SourceDossierSanteHttp(IHttpClientFactory fabrique) : ISourceDossierSante
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task<CopieDossierMedicale> ObtenirCopieAsync(Guid demandeId, CancellationToken cancellationToken)
    {
        using var reponse = await Client().GetAsync(new Uri($"api/v1/demandes-copie/{demandeId}/contenu", UriKind.Relative), cancellationToken);
        if (!reponse.IsSuccessStatusCode)
        {
            // Sans contenu : aucun détail de la réponse n'est repris (secret médical).
            throw new InvalidOperationException($"Copie {demandeId} non transmise par la Surveillance médicale (HTTP {(int)reponse.StatusCode}).");
        }

        return await reponse.Content.ReadFromJsonAsync<CopieDossierMedicale>(Json, cancellationToken)
            ?? throw new InvalidOperationException($"Copie {demandeId} : réponse vide.");
    }

    public async Task<CarnetVaccination?> ObtenirCarnetAsync(Guid vaccinationId, CancellationToken cancellationToken)
    {
        using var reponse = await Client().GetAsync(new Uri($"api/v1/vaccinations/{vaccinationId}/carnet", UriKind.Relative), cancellationToken);
        if (reponse.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!reponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Carnet de la vaccination {vaccinationId} non transmis par la Surveillance médicale (HTTP {(int)reponse.StatusCode}).");
        }

        return await reponse.Content.ReadFromJsonAsync<CarnetVaccination>(Json, cancellationToken);
    }

    private HttpClient Client()
    {
        var http = fabrique.CreateClient(ClientsHttp.SurveillanceMedicale);
        return http.BaseAddress is null
            ? throw new InvalidOperationException("Adresse du service Surveillance médicale non configurée (Documents:ServicesInternes:SurveillanceMedicale).")
            : http;
    }
}
