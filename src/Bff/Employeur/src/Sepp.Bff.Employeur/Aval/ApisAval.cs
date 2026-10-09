using System.Globalization;

namespace Sepp.Bff.Employeur.Aval;

/// <summary>Service Affiliés : fiche de l'affilié (AFF-01 à AFF-03).</summary>
public interface IAffiliesApi
{
    Task<AffilieAval> ObtenirAffilieAsync(Guid affilieId, CancellationToken ct);
}

/// <summary>Service Personnes et occupations : travailleurs d'un affilié (AFF-20 à AFF-23).</summary>
public interface IPersonnesApi
{
    Task<IReadOnlyList<TravailleurAval>> ListerTravailleursAsync(Guid affilieId, DateOnly? date, CancellationToken ct);

    Task<TravailleurAval> ObtenirPersonneAsync(Guid personneId, CancellationToken ct);
}

/// <summary>Service Postes et risques : catalogue de postes, risques, listes nominatives et propositions (AFF-10 à AFF-14, AFF-30, AFF-31).</summary>
public interface IPostesRisquesApi
{
    Task<IReadOnlyList<PosteAval>> ListerPostesAsync(Guid affilieId, CancellationToken ct);

    Task<PosteAval> ObtenirPosteAsync(Guid posteId, CancellationToken ct);

    Task<IReadOnlyList<RisqueAval>> ListerRisquesAsync(string langue, CancellationToken ct);

    Task<IReadOnlyList<ListeNominativeAval>> ListerListesNominativesAsync(Guid affilieId, CancellationToken ct);

    Task<ListeNominativeAval> ObtenirListeNominativeAsync(Guid listeId, CancellationToken ct);

    Task<IReadOnlyList<PropositionPosteRisqueAval>> ListerPropositionsPosteRisqueAsync(Guid affilieId, CancellationToken ct);

    Task<IReadOnlyList<PropositionListeAval>> ListerPropositionsListeAsync(Guid affilieId, CancellationToken ct);

    Task<Guid> ProposerModificationPosteAsync(Guid posteId, PropositionPosteRisqueCorpsAval corps, CancellationToken ct);

    Task<Guid> ProposerModificationListeAsync(Guid listeId, PropositionListeCorpsAval corps, CancellationToken ct);
}

/// <summary>Service Obligations : processus de reprise du travail (ARC-33, POR-04).</summary>
public interface IObligationsApi
{
    Task<ResultatEnregistrementRepriseAval> AnnoncerRepriseAsync(RepriseCorpsAval corps, CancellationToken ct);

    Task<IReadOnlyList<RepriseAval>> ListerReprisesAsync(Guid affilieId, CancellationToken ct);

    Task<RepriseAval> ObtenirRepriseAsync(Guid repriseId, CancellationToken ct);
}

public static class NomsServices
{
    public const string Obligations = "obligations";
    public const string Affilies = "affilies";
    public const string Personnes = "personnes";
    public const string PostesRisques = "postes-risques";
}

internal sealed class ObligationsApi(HttpClient http) : ClientAval(http, NomsServices.Obligations), IObligationsApi
{
    public Task<ResultatEnregistrementRepriseAval> AnnoncerRepriseAsync(RepriseCorpsAval corps, CancellationToken ct) =>
        PostAsync<ResultatEnregistrementRepriseAval>("api/v1/reprises", corps, ct);

    public Task<IReadOnlyList<RepriseAval>> ListerReprisesAsync(Guid affilieId, CancellationToken ct) =>
        GetAsync<IReadOnlyList<RepriseAval>>($"api/v1/reprises?affilieId={affilieId}", ct);

    public Task<RepriseAval> ObtenirRepriseAsync(Guid repriseId, CancellationToken ct) =>
        GetAsync<RepriseAval>($"api/v1/reprises/{repriseId}", ct);
}

internal sealed class AffiliesApi(HttpClient http) : ClientAval(http, NomsServices.Affilies), IAffiliesApi
{
    public Task<AffilieAval> ObtenirAffilieAsync(Guid affilieId, CancellationToken ct) =>
        GetAsync<AffilieAval>($"api/v1/affilies/{affilieId}", ct);
}

internal sealed class PersonnesApi(HttpClient http) : ClientAval(http, NomsServices.Personnes), IPersonnesApi
{
    public Task<IReadOnlyList<TravailleurAval>> ListerTravailleursAsync(Guid affilieId, DateOnly? date, CancellationToken ct) =>
        GetAsync<IReadOnlyList<TravailleurAval>>(
            date is { } d
                ? $"api/v1/affilies/{affilieId}/travailleurs?date={d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
                : $"api/v1/affilies/{affilieId}/travailleurs",
            ct);

    public Task<TravailleurAval> ObtenirPersonneAsync(Guid personneId, CancellationToken ct) =>
        GetAsync<TravailleurAval>($"api/v1/personnes/{personneId}", ct);
}

internal sealed class PostesRisquesApi(HttpClient http) : ClientAval(http, NomsServices.PostesRisques), IPostesRisquesApi
{
    public Task<IReadOnlyList<PosteAval>> ListerPostesAsync(Guid affilieId, CancellationToken ct) =>
        GetAsync<IReadOnlyList<PosteAval>>($"api/v1/postes?affilieId={affilieId}", ct);

    public Task<PosteAval> ObtenirPosteAsync(Guid posteId, CancellationToken ct) =>
        GetAsync<PosteAval>($"api/v1/postes/{posteId}", ct);

    public Task<IReadOnlyList<RisqueAval>> ListerRisquesAsync(string langue, CancellationToken ct) =>
        GetAsync<IReadOnlyList<RisqueAval>>($"api/v1/risques?langue={Uri.EscapeDataString(langue)}", ct);

    public Task<IReadOnlyList<ListeNominativeAval>> ListerListesNominativesAsync(Guid affilieId, CancellationToken ct) =>
        GetAsync<IReadOnlyList<ListeNominativeAval>>($"api/v1/listes-nominatives?affilieId={affilieId}", ct);

    public Task<ListeNominativeAval> ObtenirListeNominativeAsync(Guid listeId, CancellationToken ct) =>
        GetAsync<ListeNominativeAval>($"api/v1/listes-nominatives/{listeId}", ct);

    public Task<IReadOnlyList<PropositionPosteRisqueAval>> ListerPropositionsPosteRisqueAsync(Guid affilieId, CancellationToken ct) =>
        GetAsync<IReadOnlyList<PropositionPosteRisqueAval>>($"api/v1/propositions-poste-risque?affilieId={affilieId}", ct);

    public Task<IReadOnlyList<PropositionListeAval>> ListerPropositionsListeAsync(Guid affilieId, CancellationToken ct) =>
        GetAsync<IReadOnlyList<PropositionListeAval>>($"api/v1/propositions-liste-nominative?affilieId={affilieId}", ct);

    public async Task<Guid> ProposerModificationPosteAsync(Guid posteId, PropositionPosteRisqueCorpsAval corps, CancellationToken ct) =>
        (await PostAsync<IdentifiantAval>($"api/v1/postes/{posteId}/propositions", corps, ct)).Id;

    public async Task<Guid> ProposerModificationListeAsync(Guid listeId, PropositionListeCorpsAval corps, CancellationToken ct) =>
        (await PostAsync<IdentifiantAval>($"api/v1/listes-nominatives/{listeId}/propositions", corps, ct)).Id;
}
