using Sepp.BuildingBlocks.Domain;
using Sepp.SurveillanceMedicale.Domain.Dossiers;
using Sepp.SurveillanceMedicale.Domain.Examens;

namespace Sepp.SurveillanceMedicale.Domain.Protocoles;

/// <summary>
/// SAN-23 : valeur de référence d'une mesure (intervalle normal), historisée par période de validité (DAT-04).
/// Protocole médical validé par le CPMT dirigeant ; aucune donnée personnelle.
/// </summary>
public sealed class ValeurReference : AggregateRoot
{
    private ValeurReference()
    {
    }

    private ValeurReference(Guid id, TypeActe typeActe, string codeMesure, LocalizedLabel libelle, string unite, decimal? minimum, decimal? maximum, Validity validite)
        : base(id)
    {
        if (minimum is null && maximum is null)
        {
            throw new DomainException("Une valeur de référence a un minimum, un maximum ou les deux.");
        }

        if (minimum is { } min && maximum is { } max && min > max)
        {
            throw new DomainException("Le minimum de référence dépasse le maximum.");
        }

        TypeActe = typeActe;
        CodeMesure = Garde.Code(codeMesure, "mesure");
        Libelle = libelle;
        Unite = Garde.Requis(unite, "unité", 20);
        Minimum = minimum;
        Maximum = maximum;
        Validite = validite;
    }

    public TypeActe TypeActe { get; private set; }

    public string CodeMesure { get; private set; } = string.Empty;

    public LocalizedLabel Libelle { get; private set; } = null!;

    public string Unite { get; private set; } = string.Empty;

    public decimal? Minimum { get; private set; }

    public decimal? Maximum { get; private set; }

    public Validity Validite { get; private set; }

    public static ValeurReference Definir(TypeActe typeActe, string codeMesure, LocalizedLabel libelle, string unite, decimal? minimum, decimal? maximum, DateOnly valideDu) =>
        new(NewId(), typeActe, codeMesure, libelle, unite, minimum, maximum, new Validity(valideDu));

    public bool EstNormale(decimal valeur) => (Minimum is null || valeur >= Minimum) && (Maximum is null || valeur <= Maximum);

    /// <summary>DAT-04 : on clôture la période au lieu de modifier la valeur.</summary>
    public void Cloturer(DateOnly fin) => Validite = Validite.CloseAt(fin);
}

public enum TypeReponse
{
    OuiNon,
    Texte,
    Nombre,
    Choix,
}

/// <summary>Question d'un modèle de questionnaire de santé (libellé multilingue, DAT-07).</summary>
public sealed record QuestionModele(string Code, LocalizedLabel Libelle, TypeReponse TypeReponse, bool Obligatoire, IReadOnlyList<string> Choix);

/// <summary>
/// SAN-22 : modèle de questionnaire de santé (antécédents, plaintes, mode de vie…), versionné. Les réponses sont
/// stockées chiffrées dans le dossier ; le modèle lui-même ne contient aucune donnée personnelle.
/// </summary>
public sealed class ModeleQuestionnaire : AggregateRoot
{
    private ModeleQuestionnaire()
    {
    }

    private ModeleQuestionnaire(Guid id, string code, int version, LocalizedLabel titre, IReadOnlyList<QuestionModele> questions) : base(id)
    {
        if (questions.Count == 0)
        {
            throw new DomainException("Un modèle de questionnaire comporte au moins une question.");
        }

        var codes = questions.Select(q => Garde.Code(q.Code, "question")).ToList();
        if (codes.Distinct(StringComparer.Ordinal).Count() != codes.Count)
        {
            throw new DomainException("Les codes de question d'un modèle sont uniques.");
        }

        foreach (var q in questions.Where(q => q.TypeReponse == TypeReponse.Choix && q.Choix.Count < 2))
        {
            throw new DomainException($"La question {q.Code} à choix propose au moins deux réponses.");
        }

        Code = Garde.Code(code, "modèle");
        Version = version;
        Titre = titre;
        Questions = [.. questions.Select(q => q with { Code = Garde.Code(q.Code, "question"), Choix = [.. q.Choix] })];
    }

    public string Code { get; private set; } = string.Empty;

    public int Version { get; private set; }

    public LocalizedLabel Titre { get; private set; } = null!;

    public IReadOnlyList<QuestionModele> Questions { get; private set; } = [];

    public static ModeleQuestionnaire Creer(string code, int version, LocalizedLabel titre, IReadOnlyList<QuestionModele> questions) =>
        new(NewId(), code, version, titre, questions);

    /// <summary>Contrôle des réponses : questions connues, réponses obligatoires présentes, format conforme au type.</summary>
    public void VerifierReponses(IReadOnlyList<ReponseQuestionnaire> reponses)
    {
        foreach (var (codeQuestion, valeur) in reponses)
        {
            var question = Questions.FirstOrDefault(q => q.Code == codeQuestion?.Trim().ToUpperInvariant())
                ?? throw new DomainException($"Question inconnue du modèle {Code} : « {codeQuestion} ».");
            var conforme = question.TypeReponse switch
            {
                TypeReponse.OuiNon => valeur is "OUI" or "NON",
                TypeReponse.Nombre => decimal.TryParse(valeur, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _),
                TypeReponse.Choix => question.Choix.Contains(valeur),
                _ => !string.IsNullOrWhiteSpace(valeur),
            };
            if (!conforme)
            {
                throw new DomainException($"Réponse non conforme à la question {question.Code} ({question.TypeReponse}).");
            }
        }

        var manquantes = Questions.Where(q => q.Obligatoire && !reponses.Any(r => r.CodeQuestion?.Trim().ToUpperInvariant() == q.Code)).Select(q => q.Code).ToList();
        if (manquantes.Count > 0)
        {
            throw new DomainException($"Réponses obligatoires manquantes : {string.Join(", ", manquantes)}.");
        }
    }
}

public enum RubriqueModeleTexte
{
    Anamnese,
    ExamenClinique,
    Decision,
    Recommandation,
    Courrier,
}

/// <summary>
/// SAN-24 : modèle de texte propre à un CPMT (phrases types d'anamnèse, d'examen clinique, de recommandation).
/// La dictée vocale est hors périmètre : elle alimentera la saisie par un composant externe à intégrer.
/// </summary>
public sealed class ModeleTexte : AggregateRoot
{
    private ModeleTexte()
    {
    }

    private ModeleTexte(Guid id, string cpmtId, string code, RubriqueModeleTexte rubrique, string titre, string texte) : base(id)
    {
        CpmtId = Garde.Requis(cpmtId, "CPMT", 100);
        Code = Garde.Code(code, "code");
        Modifier(rubrique, titre, texte);
    }

    /// <summary>Propriétaire du modèle (identifiant OIDC <c>sub</c>).</summary>
    public string CpmtId { get; private set; } = string.Empty;

    public string Code { get; private set; } = string.Empty;

    public RubriqueModeleTexte Rubrique { get; private set; }

    public string Titre { get; private set; } = string.Empty;

    public string Texte { get; private set; } = string.Empty;

    public static ModeleTexte Creer(string cpmtId, string code, RubriqueModeleTexte rubrique, string titre, string texte) =>
        new(NewId(), cpmtId, code, rubrique, titre, texte);

    public void Modifier(RubriqueModeleTexte rubrique, string titre, string texte)
    {
        Rubrique = rubrique;
        Titre = Garde.Requis(titre, "titre", 200);
        Texte = Garde.Requis(texte, "texte", 10_000);
    }
}
