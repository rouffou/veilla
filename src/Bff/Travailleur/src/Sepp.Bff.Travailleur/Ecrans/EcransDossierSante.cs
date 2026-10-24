using Sepp.Bff.Travailleur.Aval;
using Sepp.Bff.Travailleur.Securite;

namespace Sepp.Bff.Travailleur.Ecrans;

/// <summary>
/// Suivi d'une demande de copie du dossier (SAN-43) : statut, date limite légale (à valider), retard, motif de refus codé,
/// document publié (à télécharger par <c>GET /documents/{id}/contenu</c>). Aucun contenu médical.
/// </summary>
public sealed record DemandeCopieEcran(
    Guid Id,
    DateTimeOffset DeposeeLe,
    DateOnly DateLimite,
    string Statut,
    bool EnRetard,
    string? MotifRefus,
    Guid? DocumentId,
    DateTimeOffset? PublieeLe);

/// <summary>
/// POR-13, SAN-43 : demande de copie du dossier de santé par le travailleur et suivi de sa demande. Le BFF ne lit jamais
/// le dossier (§3.3 « copie sur demande ») : le CPMT valide la copie dans la Surveillance médicale, le service Documents la
/// publie au travailleur en zone médicale et le portail la télécharge comme tout autre document. Le carnet de vaccination
/// (SAN-50) est publié par Documents après chaque vaccination et apparaît dans « Mes documents ».
/// </summary>
public sealed class EcransDossierSante(IDemandesCopieApi demandes)
{
    public async Task<DemandeCopieEcran> DemanderCopieAsync(Guid personneId, CancellationToken ct) =>
        ToEcran(await demandes.DeposerAsync(ct), personneId)
        ?? throw new ErreurAvalException(NomsServices.SurveillanceMedicale, System.Net.HttpStatusCode.BadGateway, "demande-copie.perimetre", null);

    /// <summary>Les demandes d'une autre personne (défense en profondeur) sont écartées.</summary>
    public async Task<IReadOnlyList<DemandeCopieEcran>> MesDemandesAsync(Guid personneId, CancellationToken ct) =>
        (await demandes.ListerMiennesAsync(ct))
        .Select(d => ToEcran(d, personneId))
        .OfType<DemandeCopieEcran>()
        .OrderByDescending(d => d.DeposeeLe)
        .ToList();

    private static DemandeCopieEcran? ToEcran(DemandeCopieAval d, Guid personneId) =>
        d.PersonneId != personneId ? null : new(d.Id, d.DeposeeLe, d.DateLimite, d.Statut, d.EnRetard, d.MotifRefus, d.DocumentId, d.PublieeLe);
}

public static class EcransDossierSanteEndpoints
{
    public static void MapDossierSante(this RouteGroupBuilder api)
    {
        api.MapGet("/demandes-copie-dossier", async (HttpContext http, EcransDossierSante ecrans, CancellationToken ct) =>
                Results.Ok(await ecrans.MesDemandesAsync(PerimetreTravailleur.PersonneObligatoire(http), ct)))
            .WithName("MesDemandesCopieDossier")
            .WithSummary("POR-13, SAN-43 : suivi de mes demandes de copie du dossier de santé (statut, date limite, document publié).");

        api.MapPost("/demandes-copie-dossier", async (HttpContext http, EcransDossierSante ecrans, CancellationToken ct) =>
            {
                var demande = await ecrans.DemanderCopieAsync(PerimetreTravailleur.PersonneObligatoire(http), ct);
                return Results.Created($"/api/v1/demandes-copie-dossier#{demande.Id}", demande);
            })
            .WithName("DemanderCopieDossier")
            .WithSummary("POR-13, SAN-43 : demande de copie de mon dossier de santé, traitée par le CPMT ; jamais rejouée automatiquement.");
    }
}
