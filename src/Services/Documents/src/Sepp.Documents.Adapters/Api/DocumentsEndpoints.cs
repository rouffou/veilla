using System.Globalization;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Web;
using Sepp.Documents.Application.Documents;
using Sepp.Documents.Application.Modeles;
using Sepp.Documents.Domain.Documents;
using Sepp.Documents.Domain.Fusion;
using Sepp.Documents.Domain.Langues;
using Sepp.Documents.Domain.Modeles;

namespace Sepp.Documents.Adapters.Api;

/// <summary>API REST du service Documents (ARC-30) : modèles (DOC-01), génération, archivage probant et intégrité (DOC-02, NF-21).</summary>
public static class DocumentsEndpoints
{
    /// <summary>En-tête du motif d'accès à un document sensible (jamais dans l'URL).</summary>
    public const string EnteteMotif = "X-Motif-Acces";

    public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");
        MapModeles(api.MapGroup("/modeles").WithTags("Modèles").RequirePermission(Permissions.DocumentsModeleLire));
        MapDocuments(api.MapGroup("/documents").WithTags("Documents").RequirePermission(Permissions.DocumentsLire));
        return app;
    }

    private static void MapModeles(RouteGroupBuilder modeles)
    {
        modeles.MapGet("/", async (string? code, IQueryHandler<ListerModeles, IReadOnlyList<ModeleResumeDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListerModeles(code), ct)).ToHttpResult())
            .WithName("ListerModeles");

        modeles.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirModele, ModeleDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ObtenirModele(id), ct)).ToHttpResult())
            .WithName("ObtenirModele");

        modeles.MapPost("/", async (CreerModele body, ICommandHandler<CreerModele, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(body, ct)).ToHttpResult(id => Results.Created($"/api/v1/modeles/{id}", new { id })))
            .RequirePermission(Permissions.DocumentsModeleGerer)
            .WithName("CreerModele")
            .WithSummary("Version 1 d'un modèle dans une langue, en brouillon (DOC-01).");

        modeles.MapPut("/{id:guid}", async (Guid id, ContenuModele body, ICommandHandler<ModifierModele, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ModifierModele(id, body.Libelle, body.Description, body.Contenu, body.Champs), ct)).ToHttpResult())
            .RequirePermission(Permissions.DocumentsModeleGerer)
            .WithName("ModifierModele")
            .WithSummary("Modification d'un brouillon.");

        modeles.MapPost("/{id:guid}/versions", async (Guid id, ICommandHandler<CreerNouvelleVersion, Guid> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new CreerNouvelleVersion(id), ct)).ToHttpResult(v => Results.Created($"/api/v1/modeles/{v}", new { id = v })))
            .RequirePermission(Permissions.DocumentsModeleGerer)
            .WithName("CreerVersionModele");

        modeles.MapPost("/{id:guid}/validation", async (Guid id, ICommandHandler<ValiderModele, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ValiderModele(id), ct)).ToHttpResult())
            .WithName("ValiderModele")
            .WithSummary("Validation avant publication : administrateur fonctionnel, CPMT dirigeant (zone médicale), CPAP dirigeant (zone psychosociale).");

        modeles.MapPost("/{id:guid}/renvoi-en-brouillon", async (Guid id, ICommandHandler<RenvoyerModeleEnBrouillon, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new RenvoyerModeleEnBrouillon(id), ct)).ToHttpResult())
            .WithName("RenvoyerModeleEnBrouillon");

        modeles.MapPost("/{id:guid}/publication", async (Guid id, ICommandHandler<PublierModele, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new PublierModele(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.DocumentsModeleGerer)
            .WithName("PublierModele");

        modeles.MapPost("/{id:guid}/apercu", async (Guid id, Dictionary<string, JsonElement> valeurs, IQueryHandler<ApercuModele, IReadOnlyList<BlocDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ApercuModele(id, Valeurs(valeurs)), ct)).ToHttpResult())
            .WithName("ApercuModele")
            .WithSummary("Fusion d'essai, sans archivage.");
    }

    private static void MapDocuments(RouteGroupBuilder documents)
    {
        documents.MapPost("/", async (DemandeDocument body, ICommandHandler<GenererDocument, DocumentDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(body.ToCommande(), ct)).ToHttpResult(d => Results.Created($"/api/v1/documents/{d.Id}", d)))
            .RequirePermission(Permissions.DocumentsGenerer)
            .WithName("GenererDocument")
            .WithSummary("Génération PDF/A, chiffrement par zone, archivage WORM, horodatage ; langue selon NF-41.");

        documents.MapGet("/", async (string? objetType, Guid? objetId, TypeDestinataire? typeDestinataire, Guid? destinataireId,
                IQueryHandler<ListerDocuments, IReadOnlyList<DocumentDto>> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ListerDocuments(objetType, objetId, typeDestinataire, destinataireId), ct)).ToHttpResult())
            .WithName("ListerDocuments");

        documents.MapGet("/{id:guid}", async (Guid id, IQueryHandler<ObtenirDocument, DocumentDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ObtenirDocument(id), ct)).ToHttpResult())
            .WithName("ObtenirDocument");

        documents.MapGet("/{id:guid}/contenu", async (Guid id, [FromHeader(Name = EnteteMotif)] string? motif,
                IQueryHandler<LireContenuDocument, ContenuDocumentDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new LireContenuDocument(id, motif), ct))
                .ToHttpResult(c => Results.File(c.Contenu, "application/pdf", c.NomFichier)))
            .WithName("LireContenuDocument")
            .WithSummary("PDF/A déchiffré ; accès journalisé (NF-04). Motif facultatif dans l'en-tête X-Motif-Acces.");

        documents.MapGet("/{id:guid}/integrite", async (Guid id, IQueryHandler<VerifierIntegriteDocument, IntegriteDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new VerifierIntegriteDocument(id), ct)).ToHttpResult())
            .WithName("VerifierIntegriteDocument")
            .WithSummary("NF-21 : empreintes SHA-256 de l'objet stocké et du PDF, déchiffrement authentifié.");

        documents.MapPost("/{id:guid}/publication", async (Guid id, ICommandHandler<PublierDocument, Unit> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new PublierDocument(id), ct)).ToHttpResult())
            .RequirePermission(Permissions.DocumentsGenerer)
            .WithName("PublierDocument")
            .WithSummary("Mise à disposition du destinataire : publie documents.document-publie.v1 (envoi par Communications).");

        documents.MapPost("/{id:guid}/signatures", async (Guid id, ICommandHandler<SignerDocument, SignatureDto> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new SignerDocument(id), ct)).ToHttpResult(s => Results.Created($"/api/v1/documents/{id}", s)))
            .RequirePermission(Permissions.DocumentsGenerer)
            .WithName("SignerDocument")
            .WithSummary("Signature électronique qualifiée de l'utilisateur courant (preuve détachée).");
    }

    /// <summary>Valeurs JSON des champs : texte, nombre, booléen ou tableau de textes.</summary>
    public static IReadOnlyDictionary<string, ValeurChamp> Valeurs(IReadOnlyDictionary<string, JsonElement>? valeurs) =>
        (valeurs ?? new Dictionary<string, JsonElement>())
            .Where(v => v.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            .ToDictionary(v => v.Key, v => v.Value.ValueKind switch
            {
                JsonValueKind.Array => ValeurChamp.Liste(v.Value.EnumerateArray().Select(Texte)),
                _ => ValeurChamp.Simple(Texte(v.Value)),
            }, StringComparer.Ordinal);

    private static string Texte(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.True => bool.TrueString.ToLower(CultureInfo.InvariantCulture),
        JsonValueKind.False => bool.FalseString.ToLower(CultureInfo.InvariantCulture),
        _ => element.GetRawText(),
    };

    public sealed record ContenuModele(string Libelle, string? Description, string Contenu, IReadOnlyList<ChampDto> Champs);

    /// <summary>Demande de génération (NF-41 : <c>langue</c> imposée, sinon <c>regime</c> de l'affilié et <c>choixTravailleur</c>, lus au besoin).</summary>
    public sealed record DemandeDocument(
        string CodeModele,
        TypeDestinataire TypeDestinataire,
        Guid DestinataireId,
        string? ServiceProprietaire,
        string ObjetType,
        Guid ObjetId,
        Dictionary<string, JsonElement>? Valeurs,
        Language? Langue,
        RegimeLinguistique? Regime,
        Language? ChoixTravailleur,
        Guid? AffilieId,
        Guid? PersonneId,
        bool Publier)
    {
        public GenererDocument ToCommande() => new(
            CodeModele, TypeDestinataire, DestinataireId, string.IsNullOrWhiteSpace(ServiceProprietaire) ? "documents" : ServiceProprietaire, ObjetType, ObjetId,
            DocumentsEndpoints.Valeurs(Valeurs), Langue, Regime, ChoixTravailleur, AffilieId, PersonneId, Publier);
    }

    internal static TypeModele TypeParDefaut => TypeModele.Courrier;
}
