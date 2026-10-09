using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

using Sepp.Contracts.Decisions;
using Sepp.Contracts.Documents;
using Sepp.Contracts.Examens;
using Sepp.Contracts.SurveillanceMedicale;

using Shouldly;

namespace Sepp.Contracts.Tests;

/// <summary>
/// Garde-fous des contrats d'événements : nommage et versionnement (ARC-34), aucune donnée clinique,
/// psychosociale ou identifiante (ARC-06, DAT-06), catalogue documenté à jour (§15.4).
/// </summary>
public partial class ContractRulesTests
{
    private static readonly Type[] Events = typeof(IntegrationEvent).Assembly.GetTypes()
        .Where(t => t is { IsAbstract: false } && t.IsSubclassOf(typeof(IntegrationEvent)))
        .ToArray();

    private static readonly HashSet<Type> AllowedTypes =
    [
        typeof(Guid), typeof(Guid?), typeof(DateOnly), typeof(DateOnly?), typeof(DateTimeOffset), typeof(DateTimeOffset?),
        typeof(string), typeof(int), typeof(int?), typeof(decimal), typeof(decimal?), typeof(bool),
        typeof(IReadOnlyList<string>), typeof(IReadOnlyList<Guid>), typeof(IReadOnlyList<DateOnly>),
    ];

    /// <summary>Termes qui trahiraient un contenu clinique, psychosocial ou une donnée d'identité.</summary>
    private static readonly string[] ForbiddenTerms =
    [
        "niss", "nom", "prenom", "adresse", "email", "telephone", "naissance", "diagnostic", "anamnese", "clinique",
        "pathologie", "symptome", "traitement", "observation", "resultat", "commentaire", "description", "contenu",
        "faits", "temoignage", "remarque", "note",
    ];

    public static TheoryData<Type> AllEvents() => [.. Events];

    [Fact]
    public void Le_catalogue_couvre_les_evenements_du_cahier_des_charges() =>
        Events.Length.ShouldBeGreaterThanOrEqualTo(44);

    [Theory]
    [MemberData(nameof(AllEvents))]
    public void Chaque_evenement_a_un_contrat_nomme_et_versionne(Type eventType)
    {
        var contract = EventContractAttribute.Of(eventType);

        ContractName().IsMatch(contract.Name).ShouldBeTrue($"{contract.Name} : attendu « service.evenement » en kebab-case.");
        contract.Version.ShouldBeGreaterThan(0);
        eventType.IsSealed.ShouldBeTrue();
    }

    [Fact]
    public void Les_noms_de_contrat_sont_uniques() =>
        Events.Select(t => EventContractAttribute.Of(t).FullName).ShouldBeUnique();

    [Theory]
    [MemberData(nameof(AllEvents))]
    public void Un_evenement_ne_transporte_que_des_identifiants_dates_statuts_et_categories(Type eventType)
    {
        foreach (var property in eventType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.DeclaringType == eventType))
        {
            AllowedTypes.ShouldContain(property.PropertyType, $"{eventType.Name}.{property.Name} : type {property.PropertyType.Name} non autorisé (ARC-06).");
            var words = CamelWords().Matches(property.Name).Select(m => m.Value.ToLowerInvariant());
            words.Intersect(ForbiddenTerms).ShouldBeEmpty(
                $"{eventType.Name}.{property.Name} évoque une donnée sensible interdite dans un événement (ARC-06).");
        }
    }

    [Theory]
    [MemberData(nameof(AllEvents))]
    public void Un_evenement_est_serialisable_en_json(Type eventType)
    {
        var ctor = eventType.GetConstructors().Single();
        var args = ctor.GetParameters().Select(p => SampleValue(p.ParameterType)).ToArray();
        var instance = ctor.Invoke(args);

        var json = JsonSerializer.Serialize(instance, eventType, JsonSerializerOptions.Web);
        var back = JsonSerializer.Deserialize(json, eventType, JsonSerializerOptions.Web);

        back.ShouldBe(instance, "Égalité structurelle après aller-retour JSON (listes comparées par référence exclues).");
    }

    [Fact]
    public void Le_catalogue_documente_est_a_jour()
    {
        var doc = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "architecture", "evenements.md"));
        foreach (var eventType in Events)
        {
            doc.ShouldContain($"`{EventContractAttribute.Of(eventType).FullName}`", customMessage: "Mettre à jour docs/architecture/evenements.md.");
        }
    }

    [Fact]
    public void Une_charge_v1_de_DecisionEmise_sans_ExamenId_est_lisible()
    {
        const string json = """
            {"decisionId":"0198a1f0-0000-7000-8000-000000000001","personneId":"0198a1f0-0000-7000-8000-000000000002",
             "affilieId":"0198a1f0-0000-7000-8000-000000000003","categorie":"APTE","codesMesures":["M1"],
             "valideJusquAu":"2027-09-28","eventId":"0198a1f0-0000-7000-8000-000000000004","occurredAt":"2026-09-28T08:00:00+00:00"}
            """;

        var decision = JsonSerializer.Deserialize<DecisionEmise>(json, JsonSerializerOptions.Web);

        decision.ShouldNotBeNull();
        decision.ExamenId.ShouldBeNull();
        decision.CodesMesures.ShouldBe(["M1"]);
    }

    [Fact]
    public void Une_charge_v1_de_DocumentPublie_sans_ObjetType_ni_ObjetId_est_lisible()
    {
        const string json = """
            {"documentId":"0198a1f0-0000-7000-8000-000000000001","zone":"STANDARD","typeDestinataire":"EMPLOYEUR",
             "destinataireId":"0198a1f0-0000-7000-8000-000000000002","codeModele":"DECISION",
             "eventId":"0198a1f0-0000-7000-8000-000000000004","occurredAt":"2026-09-28T08:00:00+00:00"}
            """;

        var document = JsonSerializer.Deserialize<DocumentPublie>(json, JsonSerializerOptions.Web);

        document.ShouldNotBeNull();
        document.ObjetType.ShouldBeNull();
        document.ObjetId.ShouldBeNull();
    }

    [Fact]
    public void Les_codes_de_type_d_examen_respectent_le_format_et_sont_uniques()
    {
        TypesExamen.Connus.ShouldNotBeEmpty();
        TypesExamen.Connus.ShouldBeUnique();
        foreach (var code in TypesExamen.Connus)
        {
            ExamCode().IsMatch(code).ShouldBeTrue($"{code} : attendu ^[A-Z0-9_]+$.");
        }

        TypesExamen.Connus.ShouldContain(TypesExamen.ActesMedicauxSupplementaires);
        TypesExamen.Connus.ShouldContain(TypesExamen.EstimationPotentielTravail);
        TypesExamen.Connus.ShouldContain(TypesExamen.AutreLegislation);
    }

    [Fact]
    public void Les_codes_de_TypesExamen_declares_sont_tous_dans_Connus()
    {
        var declares = typeof(TypesExamen).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!);

        declares.OrderBy(c => c, StringComparer.Ordinal).ShouldBe(TypesExamen.Connus.OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public void Les_codes_de_categorie_de_decision_respectent_le_format_et_sont_uniques()
    {
        CategoriesDecision.Connus.ShouldNotBeEmpty();
        CategoriesDecision.Connus.ShouldBeUnique();
        foreach (var code in CategoriesDecision.Connus)
        {
            ExamCode().IsMatch(code).ShouldBeTrue($"{code} : attendu ^[A-Z0-9_]+$.");
        }
    }

    [Fact]
    public void Les_codes_de_CategoriesDecision_declares_sont_tous_dans_Connus()
    {
        var declares = typeof(CategoriesDecision).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!);

        declares.OrderBy(c => c, StringComparer.Ordinal).ShouldBe(CategoriesDecision.Connus.OrderBy(c => c, StringComparer.Ordinal));
    }

    private static object? SampleValue(Type type) => type switch
    {
        _ when type == typeof(Guid) => Guid.CreateVersion7(),
        _ when type == typeof(DateOnly) || type == typeof(DateOnly?) => new DateOnly(2026, 9, 28),
        _ when type == typeof(DateTimeOffset) => new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero),
        _ when type == typeof(string) => "X",
        _ when type == typeof(int) => 1,
        _ when type == typeof(decimal) => 1.5m,
        _ when type == typeof(bool) => true,
        // Listes vides : l'égalité de record compare les listes par référence.
        _ => null,
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sepp.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Racine du dépôt introuvable.");
    }

    [GeneratedRegex("[A-Z][a-z]*|[a-z]+")]
    private static partial Regex CamelWords();

    [GeneratedRegex("^[A-Z0-9_]+$")]
    private static partial Regex ExamCode();

    [GeneratedRegex("^[a-z]+(-[a-z]+)*\\.[a-z]+(-[a-z]+)*$")]
    private static partial Regex ContractName();
}
