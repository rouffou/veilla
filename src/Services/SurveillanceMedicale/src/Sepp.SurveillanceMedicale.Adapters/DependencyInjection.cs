using System.Text.Json.Serialization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Infrastructure;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Contracts.Obligations;
using Sepp.Contracts.Personnes;
using Sepp.Contracts.Planification;
using Sepp.Contracts.PostesRisques;
using Sepp.Contracts.Prevention;
using Sepp.Contracts.Referentiels;
using Sepp.SurveillanceMedicale.Adapters.External;
using Sepp.SurveillanceMedicale.Adapters.Persistence;
using Sepp.SurveillanceMedicale.Adapters.Securite;
using Sepp.SurveillanceMedicale.Application;
using Sepp.SurveillanceMedicale.Application.Projections;

namespace Sepp.SurveillanceMedicale.Adapters;

public static class DependencyInjection
{
    public const string ConnectionStringName = "SurveillanceMedicale";

    /// <summary>Nom du service émetteur des traces d'audit (zone médicale).</summary>
    public const string NomService = "surveillance-medicale";

    /// <summary>Valeur de <c>SurveillanceMedicale:Adaptateurs:&lt;Port&gt;</c> sélectionnant le simulateur (données fictives).</summary>
    public const string Simulateur = "Simulateur";

    public static IServiceCollection AddSurveillanceMedicaleAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSeppPersistence<SurveillanceMedicaleDbContext>(configuration, ConnectionStringName);
        services.AddSeppMessaging(configuration);

        // NF-04 : chaque accès au dossier de santé est tracé dans la partition médicale du journal d'audit.
        services.AddSeppAuditTrail(NomService, ZonesSensibilite.Medicale);

        // ARC-04, ARC-45 : clés propres à la zone médicale (section ZoneMedicale:Encryption), jamais partagées avec les
        // autres zones ; en Azure, secrets Key Vault protégés par HSM injectés par référence.
        services.AddSingleton(sp => new ChiffrementZoneMedicale(new FieldEncryptor(
            new ConfigurationFieldKeyProvider(sp.GetRequiredService<IConfiguration>().GetSection(ChiffrementZoneMedicale.Section)))));

        services.AddScoped<IDossierSanteRepository, DossierSanteRepository>();
        services.AddScoped<IExamenRepository, ExamenRepository>();
        services.AddScoped<IDecisionRepository, DecisionRepository>();
        services.AddScoped<IProtocolesRepository, ProtocolesRepository>();
        services.AddScoped<ILotVaccinRepository, LotVaccinRepository>();
        services.AddScoped<IDeclarationMpRepository, DeclarationMpRepository>();
        services.AddScoped<ITransfertRepository, TransfertRepository>();
        services.AddScoped<IPurgeDossiers, PurgeDossiers>();
        services.AddScoped<IProjectionRepository, ProjectionRepository>();
        services.AddScoped<IContexteAcces, ContexteAccesHttp>();

        AddAdaptateursExternes(services, configuration);

        // ARC-31 : modèles de lecture alimentés par événements (idempotence par l'inbox et écriture par clé).
        services.AddIntegrationEventHandler<ObligationCreee, ObligationCreeeHandler>();
        services.AddIntegrationEventHandler<RendezVousPlanifie, RendezVousPlanifieHandler>();
        services.AddIntegrationEventHandler<AffectationModifiee, AffectationModifieeHandler>();
        services.AddIntegrationEventHandler<ProfilRisquePosteModifie, ProfilRisquePosteModifieHandler>();
        services.AddIntegrationEventHandler<MesurageEnregistre, MesurageEnregistreHandler>();
        services.AddIntegrationEventHandler<ParametreLegalModifie, ParametreLegalModifieHandler>();
        services.AddSeppConsumer<SurveillanceMedicaleDbContext>(configuration);

        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }

    /// <summary>
    /// Ports externes : seuls les simulateurs existent (prestataires réels hors périmètre, voir le README). Toute autre
    /// valeur de configuration est refusée au démarrage plutôt que de basculer silencieusement sur un simulateur.
    /// </summary>
    private static void AddAdaptateursExternes(IServiceCollection services, IConfiguration configuration)
    {
        foreach (var port in new[] { "Signature", "Fedris", "CanalTransfert" })
        {
            var valeur = configuration[$"SurveillanceMedicale:Adaptateurs:{port}"] ?? Simulateur;
            if (valeur != Simulateur)
            {
                throw new InvalidOperationException(
                    $"SurveillanceMedicale:Adaptateurs:{port} = « {valeur} » : seul le « {Simulateur} » est disponible (adaptateur réel hors périmètre).");
            }
        }

        services.AddSingleton<ISignatureQualifiee, SignatureSimulee>();
        services.AddSingleton<IFedris, FedrisSimule>();
        services.AddSingleton<ICanalTransfertDossier, CanalTransfertSimule>();
        services.AddSingleton<IImportAppareil, ImportHl7>();
        services.AddSingleton<IImportAppareil, ImportFichierCsv>();
        services.AddSingleton<IImportAppareil, SimulateurAppareil>();
    }
}
