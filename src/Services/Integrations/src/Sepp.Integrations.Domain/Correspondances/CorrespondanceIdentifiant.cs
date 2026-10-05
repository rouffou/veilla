using Sepp.BuildingBlocks.Domain;

namespace Sepp.Integrations.Domain.Correspondances;

/// <summary>
/// Identifiant d'un système externe. Le NISS n'en fait jamais partie : la personne est retrouvée par le service
/// Personnes via son index aveugle (DAT-06), aucune table du service Intégrations ne relie un NISS à une personne.
/// </summary>
public enum TypeIdentifiantExterne
{
    /// <summary>Numéro d'entreprise BCE (dix chiffres).</summary>
    NumeroBce,

    /// <summary>Référence d'une déclaration DIMONA.</summary>
    ReferenceDimona,
}

/// <summary>Objet interne (identifiant UUID du service propriétaire, DAT-02).</summary>
public enum TypeObjetInterne
{
    Affilie,
    Occupation,
}

/// <summary>
/// Correspondance d'identifiants (§15.3, couche anti-corruption) : identifiant externe ↔ identifiant interne,
/// par exemple numéro BCE ↔ <c>affilie_id</c> ou référence DIMONA ↔ <c>occupation_id</c>.
/// Unique par (type externe, valeur externe).
/// </summary>
public sealed class CorrespondanceIdentifiant : AggregateRoot
{
    private CorrespondanceIdentifiant()
    {
    }

    private CorrespondanceIdentifiant(Guid id, TypeIdentifiantExterne typeExterne, string valeurExterne, TypeObjetInterne typeInterne, Guid identifiantInterne)
        : base(id)
    {
        TypeExterne = typeExterne;
        ValeurExterne = valeurExterne;
        TypeInterne = typeInterne;
        IdentifiantInterne = identifiantInterne;
    }

    public TypeIdentifiantExterne TypeExterne { get; private set; }

    public string ValeurExterne { get; private set; } = string.Empty;

    public TypeObjetInterne TypeInterne { get; private set; }

    public Guid IdentifiantInterne { get; private set; }

    public static CorrespondanceIdentifiant Creer(TypeIdentifiantExterne typeExterne, string valeurExterne, TypeObjetInterne typeInterne, Guid identifiantInterne)
    {
        if (!Enum.IsDefined(typeExterne) || !Enum.IsDefined(typeInterne))
        {
            throw new DomainException("Type d'identifiant externe ou d'objet interne inconnu.");
        }

        if (identifiantInterne == Guid.Empty)
        {
            throw new DomainException("L'identifiant interne est obligatoire.");
        }

        if (typeExterne == TypeIdentifiantExterne.NumeroBce && typeInterne != TypeObjetInterne.Affilie)
        {
            throw new DomainException("Un numéro BCE correspond à un affilié.");
        }

        if (typeExterne == TypeIdentifiantExterne.ReferenceDimona && typeInterne != TypeObjetInterne.Occupation)
        {
            throw new DomainException("Une référence DIMONA correspond à une occupation.");
        }

        return new CorrespondanceIdentifiant(NewId(), typeExterne, Normaliser(typeExterne, valeurExterne), typeInterne, identifiantInterne);
    }

    /// <summary>Forme canonique d'un identifiant externe (clé de recherche).</summary>
    public static string Normaliser(TypeIdentifiantExterne typeExterne, string? valeur) => typeExterne switch
    {
        TypeIdentifiantExterne.NumeroBce => NumerosBce.Entreprise(valeur),
        TypeIdentifiantExterne.ReferenceDimona => Texte.Obligatoire(valeur, "La référence DIMONA", 50).ToUpperInvariant(),
        _ => throw new DomainException($"Type d'identifiant externe inconnu : {typeExterne}."),
    };

    /// <summary>Rattache l'identifiant externe à un autre objet interne (ex. numéro BCE repris par un nouvel affilié).</summary>
    public bool Rattacher(Guid identifiantInterne)
    {
        if (identifiantInterne == Guid.Empty)
        {
            throw new DomainException("L'identifiant interne est obligatoire.");
        }

        if (identifiantInterne == IdentifiantInterne)
        {
            return false;
        }

        IdentifiantInterne = identifiantInterne;
        return true;
    }
}
