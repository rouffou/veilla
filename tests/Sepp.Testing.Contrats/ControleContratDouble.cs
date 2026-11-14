using System.Collections.Concurrent;

using Shouldly;

namespace Sepp.Testing.Contrats;

/// <summary>
/// Contrôle de contrat à brancher dans un double HTTP de test (<c>ServicesAvalSimules</c> des BFF) : chaque réponse programmée
/// est confrontée à l'instantané OpenAPI du fournisseur dès sa programmation (échec immédiat, c'est le double qui est faux),
/// et chaque requête émise par le code du consommateur est confrontée à ce même instantané (écarts remontés en fin de test par
/// <see cref="ExigerRequetesConformes"/>). Les hôtes simulés sont de la forme <c>&lt;service&gt;.test</c> ; <paramref name="alias"/>
/// associe un hôte à l'instantané d'un autre service (par ex. l'instance Documents de la zone médicale partage le contrat Documents) ;
/// <paramref name="perimetre"/> (méthode, chemin) restreint le contrôle aux appels concernés (le relais générique du BFF interne
/// transporte des routes fictives qui n'ont pas à exister chez les fournisseurs).
/// </summary>
public sealed class ControleContratDouble(IReadOnlyDictionary<string, string>? alias = null, Func<string, string, bool>? perimetre = null)
{
    private readonly ConcurrentQueue<string> _ecarts = new();

    /// <summary>Contrôle une réponse programmée ; lève immédiatement si le double est incompatible avec le fournisseur.</summary>
    public void ControlerReponse(string hote, string methode, string chemin, int status, string corpsJson)
    {
        if (Fournisseur(hote) is not { } fournisseur || !(perimetre?.Invoke(methode, chemin) ?? true))
        {
            return;
        }

        var ecarts = ContratConsommateur.VerifierReponse(fournisseur, methode, chemin, status, corpsJson);
        if (ecarts.Count > 0)
        {
            throw new ShouldAssertException(
                $"Double de test incompatible avec le contrat du fournisseur :{Environment.NewLine}{string.Join(Environment.NewLine, ecarts.Select(e => $"  - {e}"))}");
        }
    }

    /// <summary>Enregistre les écarts d'une requête émise vers un hôte simulé.</summary>
    public void ControlerRequete(string hote, string methode, string cheminEtQuery, string? corpsJson)
    {
        if (Fournisseur(hote) is not { } fournisseur || !(perimetre?.Invoke(methode, cheminEtQuery.Split('?')[0]) ?? true))
        {
            return;
        }

        foreach (var ecart in ContratConsommateur.VerifierRequete(fournisseur, methode, cheminEtQuery, corpsJson))
        {
            _ecarts.Enqueue(ecart);
        }
    }

    /// <summary>Échoue si une requête émise pendant le test ne correspond à aucune opération conforme de l'instantané.</summary>
    public void ExigerRequetesConformes()
    {
        if (_ecarts.IsEmpty)
        {
            return;
        }

        throw new ShouldAssertException(
            $"Le consommateur a émis des requêtes non conformes au contrat du fournisseur :{Environment.NewLine}"
            + string.Join(Environment.NewLine, _ecarts.Distinct().Select(e => $"  - {e}")));
    }

    private string? Fournisseur(string hote)
    {
        const string suffixe = ".test";
        if (!hote.EndsWith(suffixe, StringComparison.Ordinal))
        {
            return null;
        }

        var service = hote[..^suffixe.Length];
        return alias is not null && alias.TryGetValue(service, out var cible) ? cible : service;
    }
}
