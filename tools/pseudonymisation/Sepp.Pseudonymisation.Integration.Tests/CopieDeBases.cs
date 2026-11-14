using System.Globalization;
using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using Npgsql;

using NpgsqlTypes;

using Sepp.Affilies.Adapters.Persistence;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.Personnes.Adapters.Persistence;
using Sepp.Reintegration.Adapters.Persistence;
using Sepp.SurveillanceMedicale.Adapters.Persistence;

using Testcontainers.PostgreSql;

namespace Sepp.Pseudonymisation.Integration.Tests;

/// <summary>
/// Quatre bases PostgreSQL réelles (migrations des services) peuplées de valeurs « ORIGINAL… » chiffrées par la clé source,
/// puis pseudonymisées par l'outil via sa configuration. Aucune donnée ni clé réelle : tout est généré pour le test.
/// </summary>
public sealed class CopieDeBases : IAsyncLifetime
{
    public const string Marqueur = "ORIGINAL";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Dictionary<string, string> Connexions { get; } = [];

    public Dictionary<string, FieldEncryptor> Sources { get; } = [];

    public Dictionary<string, FieldEncryptor> Cibles { get; } = [];

    public byte[] CleIndexCible { get; } = RandomNumberGenerator.GetBytes(32);

    public byte[] CleSecrete { get; } = RandomNumberGenerator.GetBytes(32);

    public Guid P1 { get; } = Guid.NewGuid();

    public Guid P2 { get; } = Guid.NewGuid();

    public string NissP1 { get; } = NissValide("850730", 33);

    public string NissP2 { get; } = NissValide("030102", 40, apres2000: true);

    public string BceA1 { get; } = NumeroBce("0200065");

    public List<Guid> PersonnesIds { get; } = [];

    public Dictionary<string, Dictionary<string, string>> ConfigurationDesServices { get; } = [];

    public IConfiguration Configuration(string environnement = "recette", string? confirmation = "recette", string? services = null)
    {
        var valeurs = new Dictionary<string, string?>
        {
            ["Pseudonymisation:Environnement"] = environnement,
            ["Pseudonymisation:Confirmation"] = confirmation,
            ["Pseudonymisation:Cle"] = Convert.ToBase64String(CleSecrete),
            ["Pseudonymisation:EnvironnementHote"] = "Testing",
            ["Pseudonymisation:Services"] = services,
        };
        foreach (var (service, connexion) in Connexions)
        {
            valeurs[$"Pseudonymisation:Services:{service}:Connexion"] = connexion;
        }

        foreach (var parametres in ConfigurationDesServices.Values)
        {
            foreach (var (cle, valeur) in parametres)
            {
                valeurs[cle] = valeur;
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(valeurs).Build();
    }

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        foreach (var service in CatalogueRegles.Tous)
        {
            await CreerBaseAsync(service.Nom);
            if (service.Chiffrement)
            {
                PreparerCles(service);
            }
        }

        await MigrerAsync();
        await PeuplerPersonnesAsync();
        await PeuplerAffiliesAsync();
        await PeuplerSurveillanceMedicaleAsync();
        await PeuplerReintegrationAsync();
    }

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    /// <summary>Dates de naissance d'origine, relevées avant l'exécution.</summary>
    public Dictionary<Guid, DateOnly> NaissancesOriginales { get; } = [];

    private Task<IReadOnlyList<RapportService>>? _execution;

    /// <summary>Exécute l'outil une seule fois (par sa configuration) sur les quatre copies.</summary>
    public Task<IReadOnlyList<RapportService>> ExecuterOutilAsync() => _execution ??= ExecuterAsync();

    private async Task<IReadOnlyList<RapportService>> ExecuterAsync()
    {
        foreach (var ligne in await LireAsync(Connexions[CatalogueRegles.Personnes], "SELECT id, date_naissance FROM personne"))
        {
            NaissancesOriginales[(Guid)ligne[0]!] = Techniques.Date(ligne[1]);
        }

        return await Execution.ExecuterAsync(Configuration(), TextWriter.Null, CancellationToken.None);
    }

    public async Task<string> CreerBaseVideAsync(string service)
    {
        var nom = $"copie_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(_postgres.GetConnectionString());
        await admin.OpenAsync();
        await using var creer = new NpgsqlCommand($"CREATE DATABASE {nom}", admin);
        await creer.ExecuteNonQueryAsync();
        var connexion = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = nom }.ConnectionString;
        await Migrer(service, connexion);
        return connexion;
    }

    public static async Task<List<object?[]>> LireAsync(string connexion, string sql)
    {
        await using var connection = new NpgsqlConnection(connexion);
        await connection.OpenAsync();
        await using var commande = new NpgsqlCommand(sql, connection);
        await using var lecteur = await commande.ExecuteReaderAsync();
        var lignes = new List<object?[]>();
        while (await lecteur.ReadAsync())
        {
            var ligne = new object?[lecteur.FieldCount];
            for (var i = 0; i < ligne.Length; i++)
            {
                ligne[i] = lecteur.IsDBNull(i) ? null : lecteur.GetValue(i);
            }

            lignes.Add(ligne);
        }

        return lignes;
    }

    /// <summary>Insère une ligne : colonnes obligatoires non précisées remplies par des valeurs neutres.</summary>
    public static async Task InsererAsync(string connexion, string table, params (string Colonne, object? Valeur)[] valeurs)
    {
        await using var connection = new NpgsqlConnection(connexion);
        await connection.OpenAsync();
        var explicites = valeurs.ToDictionary(v => v.Colonne, v => v.Valeur, StringComparer.Ordinal);
        var obligatoires = new List<(string Nom, string Type, string Udt)>();
        var jsonb = new HashSet<string>(StringComparer.Ordinal);
        await using (var types = new NpgsqlCommand("SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @t AND data_type = 'jsonb'", connection))
        {
            types.Parameters.AddWithValue("t", table);
            await using var lecteur = await types.ExecuteReaderAsync();
            while (await lecteur.ReadAsync())
            {
                jsonb.Add(lecteur.GetString(0));
            }
        }

        await using (var schema = new NpgsqlCommand(
                         "SELECT column_name, data_type, udt_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @t " +
                         "AND is_nullable = 'NO' AND column_default IS NULL AND is_identity = 'NO'", connection))
        {
            schema.Parameters.AddWithValue("t", table);
            await using var lecteur = await schema.ExecuteReaderAsync();
            while (await lecteur.ReadAsync())
            {
                obligatoires.Add((lecteur.GetString(0), lecteur.GetString(1), lecteur.GetString(2)));
            }
        }

        var colonnes = new List<string>();
        var parametres = new List<NpgsqlParameter>();
        foreach (var (nom, valeur) in explicites)
        {
            colonnes.Add(nom);
            var parametre = Param($"p{parametres.Count}", valeur ?? DBNull.Value);
            if (valeur is string && jsonb.Contains(nom))
            {
                parametre.NpgsqlDbType = NpgsqlDbType.Jsonb;
            }

            parametres.Add(parametre);
        }

        foreach (var (nom, type, udt) in obligatoires.Where(o => !explicites.ContainsKey(o.Nom)))
        {
            colonnes.Add(nom);
            parametres.Add(Neutre($"p{parametres.Count}", nom, type, udt));
        }

        var sql = $"INSERT INTO \"{table}\" ({string.Join(", ", colonnes.Select(c => $"\"{c}\""))}) VALUES ({string.Join(", ", parametres.Select(p => "@" + p.ParameterName))})";
        await using var insertion = new NpgsqlCommand(sql, connection);
        foreach (var p in parametres)
        {
            insertion.Parameters.Add(p);
        }

        await insertion.ExecuteNonQueryAsync();
    }

    public static string NissValide(string naissance, int sequence, bool apres2000 = false)
    {
        var corps = naissance + sequence.ToString("D3", CultureInfo.InvariantCulture);
        var valeur = long.Parse(corps, CultureInfo.InvariantCulture) + (apres2000 ? 2_000_000_000L : 0);
        return corps + (97 - (valeur % 97)).ToString("D2", CultureInfo.InvariantCulture);
    }

    public static string NumeroBce(string sept)
    {
        var baseNumero = "0" + sept;
        return baseNumero + (97 - (long.Parse(baseNumero, CultureInfo.InvariantCulture) % 97)).ToString("D2", CultureInfo.InvariantCulture);
    }

    private static NpgsqlParameter Param(string nom, object valeur) => new() { ParameterName = nom, Value = valeur };

    private static NpgsqlParameter Neutre(string nom, string colonne, string type, string udt)
    {
        if (colonne is "created_by" or "updated_by")
        {
            return Param(nom, $"{Marqueur}.utilisateur@exemple.be");
        }

        return type switch
        {
            "uuid" => Param(nom, Guid.NewGuid()),
            "character varying" or "text" => Param(nom, "x"),
            "integer" => Param(nom, 1),
            "bigint" => Param(nom, 1L),
            "smallint" => Param(nom, (short)1),
            "boolean" => Param(nom, false),
            "date" => Param(nom, new DateOnly(2020, 1, 1)),
            "timestamp with time zone" => Param(nom, DateTimeOffset.UtcNow),
            "jsonb" => new NpgsqlParameter { ParameterName = nom, Value = "{}", NpgsqlDbType = NpgsqlDbType.Jsonb },
            "numeric" => Param(nom, 0m),
            "double precision" => Param(nom, 0d),
            "ARRAY" => udt switch
            {
                "_uuid" => Param(nom, Array.Empty<Guid>()),
                "_int4" => Param(nom, Array.Empty<int>()),
                "_date" => Param(nom, Array.Empty<DateOnly>()),
                _ => Param(nom, Array.Empty<string>()),
            },
            _ => throw new InvalidOperationException($"Type {type} non géré pour {colonne}."),
        };
    }

    private async Task CreerBaseAsync(string service)
    {
        var nom = $"{service.Replace('-', '_')}_copie";
        await using var admin = new NpgsqlConnection(_postgres.GetConnectionString());
        await admin.OpenAsync();
        await using var creer = new NpgsqlCommand($"CREATE DATABASE {nom}", admin);
        await creer.ExecuteNonQueryAsync();
        Connexions[service] = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = nom }.ConnectionString;
    }

    private void PreparerCles(ReglesService service)
    {
        var racine = $"Pseudonymisation:Services:{service.Nom}";
        var parametres = new Dictionary<string, string>();
        foreach (var (cle, valeur) in ChiffrementChamps.ConfigurationLocale($"{racine}:Source:Chiffrement", "src"))
        {
            parametres[cle] = valeur;
        }

        var autres = service.IndexAveugle
            ? new Dictionary<string, byte[]> { [$"{racine}:Cible:IndexAveugle"] = CleIndexCible }
            : null;
        foreach (var (cle, valeur) in ChiffrementChamps.ConfigurationLocale($"{racine}:Cible:Chiffrement", "cible", autres))
        {
            parametres[cle] = valeur;
        }

        ConfigurationDesServices[service.Nom] = parametres;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(parametres!).Build();
        var hote = new EnvironnementOutil("Testing");
        Sources[service.Nom] = new FieldEncryptor(ChiffrementChamps.Charger(configuration.GetSection($"{racine}:Source:Chiffrement"), hote).Cles);
        Cibles[service.Nom] = new FieldEncryptor(ChiffrementChamps.Charger(configuration.GetSection($"{racine}:Cible:Chiffrement"), hote).Cles);
    }

    private async Task MigrerAsync()
    {
        foreach (var service in CatalogueRegles.Tous)
        {
            await Migrer(service.Nom, Connexions[service.Nom]);
        }
    }

    private static async Task Migrer(string service, string connexion)
    {
        var technique = new FieldEncryptor(new CleJetable());
        var utilisateur = new UtilisateurTest();
        switch (service)
        {
            case CatalogueRegles.Personnes:
                await using (var c = new PersonnesDbContext(Options<PersonnesDbContext>(connexion), utilisateur, TimeProvider.System, technique))
                {
                    await c.Database.MigrateAsync();
                }

                break;
            case CatalogueRegles.Affilies:
                await using (var c = new AffiliesDbContext(Options<AffiliesDbContext>(connexion), utilisateur, TimeProvider.System))
                {
                    await c.Database.MigrateAsync();
                }

                break;
            case CatalogueRegles.SurveillanceMedicale:
                await using (var c = new SurveillanceMedicaleDbContext(Options<SurveillanceMedicaleDbContext>(connexion), utilisateur, TimeProvider.System, new Sepp.SurveillanceMedicale.Adapters.Persistence.ChiffrementZoneMedicale(technique)))
                {
                    await c.Database.MigrateAsync();
                }

                break;
            case CatalogueRegles.Reintegration:
                await using (var c = new ReintegrationDbContext(Options<ReintegrationDbContext>(connexion), utilisateur, TimeProvider.System, new Sepp.Reintegration.Adapters.Persistence.ChiffrementZoneMedicale(technique)))
                {
                    await c.Database.MigrateAsync();
                }

                break;
        }
    }

    private static DbContextOptions<T> Options<T>(string connexion)
        where T : DbContext =>
        new DbContextOptionsBuilder<T>().UseNpgsql(connexion).UseSnakeCaseNamingConvention().Options;

    private async Task PeuplerPersonnesAsync()
    {
        var cs = Connexions[CatalogueRegles.Personnes];
        var chiffre = Sources[CatalogueRegles.Personnes];
        async Task Personne(Guid id, string niss, DateOnly naissance, string sexe, string nom)
        {
            PersonnesIds.Add(id);
            await InsererAsync(cs, "personne",
                ("id", id), ("niss_chiffre", chiffre.Encrypt(niss)), ("niss_hash", Convert.ToHexString(RandomNumberGenerator.GetBytes(32))),
                ("nom", $"{Marqueur} {nom}"), ("prenom", $"{Marqueur} Prénom"), ("date_naissance", naissance), ("sexe", sexe),
                ("langue", "fr"), ("canal_prefere", "Email"), ("email", $"{Marqueur}.{nom}@exemple.be"), ("telephone", "+32 470 12 34 56"),
                ("adresse_rue", $"{Marqueur} rue"), ("adresse_numero", "12"), ("adresse_boite", "3"), ("adresse_code_postal", "1000"),
                ("adresse_localite", "Bruxelles"), ("adresse_pays", "BE"));
        }

        await Personne(P1, NissP1, new DateOnly(1985, 7, 30), "Masculin", "Dupont");
        await Personne(P2, NissP2, new DateOnly(2003, 1, 2), "Feminin", "Martin");
        for (var i = 0; i < 40; i++)
        {
            await Personne(Guid.NewGuid(), NissValide("900101", 1 + (2 * i)), new DateOnly(1990, 1, 1), "Masculin", $"Serie{i}");
        }

        await InsererAsync(cs, "etat_particulier", ("type_chiffre", chiffre.Encrypt("Grossesse")), ("personne_id", P2));
        await InsererAsync(cs, "mutation_registre_national",
            ("type", "Deces"), ("reference", "ref-1"), ("rang", 0), ("personne_id", P1),
            ("avant_chiffre", chiffre.Encrypt($"{Marqueur} avant")), ("apres_chiffre", chiffre.Encrypt($"{Marqueur} après")));
        await InsererAsync(cs, "outbox_message");
    }

    private async Task PeuplerAffiliesAsync()
    {
        var cs = Connexions[CatalogueRegles.Affilies];
        var groupe = Guid.NewGuid();
        var affilie = Guid.NewGuid();
        var unite = Guid.NewGuid();
        var ue = "2" + NumeroBce("1111111")[1..9];
        ue = ue[..8] + (97 - (long.Parse(ue[..8], CultureInfo.InvariantCulture) % 97)).ToString("D2", CultureInfo.InvariantCulture);
        await InsererAsync(cs, "groupe", ("id", groupe), ("nom", $"{Marqueur} Groupe"));
        await InsererAsync(cs, "affilie",
            ("id", affilie), ("numero_bce", BceA1), ("denomination", $"{Marqueur} SA"), ("denomination_bce", $"{Marqueur} SA (BCE)"),
            ("groupe_id", groupe), ("langue", "fr"), ("statut", "Actif"), ("categorie_tarifaire", "A"), ("regime_linguistique", "x"));
        await InsererAsync(cs, "unite_etablissement",
            ("id", unite), ("affilie_id", affilie), ("numero_ue_bce", ue), ("nom", $"{Marqueur} unité"),
            ("adresse_rue", $"{Marqueur} rue"), ("adresse_numero", "5"), ("adresse_boite", "1"), ("langue", "fr"));
        await InsererAsync(cs, "site",
            ("unite_etablissement_id", unite), ("nom", $"{Marqueur} site"), ("adresse_rue", $"{Marqueur} rue"), ("adresse_numero", "5"),
            ("latitude", 50.8503), ("longitude", 4.3517));
        await InsererAsync(cs, "contact",
            ("affilie_id", affilie), ("nom", $"{Marqueur} Contact"), ("email", $"{Marqueur}.contact@exemple.be"), ("telephone", "+32 2 123 45 67"), ("role", "x"));
        await InsererAsync(cs, "modification_affilie",
            ("affilie_id", affilie), ("auteur", $"{Marqueur} auteur"), ("avant", $$"""{"denomination":"{{Marqueur}} avant"}"""), ("apres", $$"""{"denomination":"{{Marqueur}} après"}"""));
        await InsererAsync(cs, "ecart_synchronisation",
            ("affilie_id", affilie), ("numero_bce", BceA1), ("detail", $"{Marqueur} détail"), ("resolu_par", $"{Marqueur} gestionnaire"), ("code", "x"), ("statut", "Resolu"));
    }

    private async Task PeuplerSurveillanceMedicaleAsync()
    {
        var cs = Connexions[CatalogueRegles.SurveillanceMedicale];
        var c = Sources[CatalogueRegles.SurveillanceMedicale];
        var dossier = Guid.NewGuid();
        var examen = Guid.NewGuid();
        await InsererAsync(cs, "dossier_sante", ("id", dossier), ("personne_id", P1), ("gestionnaire_cpmt_id", $"{Marqueur} gestionnaire"), ("statut_archivage", "Actif"));
        await InsererAsync(cs, "examen", ("id", examen), ("dossier_id", dossier), ("personne_id", P1), ("professionnel_id", $"{Marqueur} pro"), ("statut", "x"), ("type_examen", "x"));
        await InsererAsync(cs, "observation_clinique",
            ("examen_id", examen), ("anamnese_chiffre", c.Encrypt($"{Marqueur} anamnèse")), ("examen_clinique_chiffre", c.Encrypt($"{Marqueur} examen")));
        await InsererAsync(cs, "resultat_acte",
            ("examen_id", examen), ("type_acte", "x"), ("source", "x"),
            ("valeurs_chiffre", c.Encrypt($$"""[{"code":"A","valeur":1.5,"texte":"{{Marqueur}} texte","inhabituelle":false}]""")),
            ("commentaire_chiffre", c.Encrypt($"{Marqueur} commentaire")));
        await InsererAsync(cs, "piece_jointe",
            ("dossier_id", dossier), ("categorie", "x"), ("titre_chiffre", c.Encrypt($"{Marqueur} titre")), ("description_chiffre", c.Encrypt($"{Marqueur} description")));
        await InsererAsync(cs, "questionnaire_rempli",
            ("dossier_id", dossier), ("modele_code", "x"), ("source", "x"),
            ("reponses_chiffre", c.Encrypt($$"""[{"codeQuestion":"Q1","valeur":"{{Marqueur}} réponse"}]""")));
        await InsererAsync(cs, "decision",
            ("examen_id", examen), ("dossier_id", dossier), ("personne_id", P1), ("auteur_id", $"{Marqueur} auteur"), ("categorie", "x"), ("statut", "x"), ("type_examen", "x"),
            ("justification_chiffre", c.Encrypt($"{Marqueur} justification")), ("recommandations_chiffre", c.Encrypt($"{Marqueur} recommandations")));
        await InsererAsync(cs, "declaration_mp",
            ("dossier_id", dossier), ("auteur_id", $"{Marqueur} auteur"), ("statut", "x"),
            ("contenu_chiffre", c.Encrypt($$"""{"codeMaladie":"M1","description":"{{Marqueur}} description","agentCausal":"{{Marqueur}} agent","expositions":[],"affiliesEmployeurs":[],"derniereDecision":null}""")));
        await InsererAsync(cs, "inbox_message");
    }

    private async Task PeuplerReintegrationAsync()
    {
        var cs = Connexions[CatalogueRegles.Reintegration];
        var c = Sources[CatalogueRegles.Reintegration];
        var incapacite = Guid.NewGuid();
        await InsererAsync(cs, "incapacite", ("id", incapacite), ("personne_id", P1), ("gestionnaire_id", $"{Marqueur} gestionnaire"), ("source", "x"), ("statut", "x"));
        await InsererAsync(cs, "estimation_potentiel",
            ("incapacite_id", incapacite), ("resultat", "x"), ("auteur_id", $"{Marqueur} auteur"), ("commentaire_chiffre", c.Encrypt($"{Marqueur} commentaire")));
        await InsererAsync(cs, "trajet",
            ("personne_id", P1), ("evaluateur_id", $"{Marqueur} évaluateur"), ("note_evaluation_chiffre", c.Encrypt($"{Marqueur} note")),
            ("initiateur", "x"), ("statut", "x"));
        await InsererAsync(cs, "echange_ehealth",
            ("destinataire", "x"), ("sens", "x"), ("objet", "x"), ("statut", "x"),
            ("reference_destinataire", $"{Marqueur} ref"), ("contenu_chiffre", c.Encrypt($"{Marqueur} contenu")));
    }

    private sealed class CleJetable : IFieldKeyProvider
    {
        private readonly byte[] _cle = RandomNumberGenerator.GetBytes(32);

        public string CurrentKeyId => "jetable";

        public byte[] GetKey(string keyId) => _cle;
    }

    private sealed class UtilisateurTest : ICurrentUser
    {
        public bool IsAuthenticated => false;

        public string UserId => "test";

        public IReadOnlySet<string> Roles => new HashSet<string>();

        public bool HasPermission(string permission) => false;
    }
}
