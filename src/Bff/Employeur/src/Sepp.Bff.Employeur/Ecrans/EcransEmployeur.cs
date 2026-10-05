using System.Globalization;
using System.Net;

using Sepp.Bff.Employeur.Aval;
using Sepp.BuildingBlocks.Web;

namespace Sepp.Bff.Employeur.Ecrans;

/// <summary>
/// Composition des écrans du portail employeur (ARC-35, ARC-43) : appels parallèles aux services aval puis
/// projection vers les modèles d'écran. Aucune règle métier : filtrage, tri, pagination et comptages d'affichage
/// uniquement ; les décisions (périmètre, validité d'une proposition) restent aux services propriétaires.
/// </summary>
public sealed class EcransEmployeur(IAffiliesApi affilies, IPersonnesApi personnes, IPostesRisquesApi postesRisques, TimeProvider clock)
{
    public const int TailleMaximale = 100;

    private static readonly string[] IndicateursAVenir =
        ["examensDus", "examensEnRetard", "examensPlanifies", "missionsEnCours", "mesuresPlanAction", "soldeUnites"];

    /// <summary>Affiliés du jeton, résolus auprès du service Affiliés (un appel par affilié, en parallèle).</summary>
    public async Task<MesAffilies> MesAffiliesAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var resultats = await Task.WhenAll(ids.Select(async id =>
        {
            try
            {
                return (Id: id, Affilie: await affilies.ObtenirAffilieAsync(id, ct));
            }
            catch (ErreurAvalException e) when (e.Status is HttpStatusCode.NotFound)
            {
                return (Id: id, Affilie: (AffilieAval?)null);
            }
        }));

        return new MesAffilies(
            resultats.Where(r => r.Affilie is not null)
                .Select(r => new AffilieResume(r.Id, r.Affilie!.Fiche.NumeroBce, r.Affilie.Fiche.Denomination, r.Affilie.Fiche.Statut))
                .OrderBy(a => a.Denomination, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            resultats.Where(r => r.Affilie is null).Select(r => r.Id).ToList());
    }

    public async Task<FicheAffilie> FicheAsync(Guid affilieId, CancellationToken ct) =>
        Fiche(await affilies.ObtenirAffilieAsync(affilieId, ct), Aujourdhui);

    /// <summary>Fiche d'écran : sites et contacts en vigueur à la date du jour (les lignes historisées restent dans le service).</summary>
    public static FicheAffilie Fiche(AffilieAval a, DateOnly aujourdhui)
    {
        bool EnVigueur(DateOnly du, DateOnly? au) => du <= aujourdhui && (au is null || au > aujourdhui);

        var sites = a.UnitesEtablissement
            .Where(u => EnVigueur(u.ValideDu, u.ValideJusquAu))
            .SelectMany(u => u.Sites.Where(s => EnVigueur(s.ValideDu, s.ValideJusquAu)).Select(s => new SiteEcran(
                s.Id, s.Nom, u.Nom, u.Numero,
                new AdresseEcran(s.Adresse.Rue, s.Adresse.Numero, s.Adresse.Boite, s.Adresse.CodePostal, s.Adresse.Localite, s.Adresse.CodePays ?? "BE"))))
            .OrderBy(s => s.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var contacts = a.Contacts
            .Where(c => EnVigueur(c.ValideDu, c.ValideJusquAu))
            .Select(c => new ContactEcran(c.Id, c.Nom, c.Fonction, c.Role, c.Email, c.Telephone, c.ValideDu))
            .OrderBy(c => c.Role, StringComparer.Ordinal).ThenBy(c => c.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var f = a.Fiche;
        return new FicheAffilie(a.Id, f.NumeroBce, f.Denomination, f.FormeJuridique, f.CodeNace, f.CommissionParitaire, f.CategorieTarifaire,
            f.DateAffiliation, f.DateFin, f.Langue, f.Statut, sites, contacts);
    }

    public async Task<PageEcran<TravailleurEcran>> TravailleursAsync(Guid affilieId, string? recherche, int page, int taille, CancellationToken ct) =>
        Paginer(await personnes.ListerTravailleursAsync(affilieId, null, ct), recherche, page, taille);

    /// <summary>Recherche (nom ou prénom, sans tenir compte de la casse ni des accents), tri alphabétique et pagination.</summary>
    public static PageEcran<TravailleurEcran> Paginer(IEnumerable<TravailleurAval> travailleurs, string? recherche, int page, int taille)
    {
        page = Math.Max(1, page);
        taille = Math.Clamp(taille, 1, TailleMaximale);
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        const CompareOptions Options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        var terme = recherche?.Trim();

        var filtres = travailleurs
            .Where(t => string.IsNullOrEmpty(terme)
                        || compare.IndexOf(t.Nom, terme, Options) >= 0
                        || compare.IndexOf(t.Prenom, terme, Options) >= 0
                        || compare.IndexOf($"{t.Prenom} {t.Nom}", terme, Options) >= 0
                        || compare.IndexOf($"{t.Nom} {t.Prenom}", terme, Options) >= 0)
            .OrderBy(t => t.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.Prenom, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.Id)
            .ToList();

        return new PageEcran<TravailleurEcran>(
            filtres.Skip((page - 1) * taille).Take(taille).Select(t => new TravailleurEcran(t.Id, t.Nom, t.Prenom, t.DateNaissance)).ToList(),
            filtres.Count, page, taille);
    }

    public async Task<IReadOnlyList<PosteEcran>> PostesAsync(Guid affilieId, string langue, CancellationToken ct)
    {
        var postesTache = postesRisques.ListerPostesAsync(affilieId, ct);
        var risquesTache = postesRisques.ListerRisquesAsync(langue, ct);
        await Task.WhenAll(postesTache, risquesTache);
        return Postes(postesTache.Result, risquesTache.Result);
    }

    /// <summary>Postes enrichis du libellé de leurs risques en vigueur ; un poste est « exposé » s'il est actif et porte au moins un risque.</summary>
    public static IReadOnlyList<PosteEcran> Postes(IEnumerable<PosteAval> postes, IEnumerable<RisqueAval> risques)
    {
        var parCode = risques.GroupBy(r => r.Code, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return postes
            .Select(p => new PosteEcran(
                p.Id, p.Intitule, p.Description, p.Statut, EstExpose(p),
                p.Risques.Select(r => new RisquePosteEcran(
                        r.RisqueCode,
                        parCode.TryGetValue(r.RisqueCode, out var risque) ? risque.Libelle : r.RisqueCode,
                        risque?.Categorie,
                        r.NiveauExposition,
                        r.ValideDu))
                    .OrderBy(r => r.Code, StringComparer.Ordinal)
                    .ToList()))
            .OrderBy(p => p.Statut == "Actif" ? 0 : 1)
            .ThenBy(p => p.Intitule, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<RisqueEcran>> RisquesAsync(string langue, CancellationToken ct) =>
        (await postesRisques.ListerRisquesAsync(langue, ct))
        .Select(r => new RisqueEcran(r.Code, r.Libelle, r.Categorie))
        .OrderBy(r => r.Categorie, StringComparer.Ordinal).ThenBy(r => r.Code, StringComparer.Ordinal)
        .ToList();

    public async Task<IReadOnlyList<ListeNominativeEcran>> ListesNominativesAsync(Guid affilieId, CancellationToken ct) =>
        Listes(await postesRisques.ListerListesNominativesAsync(affilieId, ct));

    /// <summary>Versions des listes nominatives (AFF-31, POR-06), la plus récente de chaque type en premier.</summary>
    public static IReadOnlyList<ListeNominativeEcran> Listes(IEnumerable<ListeNominativeAval> listes)
    {
        var toutes = listes.ToList();
        var dernieres = toutes.GroupBy(l => l.Type, StringComparer.Ordinal).Select(g => g.MaxBy(l => l.Version)!.Id).ToHashSet();
        return toutes
            .OrderBy(l => l.Type, StringComparer.Ordinal).ThenByDescending(l => l.Version)
            .Select(l => ToEcran(l, dernieres.Contains(l.Id)))
            .ToList();
    }

    /// <summary>
    /// Une version de liste et ses lignes, avec les noms des travailleurs et les intitulés des postes.
    /// Une liste d'un autre affilié est traitée comme inconnue (404), même si le jeton y donne accès.
    /// </summary>
    public async Task<ListeNominativeDetail> ListeNominativeAsync(Guid affilieId, Guid listeId, CancellationToken ct)
    {
        var liste = await postesRisques.ObtenirListeNominativeAsync(listeId, ct);
        if (liste.AffilieId != affilieId)
        {
            throw ListeInconnue(listeId);
        }

        var travailleursTache = personnes.ListerTravailleursAsync(affilieId, liste.DateReference, ct);
        var postesTache = postesRisques.ListerPostesAsync(affilieId, ct);
        await Task.WhenAll(travailleursTache, postesTache);

        var noms = travailleursTache.Result.ToDictionary(t => t.Id);
        var lignes = liste.Lignes ?? [];
        // Travailleurs sortis depuis la date de référence : lus individuellement (rares) ; inconnus s'ils ne sont plus visibles.
        foreach (var personne in await Task.WhenAll(lignes.Select(l => l.PersonneId).Distinct().Where(id => !noms.ContainsKey(id))
                     .Select(id => PersonneOuNullAsync(id, ct))))
        {
            if (personne is not null)
            {
                noms[personne.Id] = personne;
            }
        }

        var postes = postesTache.Result.ToDictionary(p => p.Id, p => p.Intitule);
        return new ListeNominativeDetail(
            ToEcran(liste, derniere: false),
            lignes.Select(l => new LigneListeEcran(
                    l.PersonneId,
                    noms.GetValueOrDefault(l.PersonneId)?.Nom,
                    noms.GetValueOrDefault(l.PersonneId)?.Prenom,
                    l.PosteId,
                    postes.GetValueOrDefault(l.PosteId),
                    l.CodesRisques,
                    l.DateDerniereEvaluation,
                    l.Origine))
                .OrderBy(l => l.Nom ?? "￿", StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(l => l.Prenom, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(l => l.Poste, StringComparer.CurrentCultureIgnoreCase)
                .ToList());
    }

    /// <summary>
    /// POR-02 : seuls les compteurs calculables aujourd'hui sont renvoyés avec une valeur. Les autres (examens,
    /// missions, plan d'action, unités) sont explicitement « à venir » : aucun chiffre fictif. Une source en panne
    /// rend ses compteurs « service-indisponible » sans faire échouer tout le tableau de bord.
    /// </summary>
    public async Task<TableauDeBord> TableauDeBordAsync(Guid affilieId, CancellationToken ct)
    {
        var travailleursTache = Tolerer(() => personnes.ListerTravailleursAsync(affilieId, null, ct));
        var postesTache = Tolerer(() => postesRisques.ListerPostesAsync(affilieId, ct));
        var propositionsTache = Tolerer(async () =>
        {
            var parPoste = postesRisques.ListerPropositionsPosteRisqueAsync(affilieId, ct);
            var parListe = postesRisques.ListerPropositionsListeAsync(affilieId, ct);
            await Task.WhenAll(parPoste, parListe);
            return parPoste.Result.Count(p => p.Statut == "Soumise") + parListe.Result.Count(p => p.Statut == "Soumise");
        });
        await Task.WhenAll(travailleursTache, postesTache, propositionsTache);

        return ComposerTableauDeBord(affilieId, Aujourdhui, travailleursTache.Result, postesTache.Result, propositionsTache.Result);
    }

    public static TableauDeBord ComposerTableauDeBord(
        Guid affilieId, DateOnly date, IReadOnlyList<TravailleurAval>? travailleurs, IReadOnlyList<PosteAval>? postes, int? propositionsEnAttente)
    {
        var indicateurs = new List<Indicateur>
        {
            travailleurs is null ? Indicateur.NonDisponible("travailleurs", Indicateur.Indisponible) : Indicateur.Avec("travailleurs", travailleurs.Count),
            postes is null
                ? Indicateur.NonDisponible("postesActifs", Indicateur.Indisponible)
                : Indicateur.Avec("postesActifs", postes.Count(p => p.Statut == "Actif")),
            postes is null
                ? Indicateur.NonDisponible("postesExposes", Indicateur.Indisponible)
                : Indicateur.Avec("postesExposes", postes.Count(EstExpose)),
            propositionsEnAttente is null
                ? Indicateur.NonDisponible("propositionsEnAttente", Indicateur.Indisponible)
                : Indicateur.Avec("propositionsEnAttente", propositionsEnAttente.Value),
        };
        indicateurs.AddRange(IndicateursAVenir.Select(code => Indicateur.NonDisponible(code, Indicateur.AVenir)));
        return new TableauDeBord(affilieId, date, indicateurs);
    }

    /// <summary>Suivi des propositions de l'affilié (POR-03) : profils de risques et listes nominatives, les plus récentes d'abord.</summary>
    public async Task<IReadOnlyList<PropositionEcran>> PropositionsAsync(Guid affilieId, CancellationToken ct)
    {
        var parPoste = postesRisques.ListerPropositionsPosteRisqueAsync(affilieId, ct);
        var parListe = postesRisques.ListerPropositionsListeAsync(affilieId, ct);
        var postes = postesRisques.ListerPostesAsync(affilieId, ct);
        await Task.WhenAll(parPoste, parListe, postes);

        var intitules = postes.Result.ToDictionary(p => p.Id, p => p.Intitule);
        return parPoste.Result
            .Select(p => new PropositionEcran(p.Id, "poste-risque", p.PosteId, intitules.GetValueOrDefault(p.PosteId) ?? string.Empty, p.Motif,
                p.Statut, p.DateProposition, p.DateDecision, p.MotifRefus))
            .Concat(parListe.Result.Select(p => new PropositionEcran(p.Id, "liste-nominative", p.ListeNominativeId,
                $"{p.TypeListe} v{p.VersionListe.ToString(CultureInfo.InvariantCulture)}", p.Motif, p.Statut, p.DateProposition, p.DateDecision, p.MotifRefus)))
            .OrderByDescending(p => p.DateProposition)
            .ToList();
    }

    /// <summary>POR-03 → AFF-14 : la proposition est déléguée au service Postes et risques, qui la soumet au CPMT.</summary>
    public async Task<PropositionSoumise> ProposerPosteAsync(Guid affilieId, Guid posteId, PropositionPosteCorps corps, CancellationToken ct)
    {
        var poste = await postesRisques.ObtenirPosteAsync(posteId, ct);
        if (poste.AffilieId != affilieId)
        {
            throw new ErreurAvalException(NomsServices.PostesRisques, HttpStatusCode.NotFound, "poste.inconnu", "Ce poste n'existe pas pour cet affilié.");
        }

        var id = await postesRisques.ProposerModificationPosteAsync(posteId,
            new PropositionPosteRisqueCorpsAval(corps.Motif, corps.ValideDu, corps.Lignes ?? [], AvisCppt: null), ct);
        return new PropositionSoumise(id, "Soumise");
    }

    /// <summary>POR-03 → AFF-31 : proposition d'ajustement d'une liste nominative, soumise au CPMT par le service Postes et risques.</summary>
    public async Task<PropositionSoumise> ProposerListeAsync(Guid affilieId, Guid listeId, PropositionListeCorps corps, CancellationToken ct)
    {
        var liste = await postesRisques.ObtenirListeNominativeAsync(listeId, ct);
        if (liste.AffilieId != affilieId)
        {
            throw ListeInconnue(listeId);
        }

        var id = await postesRisques.ProposerModificationListeAsync(listeId, new PropositionListeCorpsAval(corps.Motif, corps.Lignes ?? []), ct);
        return new PropositionSoumise(id, "Soumise");
    }

    private DateOnly Aujourdhui => clock.TodayInBelgium();

    private static bool EstExpose(PosteAval p) => p.Statut == "Actif" && p.Risques.Count > 0;

    private static ListeNominativeEcran ToEcran(ListeNominativeAval l, bool derniere) =>
        new(l.Id, l.Type, l.Version, l.DateReference, l.DateGeneration, l.NombreLignes, derniere);

    private static ErreurAvalException ListeInconnue(Guid listeId) =>
        new(NomsServices.PostesRisques, HttpStatusCode.NotFound, "liste-nominative.inconnue", $"La liste nominative {listeId} n'existe pas pour cet affilié.");

    private async Task<TravailleurAval?> PersonneOuNullAsync(Guid id, CancellationToken ct)
    {
        try
        {
            return await personnes.ObtenirPersonneAsync(id, ct);
        }
        catch (ErreurAvalException e) when (e.Status is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return null;
        }
    }

    private static async Task<T?> Tolerer<T>(Func<Task<T>> appel)
        where T : class
    {
        try
        {
            return await appel();
        }
        catch (ServiceIndisponibleException)
        {
            return null;
        }
    }

    private static async Task<int?> Tolerer(Func<Task<int>> appel)
    {
        try
        {
            return await appel();
        }
        catch (ServiceIndisponibleException)
        {
            return null;
        }
    }
}
