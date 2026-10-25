using System.Net;

using Sepp.Bff.Interne.Aval;

namespace Sepp.Bff.Interne.Ecrans;

/// <summary>
/// Section d'un écran composite qui tolère l'échec de sa source (ADR 0013) : l'écran s'affiche avec les autres sections et
/// celle-ci indique pourquoi elle manque. <c>raison</c> vaut <see cref="AccesRefuse"/> quand le service refuse l'accès à
/// l'utilisateur (sa matrice de permissions, par exemple le dossier de santé pour un gestionnaire) et
/// <see cref="ServiceIndisponible"/> quand le service est injoignable, trop lent ou répond de façon inattendue. Aucun détail
/// technique ni contenu de l'erreur n'est relayé (ARC-06).
/// </summary>
/// <typeparam name="T">Modèle de la section.</typeparam>
public sealed record SectionTolerante<T>(bool Disponible, string? Raison, T? Valeur)
{
    public const string AccesRefuse = "acces-refuse";
    public const string ServiceIndisponible = "service-indisponible";

    public static SectionTolerante<T> Ok(T valeur) => new(true, null, valeur);

    public static SectionTolerante<T> Absente(string raison) => new(false, raison, default);
}

public static class SectionTolerante
{
    /// <summary>
    /// Charge une section : 401 ou 403 du service → <c>acces-refuse</c> ; service indisponible ou réponse inattendue (5xx,
    /// contrat invalide) → <c>service-indisponible</c>. Les autres erreurs fonctionnelles (400, 404, 409, 422) concernent la
    /// demande elle-même et remontent à <see cref="ErreursAvalHandler"/> ; l'annulation de la requête aussi.
    /// </summary>
    /// <example>
    /// <code>
    /// var obligations = SectionTolerante.ChargerAsync(() => obligationsApi.ListerAsync(personneId, ct));
    /// var decisions = SectionTolerante.ChargerAsync(() => surveillanceApi.DecisionsAsync(personneId, ct));
    /// await Task.WhenAll(obligations, decisions);
    /// return new DossierTravailleur(await obligations, await decisions);
    /// </code>
    /// </example>
    public static async Task<SectionTolerante<T>> ChargerAsync<T>(Func<Task<T>> chargement)
    {
        ArgumentNullException.ThrowIfNull(chargement);
        try
        {
            return SectionTolerante<T>.Ok(await chargement());
        }
        catch (ErreurAvalException e) when (e.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return SectionTolerante<T>.Absente(SectionTolerante<T>.AccesRefuse);
        }
        catch (ErreurAvalException e) when ((int)e.Status >= 500)
        {
            return SectionTolerante<T>.Absente(SectionTolerante<T>.ServiceIndisponible);
        }
        catch (ServiceIndisponibleException)
        {
            return SectionTolerante<T>.Absente(SectionTolerante<T>.ServiceIndisponible);
        }
    }
}
