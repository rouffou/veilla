using System.Security.Cryptography;

namespace Sepp.BuildingBlocks.Infrastructure.Security;

/// <summary>Clé de données enveloppée par une clé racine : kid versionné de la clé racine et octets enveloppés.</summary>
public sealed record CleDonneesEnveloppee(string Kid, byte[] Octets);

/// <summary>
/// Clé racine (KEK) qui enveloppe des clés de données (ARC-44, ADR 0009, ADR 0012) : en Azure, clé RSA non exportable du
/// Key Vault ou du Managed HSM de la zone, qui ne quitte jamais le coffre ; seules les clés de données enveloppées sont
/// conservées, avec le kid versionné de la clé racine pour pouvoir les déballer après une rotation.
/// </summary>
public interface ICleRacine
{
    /// <summary>Enveloppe la clé de données avec la version courante de la clé racine.</summary>
    Task<CleDonneesEnveloppee> EnvelopperAsync(ReadOnlyMemory<byte> cleDonnees, CancellationToken cancellationToken);

    /// <summary>Déballe une clé de données avec la version <paramref name="kid"/> de la clé racine.</summary>
    /// <exception cref="EnveloppeAltereeException">
    /// Kid étranger à la clé racine, clé enveloppée refusée (HTTP 400 du coffre) ou invalide.
    /// </exception>
    /// <exception cref="CleRacineInaccessibleException">
    /// Version introuvable (HTTP 404) ou opération refusée (HTTP 401/403) : erreur d'exploitation, pas une altération (#377).
    /// </exception>
    Task<byte[]> DesenvelopperAsync(string kid, byte[] cleEnveloppee, CancellationToken cancellationToken);
}

/// <summary>La clé enveloppée ou son kid ne correspondent pas à la clé racine : donnée altérée ou étrangère.</summary>
public sealed class EnveloppeAltereeException : CryptographicException
{
    public EnveloppeAltereeException(string message) : base(message)
    {
    }

    public EnveloppeAltereeException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public EnveloppeAltereeException()
    {
    }
}

/// <summary>Cause de l'indisponibilité d'une version de la clé racine (#377).</summary>
public enum CauseCleRacineInaccessible
{
    /// <summary>Version introuvable dans le coffre (HTTP 404) : supprimée ou purgée.</summary>
    Introuvable,

    /// <summary>Opération refusée par le coffre (HTTP 401 ou 403) : droits de l'identité manquants ou version désactivée.</summary>
    AccesRefuse,
}

/// <summary>
/// La version de la clé racine désignée par un kid est inutilisable (ARC-44, NF-62) : erreur d'exploitation (rotation mal
/// conduite, version supprimée ou désactivée, rôle retiré), à distinguer d'une donnée altérée. Le kid n'est pas un secret
/// mais ne sert qu'au journal d'exploitation.
/// </summary>
public sealed class CleRacineInaccessibleException : Exception
{
    public CleRacineInaccessibleException(string kid, CauseCleRacineInaccessible cause, Exception? innerException)
        : base(cause == CauseCleRacineInaccessible.Introuvable
            ? "Version de la clé racine introuvable dans le coffre (supprimée ou purgée)."
            : "Opération sur la version de la clé racine refusée par le coffre (droits manquants ou version désactivée).", innerException)
    {
        Kid = kid;
        Cause = cause;
    }

    public CleRacineInaccessibleException(string message) : base(message)
    {
    }

    public CleRacineInaccessibleException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public CleRacineInaccessibleException()
    {
    }

    /// <summary>Kid versionné de la clé racine, pour le journal d'exploitation uniquement.</summary>
    public string Kid { get; } = string.Empty;

    public CauseCleRacineInaccessible Cause { get; }
}
