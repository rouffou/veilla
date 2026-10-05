using Sepp.Affilies.Domain.Affilies;
using Sepp.BuildingBlocks.Domain;

namespace Sepp.Affilies.Application.Affilies;

public sealed record PageDto<T>(IReadOnlyList<T> Elements, int Total, int Page, int Taille);

public sealed record AffilieResumeDto(Guid Id, string NumeroBce, string Denomination, CategorieTarifaire CategorieTarifaire, StatutAffilie Statut, DateOnly DateAffiliation, DateOnly? DateFin);

public sealed record FicheDto(
    string NumeroBce,
    string Denomination,
    string FormeJuridique,
    string CodeNace,
    string CommissionParitaire,
    CategorieTarifaire CategorieTarifaire,
    DateOnly DateAffiliation,
    DateOnly? DateFin,
    Language Langue,
    RegimeLinguistique RegimeLinguistique,
    StatutAffilie Statut,
    Guid? GroupeId);

public sealed record AdresseDto(string Rue, string Numero, string? Boite, string CodePostal, string Localite, string CodePays = "BE")
{
    public Adresse ToAdresse() => new(Rue, Numero, Boite, CodePostal, Localite, CodePays);

    public static AdresseDto From(Adresse a) => new(a.Rue, a.Numero, a.Boite, a.CodePostal, a.Localite, a.CodePays);
}

public sealed record DepartementDto(Guid Id, string Nom, Guid? ParentId, DateOnly ValideDu, DateOnly? ValideJusquAu);

public sealed record SiteDto(Guid Id, string Nom, AdresseDto Adresse, double? Latitude, double? Longitude, DateOnly ValideDu, DateOnly? ValideJusquAu, IReadOnlyList<DepartementDto> Departements);

public sealed record UniteEtablissementDto(Guid Id, string Numero, string Nom, AdresseDto Adresse, Language Langue, DateOnly ValideDu, DateOnly? ValideJusquAu, IReadOnlyList<SiteDto> Sites);

public sealed record ContactDto(Guid Id, string Nom, string? Fonction, RoleContact Role, string? Email, string? Telephone, DateOnly ValideDu, DateOnly? ValideJusquAu);

public sealed record ReunionDto(Guid Id, DateOnly DateReunion, Guid? OrdreDuJourDocumentId, bool ParticipationSepp);

public sealed record OrganeConcertationDto(Guid Id, TypeOrgane Type, DateOnly ValideDu, DateOnly? ValideJusquAu, IReadOnlyList<ReunionDto> Reunions);

public sealed record OperationDto(Guid Id, TypeOperation Type, StatutOperation Statut, DateOnly DateEffet, Guid? AffilieAbsorbantId, IReadOnlyList<Guid> AffiliesBeneficiaires, string? SeppContrepartie, DateOnly? DateRealisation);

/// <summary>Fiche complète d'un affilié ; sert aussi d'instantané pour l'historique (AFF-05).</summary>
public sealed record AffilieDto(
    Guid Id,
    int Version,
    FicheDto Fiche,
    IReadOnlyList<UniteEtablissementDto> UnitesEtablissement,
    IReadOnlyList<ContactDto> Contacts,
    IReadOnlyList<OrganeConcertationDto> OrganesConcertation,
    IReadOnlyList<OperationDto> Operations);

internal static class AffilieMapping
{
    public static AffilieResumeDto ToResume(this Affilie a) =>
        new(a.Id, a.NumeroBce.Formate, a.Denomination, a.CategorieTarifaire, a.Statut, a.DateAffiliation, a.DateFin);

    public static AffilieDto ToDto(this Affilie a) => new(
        a.Id,
        a.NumeroVersion,
        new FicheDto(a.NumeroBce.Formate, a.Denomination, a.FormeJuridique, a.CodeNace, a.CommissionParitaire, a.CategorieTarifaire,
            a.DateAffiliation, a.DateFin, a.Langue, a.RegimeLinguistique, a.Statut, a.GroupeId),
        a.UnitesEtablissement.OrderBy(u => u.Numero.Value, StringComparer.Ordinal).Select(u => new UniteEtablissementDto(
            u.Id, u.Numero.Formate, u.Nom, AdresseDto.From(u.Adresse), u.Langue, u.Validite.ValidFrom, u.Validite.ValidTo,
            u.Sites.OrderBy(s => s.Nom, StringComparer.Ordinal).ThenBy(s => s.Id).Select(s => new SiteDto(
                s.Id, s.Nom, AdresseDto.From(s.Adresse), s.Latitude, s.Longitude, s.Validite.ValidFrom, s.Validite.ValidTo,
                s.Departements.OrderBy(d => d.Id).Select(d => new DepartementDto(d.Id, d.Nom, d.ParentId, d.Validite.ValidFrom, d.Validite.ValidTo)).ToList()))
                .ToList()))
            .ToList(),
        a.Contacts.OrderBy(c => c.Id).Select(c => new ContactDto(c.Id, c.Nom, c.Fonction, c.Role, c.Email, c.Telephone, c.Validite.ValidFrom, c.Validite.ValidTo)).ToList(),
        a.OrganesConcertation.OrderBy(o => o.Id).Select(o => new OrganeConcertationDto(o.Id, o.Type, o.Validite.ValidFrom, o.Validite.ValidTo,
            o.Reunions.OrderBy(r => r.DateReunion).ThenBy(r => r.Id).Select(r => new ReunionDto(r.Id, r.DateReunion, r.OrdreDuJourDocumentId, r.ParticipationSepp)).ToList())).ToList(),
        a.Operations.OrderBy(o => o.Id).Select(o => new OperationDto(o.Id, o.Type, o.Statut, o.DateEffet, o.AffilieAbsorbantId, o.AffiliesBeneficiaires.ToList(), o.SeppContrepartie, o.DateRealisation)).ToList());
}
