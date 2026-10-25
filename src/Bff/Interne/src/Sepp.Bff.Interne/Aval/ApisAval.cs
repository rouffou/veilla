namespace Sepp.Bff.Interne.Aval;

/// <summary>Service Personnes et occupations : fiche d'une personne (AFF-20 à AFF-23).</summary>
public interface IPersonnesApi
{
    Task<PersonneAval> ObtenirPersonneAsync(Guid personneId, CancellationToken ct);
}

/// <summary>Service Affiliés : fiche de l'affilié (AFF-01 à AFF-03).</summary>
public interface IAffiliesApi
{
    Task<AffilieAval> ObtenirAffilieAsync(Guid affilieId, CancellationToken ct);
}

internal sealed class PersonnesApi(HttpClient http) : ClientAval(http, "personnes"), IPersonnesApi
{
    public Task<PersonneAval> ObtenirPersonneAsync(Guid personneId, CancellationToken ct) =>
        GetAsync<PersonneAval>($"api/v1/personnes/{personneId}", ct);
}

internal sealed class AffiliesApi(HttpClient http) : ClientAval(http, "affilies"), IAffiliesApi
{
    public Task<AffilieAval> ObtenirAffilieAsync(Guid affilieId, CancellationToken ct) =>
        GetAsync<AffilieAval>($"api/v1/affilies/{affilieId}", ct);
}
