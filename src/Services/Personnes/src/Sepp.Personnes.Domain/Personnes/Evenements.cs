using Sepp.BuildingBlocks.Domain;

namespace Sepp.Personnes.Domain.Personnes;

public sealed record OccupationEnregistree(Guid PersonneId, Guid OccupationId, Guid AffilieId, DateOnly DateDebut, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OccupationCloturee(Guid PersonneId, Guid OccupationId, Guid AffilieId, DateOnly DateFin, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Création ou clôture d'une affectation (DAT-04) : porte la période telle qu'elle est désormais.</summary>
public sealed record AffectationHistorisee(Guid PersonneId, Guid AffectationId, Guid PosteId, Validity Validite, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Déclaration ou fin d'un état particulier. Le type reste interne au service (ARC-06).</summary>
public sealed record EtatParticulierEnregistre(
    Guid PersonneId,
    Guid EtatParticulierId,
    TypeEtatParticulier Type,
    DateOnly DateDebut,
    DateOnly? DateFin,
    DateTimeOffset OccurredAt) : IDomainEvent;
