using System.Text.Json.Nodes;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Documents.Application.Documents;
using Sepp.Documents.Domain.Documents;

namespace Sepp.Documents.Adapters.Api;

/// <summary>
/// Publie le type de destinataire <b>renvoyé</b> par le service (#436, #476) : <c>affilie</c>, <c>personne</c>, <c>dossier</c>
/// (<see cref="TypesDestinataire.Code"/>), en minuscules. <see cref="DocumentDto.TypeDestinataire"/> est un texte, non une
/// énumération : son schéma n'annonçait aucune valeur, et le schéma de l'énumération <c>TypeDestinataire</c> (PascalCase)
/// ne décrit que ce que le service <i>accepte</i> en entrée (paramètre de requête, qui est sensible à la casse : <c>Personne</c>).
/// Les deux casses ne sont pas interchangeables, ce qui est la cause du bug #436 : un double de test pouvait renvoyer « Personne ».
/// Les valeurs viennent de <see cref="TypesDestinataire.Code"/> : elles ne peuvent pas diverger de ce que renvoie le service.
/// Métadonnées OpenAPI seules.
/// </summary>
internal static class TypeDestinataireOpenApi
{
    internal static IServiceCollection AddTypeDestinataireOpenApi(this IServiceCollection services) =>
        services.ConfigureAll<OpenApiOptions>(options => options.AddSchemaTransformer((schema, contexte, _) =>
        {
            if (contexte.JsonPropertyInfo is { DeclaringType: { } declarant, Name: { } nom }
                && declarant == typeof(DocumentDto)
                && string.Equals(nom, nameof(DocumentDto.TypeDestinataire), StringComparison.OrdinalIgnoreCase))
            {
                schema.Enum = [.. Enum.GetValues<TypeDestinataire>().Select(t => (JsonNode)JsonValue.Create(t.Code())!)];
            }

            return Task.CompletedTask;
        }));
}
