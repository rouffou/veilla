using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Application;
using Sepp.Documents.Domain.Langues;

namespace Sepp.Documents.Adapters.Externe;

/// <summary>
/// Horodatage simulé : jeton calculé localement sur l'empreinte et l'heure du serveur, <b>sans valeur probante</b>.
/// En production, un prestataire de services de confiance qualifié (horodatage RFC 3161, eIDAS) est requis.
/// </summary>
public sealed class HorodatageSimule(TimeProvider horloge) : IServiceHorodatage
{
    public const string Autorite = "simulateur (sans valeur probante)";

    public Task<Horodatage> HorodaterAsync(string empreinte, CancellationToken cancellationToken)
    {
        var date = horloge.GetUtcNow();
        var jeton = JsonSerializer.SerializeToUtf8Bytes(new { empreinte, date, autorite = Autorite, algorithme = "SHA-256" });
        return Task.FromResult(new Horodatage(date, Autorite, Convert.ToBase64String(jeton)));
    }
}

/// <summary>
/// Adaptateur réel d'horodatage qualifié (RFC 3161) : à raccorder au prestataire retenu (liste de confiance belge des
/// prestataires qualifiés). Non implémenté : le prestataire, son point d'accès et ses certificats ne sont pas connus.
/// </summary>
public sealed class HorodatageQualifie : IServiceHorodatage
{
    public Task<Horodatage> HorodaterAsync(string empreinte, CancellationToken cancellationToken) =>
        throw new NotImplementedException("Horodatage qualifié RFC 3161 : prestataire de services de confiance à désigner (voir README du service Documents).");
}

/// <summary>
/// Signature qualifiée simulée : preuve détachée portant sur l'empreinte du document, <b>sans valeur juridique</b>.
/// La signature réelle (eID, itsme, via un prestataire qualifié eIDAS) est hors périmètre de ce lot.
/// </summary>
public sealed class SignatureSimulee(TimeProvider horloge) : ISignatureQualifiee
{
    public const string Type = "qualifiee-simulee";

    public Task<PreuveSignature> SignerAsync(Guid documentId, string empreinte, string signataireId, CancellationToken cancellationToken)
    {
        var date = horloge.GetUtcNow();
        var preuve = JsonSerializer.SerializeToUtf8Bytes(new
        {
            document = documentId,
            empreinte,
            signataire = signataireId,
            date,
            avertissement = "SIMULATION — sans valeur juridique",
            sceau = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{documentId:N}|{empreinte}|{signataireId}|{date:O}"))),
        });
        return Task.FromResult(new PreuveSignature(Type, date, Convert.ToBase64String(preuve)));
    }
}

/// <summary>Adaptateur réel de signature qualifiée (eID, itsme) : hors périmètre, prestataire à désigner.</summary>
public sealed class SignatureQualifieeReelle : ISignatureQualifiee
{
    public Task<PreuveSignature> SignerAsync(Guid documentId, string empreinte, string signataireId, CancellationToken cancellationToken) =>
        throw new NotImplementedException("Signature électronique qualifiée (eID, itsme) : prestataire à désigner (voir README du service Documents).");
}

/// <summary>Données linguistiques fictives et déterministes (développement, tests) : régime français, langue du travailleur inconnue.</summary>
public sealed class SourceLinguistiqueSimulee : ISourceLinguistique
{
    public Task<InformationsLinguistiques> ObtenirAsync(Guid? affilieId, Guid? personneId, CancellationToken cancellationToken) =>
        Task.FromResult(new InformationsLinguistiques(affilieId is null ? null : RegimeLinguistique.Francais, affilieId is null ? null : Language.Fr, null));
}

/// <summary>
/// NF-41 : régime linguistique et langue de l'affilié lus par <c>GET /api/v1/affilies/{id}</c> (service Affiliés), langue
/// du travailleur par <c>GET /api/v1/personnes/{id}</c> (service Personnes), avec le jeton du compte technique (rôle
/// <c>documents</c> : <c>affilie:lire</c>, <c>personne:lire</c>). Une personne ou un affilié introuvable laisse la langue
/// indéterminée (la règle se rabat sur les autres éléments) ; toute autre erreur fait échouer la génération (reprise).
/// </summary>
internal sealed class SourceLinguistiqueHttp(IHttpClientFactory fabrique) : ISourceLinguistique
{
    public async Task<InformationsLinguistiques> ObtenirAsync(Guid? affilieId, Guid? personneId, CancellationToken cancellationToken)
    {
        RegimeLinguistique? regime = null;
        Language? langueAffilie = null;
        Language? langueTravailleur = null;
        if (affilieId is { } affilie && await LireAsync(ClientsHttp.Affilies, $"api/v1/affilies/{affilie}", cancellationToken) is { } fiche
            && fiche.TryGetProperty("fiche", out var detail))
        {
            regime = Enumeration<RegimeLinguistique>(detail, "regimeLinguistique");
            langueAffilie = Enumeration<Language>(detail, "langue");
        }

        if (personneId is { } personne && await LireAsync(ClientsHttp.Personnes, $"api/v1/personnes/{personne}", cancellationToken) is { } travailleur)
        {
            langueTravailleur = Enumeration<Language>(travailleur, "langue");
        }

        return new InformationsLinguistiques(regime, langueAffilie, langueTravailleur);
    }

    private async Task<JsonElement?> LireAsync(string client, string chemin, CancellationToken cancellationToken)
    {
        var http = fabrique.CreateClient(client);
        if (http.BaseAddress is null)
        {
            throw new InvalidOperationException($"Adresse du service interne non configurée ({client}, Documents:ServicesInternes).");
        }

        using var reponse = await http.GetAsync(new Uri(chemin, UriKind.Relative), cancellationToken);
        if (reponse.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        reponse.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await reponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    private static T? Enumeration<T>(JsonElement element, string propriete)
        where T : struct, Enum =>
        element.TryGetProperty(propriete, out var valeur) && valeur.ValueKind == JsonValueKind.String && Enum.TryParse<T>(valeur.GetString(), ignoreCase: true, out var resultat)
            ? resultat
            : null;
}

internal static class AdaptateursExternes
{
    public static void AjouterClientsInternes(IServiceCollection services, OptionsServicesInternes adresses, OptionsCompteTechnique compte)
    {
        services.AddSingleton(compte);
        services.AddSingleton<FournisseurJetonTechnique>();
        services.AddTransient<JetonTechniqueHandler>();
        services.AddHttpClient(ClientsHttp.JetonOidc);
        services.AddHttpClient(ClientsHttp.Affilies, c => c.BaseAddress = adresses.Affilies).AddHttpMessageHandler<JetonTechniqueHandler>();
        services.AddHttpClient(ClientsHttp.Personnes, c => c.BaseAddress = adresses.Personnes).AddHttpMessageHandler<JetonTechniqueHandler>();
        services.AddScoped<ISourceLinguistique, SourceLinguistiqueHttp>();
    }
}
