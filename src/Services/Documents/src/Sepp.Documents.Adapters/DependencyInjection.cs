using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Sepp.BuildingBlocks.Infrastructure;
using Sepp.Contracts.Affilies;
using Sepp.Contracts.Personnes;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.Documents.Adapters.Chiffrement;
using Sepp.Documents.Adapters.Externe;
using Sepp.Documents.Adapters.Langues;
using Sepp.Documents.Adapters.Pdf;
using Sepp.Documents.Adapters.Persistence;
using Sepp.Documents.Adapters.Securite;
using Sepp.Documents.Adapters.Stockage;
using Sepp.Documents.Application;
using Sepp.Documents.Application.DossierSante;
using Sepp.Documents.Application.EvaluationSante;
using Sepp.Documents.Application.Langues;
using Sepp.Documents.Domain.Commun;

namespace Sepp.Documents.Adapters;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Documents";

    /// <summary>Valeur de <c>Documents:Adaptateurs:&lt;Port&gt;</c> sélectionnant le simulateur (sans valeur probante ni juridique).</summary>
    public const string Simulateur = "Simulateur";

    /// <summary>Valeur de <c>Documents:Adaptateurs:&lt;Port&gt;</c> sélectionnant l'adaptateur réel.</summary>
    public const string Reel = "Reel";

    /// <summary>Valeur de <c>Documents:SourceLinguistique</c> : appels HTTP à Affiliés et Personnes (défaut).</summary>
    public const string SourceLinguistiqueHttp = "Http";

    /// <summary>Valeur de <c>Documents:SourceLinguistique</c> : projection locale alimentée par événements (ADR 0009).</summary>
    public const string SourceLinguistiqueProjection = "Projection";

    public static IServiceCollection AddDocumentsAdapters(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // Zones servies (décision #356) : échec au démarrage si la liste est absente ou si, hors développement et test, elle
        // n'est pas réduite à la zone de l'environnement (SEPP__ZONE).
        var zones = ZonesServies.Depuis(configuration, environment);
        services.AddSingleton(zones);
        services.AddSingleton<IZonesServies>(zones);

        services.AddSeppPersistence<DocumentsDbContext>(configuration, ConnectionStringName);
        services.AddSeppMessaging(configuration);
        services.AddScoped<IModeleRepository, ModeleRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IContexteLinguistiqueRepository, ContexteLinguistiqueRepository>();

        services.AddSingleton<IRenduPdf, RenduPdfA>();
        // Chiffrement par enveloppe (ADR 0009) : clé de données par document, enveloppée par la clé racine de la zone.
        ConfigurationEnveloppe.AjouterEnveloppe(services, configuration, environment, zones);
        services.AddSingleton<IChiffrementDocuments, ChiffrementParZone>();
        // #377 : version de clé racine introuvable ou refusée → 503 et journal d'exploitation, jamais « document altéré ».
        services.AddExceptionHandler<Api.CleRacineIndisponibleExceptionHandler>();
        services.AddScoped<IPerimetreExterne, PerimetreJeton>();
        services.AddScoped<IJournalAcces, JournalAccesOutbox>();

        AddStockage(services, configuration);
        AddAdaptateurs(services, configuration);

        // Saga de reprise (§14.6 étape 5) : un formulaire d'évaluation de santé par décision émise (ARC-31).
        services.AddIntegrationEventHandler<DecisionEmise, DecisionEmiseHandler>();

        // NF-41, ADR 0009 : projection du contexte linguistique (régime et langue de l'affilié, langue du travailleur),
        // tenue par chaque instance ; lue quand Documents:SourceLinguistique vaut « Projection ».
        services.AddIntegrationEventHandler<AffilieCree, ProjectionLinguistiqueHandler>();
        services.AddIntegrationEventHandler<AffilieModifie, ProjectionLinguistiqueHandler>();
        services.AddIntegrationEventHandler<OccupationDebutee, ProjectionLinguistiqueHandler>();
        services.AddIntegrationEventHandler<LangueModifiee, ProjectionLinguistiqueHandler>();

        // POR-13 : copie du dossier validée par le CPMT (SAN-43) et carnet de vaccination après chaque dose (SAN-50),
        // publiés au travailleur en zone médicale ; contenu lu auprès de la Surveillance médicale (compte technique).
        // Enregistrés seulement par une instance qui sert la zone médicale (décision #356).
        if (zones.Sert(ZoneDocument.Medicale))
        {
            services.AddIntegrationEventHandler<CopieDossierValidee, CopieDossierValideeHandler>();
            services.AddIntegrationEventHandler<VaccinationAdministree, VaccinationAdministreeHandler>();
        }

        services.AddSeppConsumer<DocumentsDbContext>(configuration);

        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }

    /// <summary>Stockage des documents : Azure Blob (WORM) ou fichiers locaux, sans défaut implicite (NF-21).</summary>
    private static void AddStockage(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Documents:Stockage").Get<OptionsStockage>() ?? new OptionsStockage();
        services.AddSingleton(options);
        switch (options.Type)
        {
            case "Local":
                services.AddSingleton<StockageLocal>();
                services.AddSingleton<IStockageDocuments>(sp => sp.GetRequiredService<StockageLocal>());
                break;
            case "AzureBlob":
                services.AddSingleton(_ => StockageAzureBlob.Client(
                    configuration.GetConnectionString("DocumentsBlob"), configuration["Documents:BlobEndpoint"]));
                services.AddSingleton<IStockageDocuments, StockageAzureBlob>();
                break;
            default:
                throw new InvalidOperationException(
                    $"Documents:Stockage:Type doit valoir « Local » (fichiers, développement) ou « AzureBlob » (WORM) ; valeur actuelle : « {options.Type} ».");
        }
    }

    /// <summary>Horodatage, signature qualifiée et source linguistique : simulateur ou adaptateur réel selon la configuration.</summary>
    private static void AddAdaptateurs(IServiceCollection services, IConfiguration configuration)
    {
        switch (Choix(configuration, "Horodatage"))
        {
            case Simulateur:
                services.AddSingleton<IServiceHorodatage, HorodatageSimule>();
                break;
            default:
                services.AddSingleton<IServiceHorodatage, HorodatageQualifie>();
                break;
        }

        switch (Choix(configuration, "Signature"))
        {
            case Simulateur:
                services.AddSingleton<ISignatureQualifiee, SignatureSimulee>();
                break;
            default:
                services.AddSingleton<ISignatureQualifiee, SignatureQualifieeReelle>();
                break;
        }

        var adresses = configuration.GetSection("Documents:ServicesInternes").Get<OptionsServicesInternes>() ?? new OptionsServicesInternes();
        var compte = configuration.GetSection("Documents:CompteTechnique").Get<OptionsCompteTechnique>() ?? new OptionsCompteTechnique();

        // En Azure, Terraform injecte SEPP__SERVICES__<NOM> (infra/main.tf, service_urls) : repli sur cette clé.
        adresses.SurveillanceMedicale ??= Uri.TryCreate(configuration["Sepp:Services:Surveillance_Medicale"], UriKind.Absolute, out var sm) ? sm : null;

        switch (Choix(configuration, "SourceLinguistique", reelImplemente: true))
        {
            case Simulateur:
                services.AddSingleton<ISourceLinguistique, SourceLinguistiqueSimulee>();
                break;
            case Reel when ModeSourceLinguistique(configuration) == SourceLinguistiqueProjection:
                // ADR 0009 : l'instance médicale lit la projection locale (aucun appel à Affiliés ni à Personnes).
                services.AddScoped<ISourceLinguistique, SourceLinguistiqueParProjection>();
                break;
            default:
                AdaptateursExternes.AjouterClientsInternes(services, adresses, compte);
                break;
        }

        AdaptateursExternes.AjouterSourceDossierSante(services, adresses, compte);
    }

    /// <summary>
    /// Source de l'adaptateur linguistique réel (<c>Documents:SourceLinguistique</c>, ADR 0009) : <c>Http</c> (défaut, appels à
    /// Affiliés et Personnes avec le compte technique) ou <c>Projection</c> (table locale alimentée par événements, injecté
    /// par Terraform pour l'instance médicale). Toute autre valeur empêche le démarrage.
    /// </summary>
    public static string ModeSourceLinguistique(IConfiguration configuration)
    {
        var valeur = configuration["Documents:SourceLinguistique"];
        return valeur switch
        {
            null or "" => SourceLinguistiqueHttp,
            _ when string.Equals(valeur, SourceLinguistiqueHttp, StringComparison.OrdinalIgnoreCase) => SourceLinguistiqueHttp,
            _ when string.Equals(valeur, SourceLinguistiqueProjection, StringComparison.OrdinalIgnoreCase) => SourceLinguistiqueProjection,
            _ => throw new InvalidOperationException(
                $"Documents:SourceLinguistique doit valoir « {SourceLinguistiqueHttp} » ou « {SourceLinguistiqueProjection} » ; valeur actuelle : « {valeur} »."),
        };
    }

    private static string Choix(IConfiguration configuration, string port, bool reelImplemente = false)
    {
        var valeur = configuration[$"Documents:Adaptateurs:{port}"];
        GardeAdaptateurs.RefuserReelNonImplementeHorsProduction(configuration, $"Documents:Adaptateurs:{port}", valeur, reelImplemente);
        return valeur switch
        {
            Simulateur or Reel => valeur,
            _ => throw new InvalidOperationException(
                $"Documents:Adaptateurs:{port} doit valoir « {Simulateur} » ou « {Reel} » ; valeur actuelle : « {valeur} »."),
        };
    }
}
