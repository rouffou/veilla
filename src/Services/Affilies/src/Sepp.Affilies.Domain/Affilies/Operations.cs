using Sepp.BuildingBlocks.Domain;

namespace Sepp.Affilies.Domain.Affilies;

/// <summary>Opération de restructuration ou de changement de SEPP (AFF-06).</summary>
public enum TypeOperation
{
    /// <summary>L'affilié est absorbé par un autre affilié du SEPP.</summary>
    Fusion,

    /// <summary>L'affilié est scindé en plusieurs affiliés bénéficiaires.</summary>
    Scission,

    /// <summary>L'affilié quitte le SEPP pour un autre SEPP.</summary>
    TransfertSortant,

    /// <summary>L'affilié provient d'un autre SEPP.</summary>
    TransfertEntrant,
}

public enum StatutOperation
{
    Projetee,
    Realisee,
    Annulee,
}

/// <summary>
/// AFF-06 — Fusion, scission et transfert entre SEPP. Une opération est d'abord projetée (date d'effet connue),
/// puis réalisée à partir de sa date d'effet : l'affiliation prend fin la veille et la fiche est figée.
/// Le transfert encadré des dossiers de santé relève du service Surveillance médicale (SAN-42),
/// déclenché par l'événement d'intégration publié par l'application.
/// </summary>
public sealed partial class Affilie
{
    /// <summary>Opération projetée en cours, s'il y en a une.</summary>
    public OperationAffilie? OperationEnCours => _operations.SingleOrDefault(o => o.Statut == StatutOperation.Projetee);

    public OperationAffilie ProjeterFusion(Guid affilieAbsorbantId, DateOnly dateEffet)
    {
        if (affilieAbsorbantId == Guid.Empty || affilieAbsorbantId == Id)
        {
            throw new DomainException("L'affilié absorbant doit être un autre affilié.");
        }

        return Projeter(OperationAffilie.Projeter(NewId(), TypeOperation.Fusion, dateEffet, affilieAbsorbantId, [], null));
    }

    public OperationAffilie ProjeterScission(IReadOnlyCollection<Guid> affiliesBeneficiaires, DateOnly dateEffet)
    {
        var beneficiaires = affiliesBeneficiaires.Distinct().ToArray();
        if (beneficiaires.Length == 0 || beneficiaires.Contains(Guid.Empty))
        {
            throw new DomainException("Une scission désigne au moins un affilié bénéficiaire.");
        }

        if (beneficiaires.Contains(Id))
        {
            throw new DomainException("L'affilié scindé ne peut pas être son propre bénéficiaire.");
        }

        return Projeter(OperationAffilie.Projeter(NewId(), TypeOperation.Scission, dateEffet, null, beneficiaires, null));
    }

    public OperationAffilie ProjeterTransfertSortant(string seppDestination, DateOnly dateEffet) =>
        Projeter(OperationAffilie.Projeter(NewId(), TypeOperation.TransfertSortant, dateEffet, null, [],
            Texte.Obligatoire(seppDestination, "Le SEPP de destination", 200)));

    /// <summary>
    /// Réalise l'opération à partir de sa date d'effet : l'affiliation prend fin la veille
    /// et le statut devient absorbé, scindé ou transféré.
    /// </summary>
    public void RealiserOperation(Guid operationId, DateOnly aujourdhui)
    {
        var operation = OperationProjetee(operationId);
        if (aujourdhui < operation.DateEffet)
        {
            throw new DomainException($"L'opération ne peut être réalisée qu'à partir de sa date d'effet ({operation.DateEffet:yyyy-MM-dd}).");
        }

        operation.Realiser(aujourdhui);
        DateFin = operation.DateEffet.AddDays(-1);
        Statut = operation.Type switch
        {
            TypeOperation.Fusion => StatutAffilie.Absorbe,
            TypeOperation.Scission => StatutAffilie.Scinde,
            _ => StatutAffilie.Transfere,
        };
        IncrementerVersion();
    }

    public void AnnulerOperation(Guid operationId)
    {
        OperationProjetee(operationId).Annuler();
        IncrementerVersion();
    }

    private OperationAffilie Projeter(OperationAffilie operation)
    {
        VerifierModifiable();
        if (Statut != StatutAffilie.Actif)
        {
            throw new DomainException($"Seul un affilié actif peut faire l'objet d'une opération (statut actuel : {Statut}).");
        }

        if (OperationEnCours is not null)
        {
            throw new DomainException("Une opération est déjà projetée pour cet affilié : l'annuler ou la réaliser d'abord.");
        }

        if (operation.DateEffet <= DateAffiliation)
        {
            throw new DomainException($"La date d'effet doit être postérieure à la date d'affiliation ({DateAffiliation:yyyy-MM-dd}).");
        }

        _operations.Add(operation);
        IncrementerVersion();
        return operation;
    }

    private OperationAffilie OperationProjetee(Guid operationId)
    {
        var operation = _operations.SingleOrDefault(o => o.Id == operationId)
                        ?? throw new ElementIntrouvableException($"Opération {operationId} inconnue pour cet affilié.");
        return operation.Statut == StatutOperation.Projetee
            ? operation
            : throw new DomainException($"L'opération est déjà {operation.Statut.ToString().ToLowerInvariant()}.");
    }
}

public sealed class OperationAffilie : Entity
{
    private OperationAffilie()
    {
    }

    private OperationAffilie(Guid id, TypeOperation type, StatutOperation statut, DateOnly dateEffet, Guid? affilieAbsorbantId,
        Guid[] affiliesBeneficiaires, string? seppContrepartie) : base(id)
    {
        Type = type;
        Statut = statut;
        DateEffet = dateEffet;
        AffilieAbsorbantId = affilieAbsorbantId;
        AffiliesBeneficiaires = affiliesBeneficiaires;
        SeppContrepartie = seppContrepartie;
    }

    public TypeOperation Type { get; private set; }

    public StatutOperation Statut { get; private set; }

    /// <summary>Premier jour de la nouvelle situation.</summary>
    public DateOnly DateEffet { get; private set; }

    /// <summary>Fusion : affilié absorbant.</summary>
    public Guid? AffilieAbsorbantId { get; private set; }

    /// <summary>Scission : affiliés bénéficiaires.</summary>
    public IReadOnlyList<Guid> AffiliesBeneficiaires { get; private set; } = [];

    /// <summary>Transfert : SEPP de destination (sortant) ou d'origine (entrant).</summary>
    public string? SeppContrepartie { get; private set; }

    public DateOnly? DateRealisation { get; private set; }

    internal static OperationAffilie Projeter(Guid id, TypeOperation type, DateOnly dateEffet, Guid? absorbant, Guid[] beneficiaires, string? sepp) =>
        new(id, type, StatutOperation.Projetee, dateEffet, absorbant, beneficiaires, sepp);

    internal static OperationAffilie TransfertEntrant(Guid id, string seppOrigine, DateOnly dateAffiliation) =>
        new(id, TypeOperation.TransfertEntrant, StatutOperation.Realisee, dateAffiliation, null, [], seppOrigine)
        {
            DateRealisation = dateAffiliation,
        };

    internal void Realiser(DateOnly date)
    {
        Statut = StatutOperation.Realisee;
        DateRealisation = date;
    }

    internal void Annuler() => Statut = StatutOperation.Annulee;
}
