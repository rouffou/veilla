using Microsoft.EntityFrameworkCore;

using Sepp.Affilies.Adapters.Persistence;
using Sepp.BuildingBlocks.Application.Republication;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.Contracts.Affilies;

namespace Sepp.Affilies.Adapters.Rattrapage;

/// <summary>
/// ADR 0011, NF-41 : état courant de chaque affilié (<c>affilies.affilie-instantane.v1</c>) : numéro BCE, catégorie tarifaire,
/// statut, régime linguistique et langue, comme <c>affilies.affilie-modifie.v1</c> (ARC-06). Projection SQL de ces seules
/// colonnes et de l'horodatage de l'état (<c>updated_at</c>) ; les affiliés supprimés logiquement sont exclus.
/// </summary>
public sealed class RepublicationAffilies(AffiliesDbContext db) : ISourceRepublication
{
    public const string Nom = "affilies";

    public string Type => Nom;

    public async Task<LotRepublication> LireAsync(string? curseur, int taille, CancellationToken cancellationToken)
    {
        var apres = CurseurRepublication.Lire(curseur);
        var affilies = db.Affilies.AsNoTracking();
        if (apres is { } id)
        {
            affilies = affilies.Where(a => a.Id.CompareTo(id) > 0);
        }

        var lignes = await affilies
            .OrderBy(a => a.Id)
            .Take(taille)
            .Select(a => new
            {
                a.Id,
                a.NumeroBce,
                a.CategorieTarifaire,
                a.Statut,
                a.RegimeLinguistique,
                a.Langue,
                EtatDu = EF.Property<DateTimeOffset>(a, SeppDbContext.UpdatedAt),
            })
            .ToListAsync(cancellationToken);

        return new LotRepublication(
            [
                .. lignes.Select(l => new AffilieInstantane(
                    l.Id, l.NumeroBce.Value, l.CategorieTarifaire.ToString(), l.Statut.ToString(), l.RegimeLinguistique.ToString(), l.Langue.ToString(), l.EtatDu)),
            ],
            CurseurRepublication.Suivant(lignes, taille, l => l.Id));
    }
}
