using Microsoft.EntityFrameworkCore;

using Sepp.BuildingBlocks.Application.Republication;
using Sepp.BuildingBlocks.Infrastructure.Messaging;
using Sepp.BuildingBlocks.Infrastructure.Persistence;
using Sepp.Contracts.Personnes;
using Sepp.Personnes.Adapters.Persistence;

namespace Sepp.Personnes.Adapters.Rattrapage;

/// <summary>
/// ADR 0011, NF-41 : langue courante de chaque personne (<c>personnes.personne-instantanee.v1</c>), pour la projection
/// linguistique de Documents. Projection SQL de la seule langue et de l'horodatage de l'état (<c>updated_at</c>) : le NISS,
/// l'identité et les états particuliers ne sont ni lus ni déchiffrés (ARC-06). Les personnes supprimées logiquement sont exclues.
/// </summary>
public sealed class RepublicationPersonnes(PersonnesDbContext db) : ISourceRepublication
{
    public const string Nom = "personnes";

    public string Type => Nom;

    public async Task<LotRepublication> LireAsync(string? curseur, int taille, CancellationToken cancellationToken)
    {
        var apres = CurseurRepublication.Lire(curseur);
        var personnes = db.Personnes.AsNoTracking();
        if (apres is { } id)
        {
            personnes = personnes.Where(p => p.Id.CompareTo(id) > 0);
        }

        var lignes = await personnes
            .OrderBy(p => p.Id)
            .Take(taille)
            .Select(p => new { p.Id, p.Langue, EtatDu = EF.Property<DateTimeOffset>(p, SeppDbContext.UpdatedAt) })
            .ToListAsync(cancellationToken);

        return new LotRepublication(
            [.. lignes.Select(l => new PersonneInstantanee(l.Id, l.Langue.ToString(), l.EtatDu))],
            CurseurRepublication.Suivant(lignes, taille, l => l.Id));
    }
}

/// <summary>
/// ADR 0011, POR-11 : état courant de chaque occupation (<c>personnes.occupation-instantanee.v1</c>), pour les projections
/// d'occupations (Affiliés ; Obligations et Planification quand elles s'y abonneront). Identifiants et dates seulement
/// (ARC-06). Les occupations d'une personne supprimée logiquement sont exclues.
/// </summary>
public sealed class RepublicationOccupations(PersonnesDbContext db) : ISourceRepublication
{
    public const string Nom = "occupations";

    public string Type => Nom;

    public async Task<LotRepublication> LireAsync(string? curseur, int taille, CancellationToken cancellationToken)
    {
        var apres = CurseurRepublication.Lire(curseur);
        var occupations = db.Personnes.AsNoTracking()
            .SelectMany(p => p.Occupations, (p, o) => new
            {
                o.Id,
                PersonneId = p.Id,
                o.AffilieId,
                o.DateDebut,
                o.DateFin,
                EtatDu = EF.Property<DateTimeOffset>(o, SeppDbContext.UpdatedAt),
            });
        if (apres is { } id)
        {
            occupations = occupations.Where(o => o.Id.CompareTo(id) > 0);
        }

        var lignes = await occupations.OrderBy(o => o.Id).Take(taille).ToListAsync(cancellationToken);

        return new LotRepublication(
            [.. lignes.Select(l => new OccupationInstantanee(l.Id, l.PersonneId, l.AffilieId, l.DateDebut, l.DateFin, l.EtatDu))],
            CurseurRepublication.Suivant(lignes, taille, l => l.Id));
    }
}
