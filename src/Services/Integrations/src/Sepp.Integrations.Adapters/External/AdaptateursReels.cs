using Sepp.Integrations.Application.Externe;
using Sepp.Integrations.Domain.Bce;

namespace Sepp.Integrations.Adapters.External;

// Adaptateurs réels des organismes — SQUELETTES. Les services, versions, schémas de message et modes d'échange
// exacts sont à confirmer auprès de chaque organisme lors de l'analyse détaillée (INT-04, §12) : aucun format n'est
// supposé ici. Chaque méthode lève NotImplementedException avec la documentation à obtenir ; l'implémentation
// traduira le format de l'organisme vers le format canonique du port (couche anti-corruption) sans toucher au cœur.

/// <summary>Documentation et autorisations à obtenir avant d'implémenter chaque adaptateur réel (voir README, INT-03, INT-04).</summary>
public static class DocumentationAObtenir
{
    public const string Bce =
        "Adaptateur BCE réel non implémenté (INT-04) : obtenir auprès du SPF Économie (Banque-Carrefour des Entreprises) " +
        "la documentation du service d'accès aux données des entreprises et des unités d'établissement retenu (service, version, " +
        "schéma, authentification, limites d'usage), puis traduire ses réponses vers DonneesEntreprise.";

    public const string Dimona =
        "Adaptateur DIMONA / DmfA réel non implémenté (INT-04) : obtenir auprès de la BCSS la documentation des services de " +
        "mise à disposition des déclarations DIMONA et DmfA pour un SEPP (service, version, schéma, mode d'échange et de reprise), " +
        "la délibération du Comité de sécurité de l'information autorisant le SEPP (INT-03) et le certificat d'accès, " +
        "puis traduire les déclarations vers DeclarationDimona (codes de type de travailleur et de contrat compris).";

    public const string RegistreNational =
        "Adaptateur BCSS identification réel non implémenté (INT-04) : obtenir auprès de la BCSS la documentation des services de " +
        "consultation du registre national (ou des registres BCSS) et de communication des mutations (service, version, schéma), " +
        "la délibération du Comité de sécurité de l'information (INT-03), les modalités d'intégration du SEPP communiquées par la BCSS " +
        "et le certificat d'accès, puis traduire vers IdentiteRegistreNational et MutationRegistreNational.";
}

/// <summary>Accès aux services de la BCSS : point d'accès et certificat (configuration <c>Integrations:Bcss</c>, voir README).</summary>
public sealed class ConfigurationBcss
{
    /// <summary>Adresse du service communiquée par la BCSS (environnement d'acceptation puis de production).</summary>
    public Uri? Adresse { get; set; }

    /// <summary>Nom du certificat client dans Azure Key Vault (jamais le certificat lui-même dans la configuration, CTR-16).</summary>
    public string? NomCertificatKeyVault { get; set; }

    /// <summary>Identifiant d'entité du SEPP auprès de la BCSS, communiqué par la BCSS lors de l'intégration.</summary>
    public string? IdentifiantExpediteur { get; set; }
}

public sealed class BceAdaptateurReel : IRegistreBce
{
    public Task<DonneesEntreprise?> ConsulterEntrepriseAsync(string numeroBce, CancellationToken cancellationToken) =>
        throw new NotImplementedException(DocumentationAObtenir.Bce);
}

public sealed class DimonaAdaptateurReel(ConfigurationBcss configuration) : IFluxDimona
{
    public ConfigurationBcss Configuration { get; } = configuration;

    public Task<LotFlux<DeclarationDimona>> RecupererDeclarationsAsync(string? position, CancellationToken cancellationToken) =>
        throw new NotImplementedException(DocumentationAObtenir.Dimona);
}

public sealed class RegistreNationalAdaptateurReel(ConfigurationBcss configuration) : IRegistreNational
{
    public ConfigurationBcss Configuration { get; } = configuration;

    public Task<IdentiteRegistreNational?> ConsulterIdentiteAsync(string niss, CancellationToken cancellationToken) =>
        throw new NotImplementedException(DocumentationAObtenir.RegistreNational);

    public Task<LotFlux<MutationRegistreNational>> RecupererMutationsAsync(string? position, CancellationToken cancellationToken) =>
        throw new NotImplementedException(DocumentationAObtenir.RegistreNational);
}
