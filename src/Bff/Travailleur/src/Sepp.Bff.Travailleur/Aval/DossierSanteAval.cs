namespace Sepp.Bff.Travailleur.Aval;

// --- Service Surveillance médicale (port 5118), routes /api/v1/demandes-copie (SAN-43, POR-13) ---

/// <summary>
/// Demande de copie du dossier vue par le travailleur : dates, statut, motif de refus codé et document publié. Aucun
/// contenu médical n'existe dans cette réponse ; le CPMT qui l'a traitée n'est pas déclaré, donc jamais relayé.
/// </summary>
public sealed record DemandeCopieAval(
    Guid Id,
    Guid PersonneId,
    DateTimeOffset DeposeeLe,
    DateOnly DateLimite,
    string Statut,
    bool EnRetard,
    DateTimeOffset? TraiteeLe,
    string? MotifRefus,
    Guid? DocumentId,
    DateTimeOffset? PublieeLe);

/// <summary>
/// Droit d'accès du travailleur à son dossier de santé (§3.3 « copie sur demande », SAN-43) : dépôt et suivi de la demande,
/// sans aucune lecture du dossier. La copie elle-même est publiée par le service Documents et téléchargée par
/// <see cref="IDocumentsApi"/>.
/// </summary>
public interface IDemandesCopieApi
{
    Task<DemandeCopieAval> DeposerAsync(CancellationToken ct);

    Task<IReadOnlyList<DemandeCopieAval>> ListerMiennesAsync(CancellationToken ct);
}

internal sealed class DemandesCopieApi(HttpClient http) : ClientAval(http, NomsServices.SurveillanceMedicale), IDemandesCopieApi
{
    // La personne est celle du jeton propagé (claim personne_id), revérifiée par le service : aucun identifiant n'est envoyé.
    public Task<DemandeCopieAval> DeposerAsync(CancellationToken ct) => PostAsync<DemandeCopieAval>("api/v1/demandes-copie", new { }, ct);

    public Task<IReadOnlyList<DemandeCopieAval>> ListerMiennesAsync(CancellationToken ct) =>
        GetAsync<IReadOnlyList<DemandeCopieAval>>("api/v1/demandes-copie/miennes", ct);
}
