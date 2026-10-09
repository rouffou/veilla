using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts;
using Sepp.Contracts.Personnes;
using Sepp.Personnes.Domain.Personnes;

namespace Sepp.Personnes.Application;

public sealed record AdresseDto(string Rue, string Numero, string? Boite, string CodePostal, string Localite, string Pays = "BE")
{
    public Adresse ToAdresse() => new(Rue, Numero, Boite, CodePostal, Localite, Pays);

    public static AdresseDto? From(Adresse? adresse) =>
        adresse is null ? null : new(adresse.Rue, adresse.Numero, adresse.Boite, adresse.CodePostal, adresse.Localite, adresse.Pays);
}

public sealed record IdentiteDto(string Nom, string Prenom, DateOnly DateNaissance, Sexe Sexe, Language Langue)
{
    public Identite ToIdentite() => new(Nom, Prenom, DateNaissance, Sexe, Langue);
}

public sealed record CoordonneesDto(AdresseDto? Adresse, string? Email, string? Telephone, CanalCommunication CanalPrefere)
{
    public static readonly CoordonneesDto Aucune = new(null, null, null, CanalCommunication.Portail);

    public Coordonnees ToCoordonnees() => new(Adresse?.ToAdresse(), Email, Telephone, CanalPrefere);
}

public sealed record NouvelleOccupationDto(
    Guid AffilieId,
    Guid? AffilieUtilisateurId,
    TypeTravailleur TypeTravailleur,
    TypeContrat TypeContrat,
    DateOnly DateDebut,
    DateOnly? DateFin)
{
    public NouvelleOccupation ToOccupation(string? referenceDimona = null) =>
        new(AffilieId, AffilieUtilisateurId, TypeTravailleur, TypeContrat, DateDebut, DateFin, referenceDimona);
}

/// <summary>Résumé d'une personne : jamais le NISS en clair, seulement sa forme masquée (DAT-06).</summary>
public sealed record PersonneResumeDto(Guid Id, string NissMasque, string Nom, string Prenom, DateOnly DateNaissance);

public sealed record PersonneDto(
    Guid Id,
    string NissMasque,
    string Nom,
    string Prenom,
    DateOnly DateNaissance,
    Sexe Sexe,
    Language Langue,
    AdresseDto? Adresse,
    string? Email,
    string? Telephone,
    CanalCommunication CanalPrefere,
    IReadOnlyList<OccupationDto> Occupations);

public sealed record OccupationDto(
    Guid Id,
    Guid AffilieId,
    Guid? AffilieUtilisateurId,
    TypeTravailleur TypeTravailleur,
    TypeContrat TypeContrat,
    DateOnly DateDebut,
    DateOnly? DateFin,
    string? ReferenceDimona,
    IReadOnlyList<AffectationDto> Affectations);

/// <summary>Affectation historisée (DAT-04) : <c>ValideJusquAu</c> est la borne exclusive.</summary>
public sealed record AffectationDto(Guid Id, Guid OccupationId, Guid PosteId, Guid SiteId, DateOnly ValideDu, DateOnly? ValideJusquAu);

public sealed record EtatParticulierDto(Guid Id, TypeEtatParticulier Type, DateOnly DateDebut, DateOnly? DateFin);

internal static class Projections
{
    public static PersonneResumeDto Resume(this Personne p) => new(p.Id, p.Niss.Masque(), p.Nom, p.Prenom, p.DateNaissance);

    public static PersonneDto Fiche(this Personne p, IEnumerable<Occupation> occupations) => new(
        p.Id, p.Niss.Masque(), p.Nom, p.Prenom, p.DateNaissance, p.Sexe, p.Langue, AdresseDto.From(p.Adresse), p.Email, p.Telephone,
        p.CanalPrefere, occupations.Select(Dto).ToList());

    public static OccupationDto Dto(this Occupation o) => new(
        o.Id, o.AffilieId, o.AffilieUtilisateurId, o.TypeTravailleur, o.TypeContrat, o.DateDebut, o.DateFin, o.ReferenceDimona,
        o.Affectations.OrderBy(a => a.Validite.ValidFrom).Select(a => a.Dto(o.Id)).ToList());

    public static AffectationDto Dto(this Affectation a, Guid occupationId) =>
        new(a.Id, occupationId, a.PosteId, a.SiteId, a.Validite.ValidFrom, a.Validite.ValidTo);
}

/// <summary>
/// Périmètre de l'utilisateur (ADR 0005) : les internes voient tous les travailleurs ; l'employeur et le SIPP
/// sont limités à leur affilié (claim <c>affilie_id</c>), y compris comme entreprise utilisatrice d'un intérimaire (AFF-23).
/// </summary>
public sealed class PerimetreUtilisateur(ICurrentUser user, IContexteAffilie contexte)
{
    public bool EstExterne => user.Roles.Contains(Roles.Employeur) || user.Roles.Contains(Roles.Sipp);

    /// <summary>Affilié de l'utilisateur externe ; <c>null</c> pour un interne.</summary>
    public Guid? AffilieExterne => EstExterne ? contexte.AffilieId : null;

    public bool PeutAgirPour(Guid affilieId) => !EstExterne || contexte.AffilieId == affilieId;

    public bool PeutAgirPour(Occupation occupation) =>
        PeutAgirPour(occupation.AffilieId) || (occupation.AffilieUtilisateurId is { } utilisateur && PeutAgirPour(utilisateur));

    public bool PeutVoir(Personne personne) =>
        !EstExterne || (contexte.AffilieId is { } affilie && personne.EstRattacheA(affilie));

    /// <summary>Un externe ne voit que les occupations de son affilié, pas les autres emplois du travailleur.</summary>
    public IEnumerable<Occupation> OccupationsVisibles(Personne personne) =>
        personne.Occupations.Where(PeutAgirPour).OrderBy(o => o.DateDebut);
}

/// <summary>Chargement d'une personne avec contrôle de permission et de périmètre (ARC-41).</summary>
public sealed class AccesPersonnes(IPersonneRepository repository, ICurrentUser user, PerimetreUtilisateur perimetre)
{
    public async Task<Result<Personne>> ChargerAsync(Guid personneId, string permission, CancellationToken cancellationToken)
    {
        if (!user.HasPermission(permission))
        {
            return Error.Forbidden("personnes.interdit", "Droits insuffisants sur les données des travailleurs.");
        }

        var personne = await repository.GetAsync(personneId, cancellationToken);

        // Hors périmètre : même réponse qu'une personne inexistante, pour ne pas révéler son existence.
        if (personne is null || !perimetre.PeutVoir(personne))
        {
            return Error.NotFound("personne.inconnue", "Personne inconnue.");
        }

        return personne;
    }
}

/// <summary>Traduction des événements de domaine en événements d'intégration (ARC-06 : identifiants, dates et catégories).</summary>
public static class EvenementsIntegration
{
    public const string ProtectionMaternite = "PROTECTION_MATERNITE";
    public const string TravailDeNuit = "TRAVAIL_DE_NUIT";
    public const string JeuneTravailleur = "JEUNE_TRAVAILLEUR";

    /// <summary>Catégorie générique publiée : grossesse et allaitement sont confondus (AFF-24, ARC-06).</summary>
    public static string Categorie(TypeEtatParticulier type) => type switch
    {
        TypeEtatParticulier.Grossesse or TypeEtatParticulier.Allaitement => ProtectionMaternite,
        TypeEtatParticulier.TravailDeNuit => TravailDeNuit,
        TypeEtatParticulier.Jeune => JeuneTravailleur,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static void Publier(Personne personne, IIntegrationEventOutbox outbox)
    {
        foreach (var evenement in personne.DomainEvents)
        {
            IntegrationEvent? integration = evenement switch
            {
                OccupationEnregistree e => new OccupationDebutee(e.OccupationId, e.PersonneId, e.AffilieId, e.DateDebut),
                OccupationCloturee e => new OccupationTerminee(e.OccupationId, e.PersonneId, e.AffilieId, e.DateFin),
                AffectationHistorisee e => new AffectationModifiee(e.AffectationId, e.PersonneId, e.PosteId, e.Validite.ValidFrom, e.Validite.ValidTo),
                EtatParticulierEnregistre e => new EtatParticulierDeclare(e.EtatParticulierId, e.PersonneId, Categorie(e.Type), e.DateDebut, e.DateFin),
                _ => null,
            };

            if (integration is not null)
            {
                outbox.Add(integration);
            }
        }

        personne.ClearDomainEvents();
    }
}

internal static class Regles
{
    /// <summary>Exécute une règle métier et traduit sa violation en erreur de validation.</summary>
    public static Result<T> Appliquer<T>(string code, Func<T> regle)
    {
        try
        {
            return regle();
        }
        catch (OccupationApresDecesException ex)
        {
            return Error.Validation(OccupationApresDecesException.Code, ex.Message);
        }
        catch (DomainException ex)
        {
            return Error.Validation(code, ex.Message);
        }
    }
}
