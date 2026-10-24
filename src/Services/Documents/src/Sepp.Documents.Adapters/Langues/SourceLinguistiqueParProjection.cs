using Microsoft.EntityFrameworkCore;

using Sepp.Documents.Adapters.Persistence;
using Sepp.Documents.Application;
using Sepp.Documents.Domain.Langues;

namespace Sepp.Documents.Adapters.Langues;

/// <summary>
/// NF-41, ADR 0009 (<c>Documents:SourceLinguistique=Projection</c>) : régime linguistique et langue de l'affilié, langue du
/// travailleur lus dans la projection locale <c>contexte_linguistique</c>, alimentée par les événements d'Affiliés et de
/// Personnes. Aucun appel réseau : c'est la source de l'instance médicale, que le NSG empêche d'appeler Affiliés et
/// Personnes. Une information absente (affilié ou personne inconnu de la projection, événement antérieur à sa mise en
/// service : rattrapage suivi par #369) reste indéterminée, et la règle linguistique se rabat sur les autres éléments,
/// à défaut le français (<see cref="RegleLinguistique"/>).
/// </summary>
internal sealed class SourceLinguistiqueParProjection(DocumentsDbContext db) : ISourceLinguistique
{
    public async Task<InformationsLinguistiques> ObtenirAsync(Guid? affilieId, Guid? personneId, CancellationToken cancellationToken)
    {
        var affilie = affilieId is { } a ? await LireAsync(SujetLinguistique.Affilie, a, cancellationToken) : null;
        var personne = personneId is { } p ? await LireAsync(SujetLinguistique.Personne, p, cancellationToken) : null;
        return new InformationsLinguistiques(affilie?.Regime, affilie?.Langue, personne?.Langue);
    }

    private Task<ContexteLinguistiqueProjete?> LireAsync(SujetLinguistique sujet, Guid id, CancellationToken cancellationToken) =>
        db.ContextesLinguistiques.AsNoTracking().SingleOrDefaultAsync(c => c.Sujet == sujet && c.SujetId == id, cancellationToken);
}
