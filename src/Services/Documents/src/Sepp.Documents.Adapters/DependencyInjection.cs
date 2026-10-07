using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Infrastructure;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.Documents.Adapters.Chiffrement;
using Sepp.Documents.Adapters.Externe;
using Sepp.Documents.Adapters.Pdf;
using Sepp.Documents.Adapters.Persistence;
using Sepp.Documents.Adapters.Securite;
using Sepp.Documents.Adapters.Stockage;
using Sepp.Documents.Application;
using Sepp.Documents.Application.EvaluationSante;

namespace Sepp.Documents.Adapters;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Documents";

    /// <summary>Valeur de <c>Documents:Adaptateurs:&lt;Port&gt;</c> sélectionnant le simulateur (sans valeur probante ni juridique).</summary>
    public const string Simulateur = "Simulateur";

    /// <summary>Valeur de <c>Documents:Adaptateurs:&lt;Port&gt;</c> sélectionnant l'adaptateur réel.</summary>
    public const string Reel = "Reel";

    public static IServiceCollection AddDocumentsAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSeppPersistence<DocumentsDbContext>(configuration, ConnectionStringName);
        services.AddSeppMessaging(configuration);
        services.AddScoped<IModeleRepository, ModeleRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();

        services.AddSingleton<IRenduPdf, RenduPdfA>();
        services.AddSingleton<IChiffrementDocuments, ChiffrementParZone>();
        services.AddScoped<IPerimetreExterne, PerimetreJeton>();
        services.AddScoped<IJournalAcces, JournalAccesOutbox>();

        AddStockage(services, configuration);
        AddAdaptateurs(services, configuration);

        // Saga de reprise (§14.6 étape 5) : un formulaire d'évaluation de santé par décision émise (ARC-31).
        services.AddIntegrationEventHandler<DecisionEmise, DecisionEmiseHandler>();
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

        switch (Choix(configuration, "SourceLinguistique"))
        {
            case Simulateur:
                services.AddSingleton<ISourceLinguistique, SourceLinguistiqueSimulee>();
                break;
            default:
                AdaptateursExternes.AjouterClientsInternes(
                    services,
                    configuration.GetSection("Documents:ServicesInternes").Get<OptionsServicesInternes>() ?? new OptionsServicesInternes(),
                    configuration.GetSection("Documents:CompteTechnique").Get<OptionsCompteTechnique>() ?? new OptionsCompteTechnique());
                break;
        }
    }

    private static string Choix(IConfiguration configuration, string port)
    {
        var valeur = configuration[$"Documents:Adaptateurs:{port}"];
        return valeur switch
        {
            Simulateur or Reel => valeur,
            _ => throw new InvalidOperationException(
                $"Documents:Adaptateurs:{port} doit valoir « {Simulateur} » ou « {Reel} » ; valeur actuelle : « {valeur} »."),
        };
    }
}
