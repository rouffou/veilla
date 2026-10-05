using Sepp.BuildingBlocks.Domain;

namespace Sepp.PostesRisques.Domain.Listes;

/// <summary>Modification demandée sur une liste : inscrire ou retirer un travailleur pour un poste.</summary>
public sealed record DemandeLigneListe(TypeModification Type, Guid PersonneId, Guid PosteId);

/// <summary>
/// AFF-31 : proposition de modification d'une liste nominative, typiquement par l'employeur via le portail,
/// validée ou refusée par le CPMT. La validation produit une nouvelle version de la liste ; la proposition
/// et la version d'origine restent dans l'historique.
/// </summary>
public sealed class PropositionListeNominative : AggregateRoot
{
    private readonly List<LignePropositionListe> _lignes = [];

    private PropositionListeNominative()
    {
    }

    private PropositionListeNominative(
        Guid id, ListeNominative liste, OrigineProposition origine, string proposeePar, DateTimeOffset dateProposition, string motif)
        : base(id)
    {
        ListeNominativeId = liste.Id;
        AffilieId = liste.AffilieId;
        TypeListe = liste.Type;
        VersionListe = liste.Version;
        Origine = origine;
        ProposeePar = proposeePar;
        DateProposition = dateProposition;
        Motif = motif;
        Statut = StatutProposition.Soumise;
    }

    /// <summary>Version de liste sur laquelle porte la proposition.</summary>
    public Guid ListeNominativeId { get; private set; }

    public Guid AffilieId { get; private set; }

    public TypeListeNominative TypeListe { get; private set; }

    public int VersionListe { get; private set; }

    public OrigineProposition Origine { get; private set; }

    public string ProposeePar { get; private set; } = string.Empty;

    public DateTimeOffset DateProposition { get; private set; }

    public string Motif { get; private set; } = string.Empty;

    public StatutProposition Statut { get; private set; }

    public IReadOnlyList<LignePropositionListe> Lignes => _lignes.AsReadOnly();

    public string? DecidePar { get; private set; }

    public DateTimeOffset? DateDecision { get; private set; }

    public string? MotifRefus { get; private set; }

    /// <summary>Nouvelle version de la liste produite par la validation.</summary>
    public Guid? ListeResultanteId { get; private set; }

    public static PropositionListeNominative Soumettre(
        ListeNominative liste,
        OrigineProposition origine,
        string proposeePar,
        DateTimeOffset maintenant,
        string motif,
        IReadOnlyCollection<DemandeLigneListe> demandes)
    {
        if (demandes.Count == 0)
        {
            throw new DomainException("Une proposition doit contenir au moins une modification.");
        }

        if (demandes.GroupBy(d => (d.PersonneId, d.PosteId)).Any(g => g.Count() > 1))
        {
            throw new DomainException("Un même travailleur ne peut figurer qu'une fois par poste dans une proposition.");
        }

        var proposition = new PropositionListeNominative(
            NewId(), liste, origine, Saisie.Obligatoire(proposeePar, "L'auteur", 100), maintenant, Saisie.Obligatoire(motif, "Le motif", 2000));
        foreach (var demande in demandes)
        {
            switch (demande.Type)
            {
                case TypeModification.Modification:
                    throw new DomainException("Une liste nominative se modifie par ajout ou retrait d'un travailleur.");
                case TypeModification.Retrait when !liste.Contient(demande.PersonneId, demande.PosteId):
                    throw new DomainException("Le travailleur à retirer ne figure pas sur la liste pour ce poste.");
                case TypeModification.Ajout when liste.Contient(demande.PersonneId, demande.PosteId):
                    throw new DomainException("Le travailleur à ajouter figure déjà sur la liste pour ce poste.");
            }

            proposition._lignes.Add(new LignePropositionListe(
                NewId(), demande.Type, Saisie.Identifiant(demande.PersonneId, "Le travailleur"), Saisie.Identifiant(demande.PosteId, "Le poste")));
        }

        proposition.Raise(new PropositionListeModifiee(proposition.Id, proposition.Statut, DateTimeOffset.UtcNow));
        return proposition;
    }

    /// <summary>
    /// Lignes de la nouvelle version : lignes de la liste d'origine moins les retraits, plus les ajouts
    /// (dont les risques et la dernière évaluation sont fournis par l'appelant).
    /// </summary>
    public IReadOnlyList<LigneCalculee> AppliquerA(ListeNominative liste, Func<LignePropositionListe, LigneCalculee> ajout)
    {
        if (liste.Id != ListeNominativeId)
        {
            throw new DomainException("La proposition ne porte pas sur cette version de liste.");
        }

        var retraits = _lignes.Where(l => l.Type == TypeModification.Retrait).Select(l => (l.PersonneId, l.PosteId)).ToHashSet();
        return liste.Lignes
            .Where(l => !retraits.Contains((l.PersonneId, l.PosteId)))
            .Select(l => l.VersLigneCalculee())
            .Concat(_lignes.Where(l => l.Type == TypeModification.Ajout).Select(ajout))
            .ToList();
    }

    public void Valider(string cpmtId, DateTimeOffset maintenant, ListeNominative nouvelleVersion)
    {
        ExigerSoumise();
        if (nouvelleVersion.PropositionId != Id)
        {
            throw new DomainException("La nouvelle version de la liste doit être issue de cette proposition.");
        }

        DecidePar = Saisie.Obligatoire(cpmtId, "Le CPMT", 100);
        DateDecision = maintenant;
        ListeResultanteId = nouvelleVersion.Id;
        Statut = StatutProposition.Validee;
        Raise(new PropositionListeModifiee(Id, Statut, DateTimeOffset.UtcNow));
    }

    public void Refuser(string cpmtId, DateTimeOffset maintenant, string motif)
    {
        ExigerSoumise();
        DecidePar = Saisie.Obligatoire(cpmtId, "Le CPMT", 100);
        DateDecision = maintenant;
        MotifRefus = Saisie.Obligatoire(motif, "Le motif du refus", 2000);
        Statut = StatutProposition.Refusee;
        Raise(new PropositionListeModifiee(Id, Statut, DateTimeOffset.UtcNow));
    }

    private void ExigerSoumise()
    {
        if (Statut != StatutProposition.Soumise)
        {
            throw new DomainException($"La proposition a déjà été traitée ({Statut}).");
        }
    }
}

public sealed class LignePropositionListe : Entity
{
    private LignePropositionListe()
    {
    }

    internal LignePropositionListe(Guid id, TypeModification type, Guid personneId, Guid posteId) : base(id)
    {
        Type = type;
        PersonneId = personneId;
        PosteId = posteId;
    }

    public TypeModification Type { get; private set; }

    public Guid PersonneId { get; private set; }

    public Guid PosteId { get; private set; }
}

public sealed record PropositionListeModifiee(Guid PropositionId, StatutProposition Statut, DateTimeOffset OccurredAt) : IDomainEvent;
