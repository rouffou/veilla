using System.Security.Cryptography;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Sepp.BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Chiffrement des champs par enveloppe (ARC-44, ARC-45, ADR 0012), configuré par une section (<c>Encryption</c>,
/// <c>ZoneMedicale:Encryption</c>…) :
/// <list type="bullet">
/// <item><c>Enveloppe:Type</c> : <c>KeyVault</c> (clé racine du service dans le Key Vault ou le Managed HSM de sa zone,
/// obligatoire hors développement et test) ou <c>Local</c> (clé racine en configuration, développement et tests) ; aucun
/// défaut implicite.</item>
/// <item><c>Enveloppe:CleKek</c> : pour <c>KeyVault</c>, URI de la clé racine <b>sans version</b>
/// (<c>https://&lt;coffre&gt;/keys/&lt;service&gt;-fields-kek</c>), injecté par Terraform ; pour <c>Local</c>, une section
/// <c>Courante</c> et <c>Versions:&lt;version&gt;</c> (256 bits en base64).</item>
/// <item><c>CurrentKeyId</c> : identifiant de la clé de données (DEK) courante.</item>
/// <item><c>Deks:&lt;id&gt;</c> : DEK enveloppée par la clé racine, <c>&lt;kid versionné&gt;|&lt;base64&gt;</c> (secret Key
/// Vault en Azure : la valeur enveloppée ne se déballe que dans le coffre).</item>
/// <item><c>Keys:&lt;id&gt;</c> (facultatif) : anciennes clés AES en clair du format <c>v1</c>, en lecture seule.</item>
/// </list>
/// Les DEK sont déballées une fois, au chargement, puis gardées en mémoire : aucun appel au coffre par valeur.
/// </summary>
public sealed class ChiffrementChamps
{
    public const string KeyVault = "KeyVault";
    public const string Local = "Local";

    /// <summary>Préfixe du kid de la clé racine locale et usage lié par les données associées.</summary>
    public const string UsageLocal = "field-kek";

    private const int TailleCle = 32;

    private ChiffrementChamps(ICleRacine cleRacine, bool estLocal, IFieldKeyProvider cles, string chemin)
    {
        CleRacine = cleRacine;
        EstLocal = estLocal;
        Cles = cles;
        Chemin = chemin;
    }

    /// <summary>Clé racine du service : sert aussi à déballer d'autres clés du service (index aveugle).</summary>
    public ICleRacine CleRacine { get; }

    /// <summary>Vrai pour la clé racine locale (développement et tests).</summary>
    public bool EstLocal { get; }

    /// <summary>DEK déballées et anciennes clés <c>v1</c>.</summary>
    public IFieldKeyProvider Cles { get; }

    /// <summary>Chemin de la section de configuration (messages d'erreur).</summary>
    public string Chemin { get; }

    /// <summary>Charge la section au démarrage (appel bloquant : une fois, avant de servir).</summary>
    public static ChiffrementChamps Charger(IConfiguration section, IHostEnvironment environnement, IOperationsCleRacine? operations = null) =>
        ChargerAsync(section, environnement, operations, CancellationToken.None).GetAwaiter().GetResult();

    public static async Task<ChiffrementChamps> ChargerAsync(IConfiguration section, IHostEnvironment environnement, IOperationsCleRacine? operations, CancellationToken cancellationToken)
    {
        var chemin = (section as IConfigurationSection)?.Path ?? "Encryption";
        var type = section["Enveloppe:Type"];
        ICleRacine cleRacine;
        switch (type)
        {
            case KeyVault:
                var parametre = $"{chemin}:Enveloppe:CleKek";
                cleRacine = new CleRacineKeyVault(CleRacineKeyVault.LireUriCle(section["Enveloppe:CleKek"], parametre), operations ?? new OperationsCleRacineAzure(), parametre);
                break;
            case Local:
                CleRacineLocale.VerifierEnvironnement(environnement, $"{chemin}:Enveloppe:Type");
                cleRacine = CleRacineLocale.Depuis(section.GetSection("Enveloppe:CleKek"), $"local:{UsageLocal}:", UsageLocal);
                break;
            default:
                throw new InvalidOperationException(
                    $"{chemin}:Enveloppe:Type doit valoir « {KeyVault} » (clé racine du coffre de la zone) ou « {Local} » (développement) ; valeur actuelle : « {type} » (ARC-44).");
        }

        var courante = section["CurrentKeyId"];
        if (string.IsNullOrWhiteSpace(courante))
        {
            throw new InvalidOperationException($"{chemin}:CurrentKeyId manquant.");
        }

        var deks = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var dek in section.GetSection("Deks").GetChildren())
        {
            deks[dek.Key] = await DesenvelopperAsync(cleRacine, dek.Value, $"{chemin}:Deks:{dek.Key}", cancellationToken);
        }

        if (!deks.ContainsKey(courante))
        {
            throw new InvalidOperationException($"{chemin}:Deks:{courante} manquant : clé de données courante (CurrentKeyId).");
        }

        var historiques = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var cle in section.GetSection("Keys").GetChildren())
        {
            var octets = Convert.FromBase64String(cle.Value ?? string.Empty);
            historiques[cle.Key] = octets.Length == TailleCle
                ? octets
                : throw new InvalidOperationException($"La clé de chiffrement historique '{cle.Key}' doit faire 256 bits.");
        }

        return new ChiffrementChamps(cleRacine, type == Local, new CleChampsEnMemoire(courante, deks, historiques), chemin);
    }

    /// <summary>Déballe une clé de 256 bits du service enveloppée par sa clé racine (<c>&lt;kid&gt;|&lt;base64&gt;</c>).</summary>
    public byte[] Desenvelopper(string? valeur, string parametre) =>
        DesenvelopperAsync(CleRacine, valeur, parametre, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>Forme stockée d'une clé enveloppée : <c>&lt;kid versionné&gt;|&lt;base64&gt;</c>.</summary>
    public static string Formater(CleDonneesEnveloppee cle) => $"{cle.Kid}|{Convert.ToBase64String(cle.Octets)}";

    /// <summary>
    /// Configuration locale complète d'une section (clé racine locale aléatoire et DEK enveloppée), pour les tests et le
    /// développement : <b>sans valeur de sécurité</b>.
    /// </summary>
    /// <param name="section">Section de configuration du chiffrement des champs.</param>
    /// <param name="dekId">Identifiant de la DEK courante.</param>
    /// <param name="autresCles">Autres clés du service à envelopper (clé de configuration -> clé en clair), par exemple l'index aveugle.</param>
    public static Dictionary<string, string> ConfigurationLocale(string section, string dekId = "test", IReadOnlyDictionary<string, byte[]>? autresCles = null)
    {
        var kek = RandomNumberGenerator.GetBytes(TailleCle);
        var cleRacine = new CleRacineLocale($"local:{UsageLocal}:", UsageLocal, "v1", new Dictionary<string, byte[]> { ["v1"] = kek });
        string Envelopper(byte[] cle) => Formater(cleRacine.EnvelopperAsync(cle, CancellationToken.None).GetAwaiter().GetResult());

        var configuration = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{section}:Enveloppe:Type"] = Local,
            [$"{section}:Enveloppe:CleKek:Courante"] = "v1",
            [$"{section}:Enveloppe:CleKek:Versions:v1"] = Convert.ToBase64String(kek),
            [$"{section}:CurrentKeyId"] = dekId,
            [$"{section}:Deks:{dekId}"] = Envelopper(RandomNumberGenerator.GetBytes(TailleCle)),
        };
        foreach (var (cle, valeur) in autresCles ?? new Dictionary<string, byte[]>())
        {
            configuration[cle] = Envelopper(valeur);
        }

        return configuration;
    }

    private static async Task<byte[]> DesenvelopperAsync(ICleRacine cleRacine, string? valeur, string parametre, CancellationToken cancellationToken)
    {
        var separateur = valeur?.LastIndexOf('|') ?? -1;
        if (valeur is null || separateur <= 0)
        {
            throw new InvalidOperationException($"{parametre} doit valoir « <kid versionné de la clé racine>|<clé enveloppée en base64> ».");
        }

        byte[] enveloppee;
        try
        {
            enveloppee = Convert.FromBase64String(valeur[(separateur + 1)..]);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"{parametre} : clé enveloppée non lisible (base64 attendu).", ex);
        }

        var cle = await cleRacine.DesenvelopperAsync(valeur[..separateur], enveloppee, cancellationToken);
        if (cle.Length != TailleCle)
        {
            CryptographicOperations.ZeroMemory(cle);
            throw new InvalidOperationException($"{parametre} : la clé déballée doit faire 256 bits.");
        }

        return cle;
    }

    /// <summary>DEK déballées et anciennes clés, en mémoire pour la durée du processus.</summary>
    private sealed class CleChampsEnMemoire(string courante, Dictionary<string, byte[]> deks, Dictionary<string, byte[]> historiques) : IFieldKeyProvider
    {
        public string CurrentKeyId { get; } = courante;

        public byte[] GetKey(string keyId) =>
            deks.TryGetValue(keyId, out var cle) ? cle : throw new CryptographicException($"Clé de données '{keyId}' inconnue.");

        public byte[] GetLegacyKey(string keyId) =>
            historiques.TryGetValue(keyId, out var cle) ? cle : throw new CryptographicException($"Clé de chiffrement historique '{keyId}' inconnue.");
    }
}
