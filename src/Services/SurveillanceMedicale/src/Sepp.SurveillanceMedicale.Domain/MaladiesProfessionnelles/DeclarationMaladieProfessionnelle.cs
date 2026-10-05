using Sepp.BuildingBlocks.Domain;

namespace Sepp.SurveillanceMedicale.Domain.MaladiesProfessionnelles;

public enum StatutDeclarationMp
{
    Brouillon,
    Envoyee,
    InformationDemandee,
    Reconnue,
    Refusee,
    Cloturee,
}

/// <summary>Exposition reprise du dossier dans la déclaration (agent, niveau, période).</summary>
public sealed record ExpositionDeclaree(string Agent, string Niveau, DateOnly Debut, DateOnly? Fin);

/// <summary>
/// Contenu de la déclaration à Fedris et au médecin-inspecteur (SAN-70) : maladie (code de la liste belge), description
/// clinique et données reprises du dossier. Chiffré en base (ARC-45) ; ne circule que vers Fedris par le port dédié.
/// </summary>
public sealed record ContenuDeclarationMp(
    string CodeMaladie,
    string Description,
    string? AgentCausal,
    IReadOnlyList<ExpositionDeclaree> Expositions,
    IReadOnlyList<Guid> AffiliesEmployeurs,
    string? DerniereDecision);

/// <summary>SAN-71 : demande d'information de Fedris et réponse du SEPP (objet et réponse chiffrés).</summary>
public sealed class DemandeInformationFedris : Entity
{
    private DemandeInformationFedris()
    {
    }

    internal DemandeInformationFedris(Guid id, DateOnly dateDemande, DateOnly? echeance, string objet) : base(id)
    {
        DateDemande = dateDemande;
        Echeance = echeance;
        Objet = Garde.Requis(objet, "objet", 4_000);
    }

    public DateOnly DateDemande { get; private set; }

    public DateOnly? Echeance { get; private set; }

    public string Objet { get; private set; } = string.Empty;

    public DateOnly? DateReponse { get; private set; }

    public string? Reponse { get; private set; }

    internal void Repondre(DateOnly date, string reponse)
    {
        if (DateReponse is not null)
        {
            throw new DomainException("Cette demande a déjà reçu une réponse.");
        }

        DateReponse = date;
        Reponse = Garde.Requis(reponse, "réponse", Garde.TexteCliniqueMaximum);
    }
}

/// <summary>
/// SAN-70, SAN-71 : déclaration de maladie professionnelle (§15.3 <c>declaration_mp</c>), préremplie depuis le dossier
/// de santé, envoyée à Fedris (port <c>IFedris</c>) et suivie jusqu'à la décision de Fedris.
/// </summary>
public sealed class DeclarationMaladieProfessionnelle : AggregateRoot
{
    private readonly List<DemandeInformationFedris> _demandesInformation = [];

    private DeclarationMaladieProfessionnelle()
    {
    }

    private DeclarationMaladieProfessionnelle(Guid id, Guid dossierId, Guid personneId, DateOnly date, string auteurId, ContenuDeclarationMp contenu) : base(id)
    {
        DossierId = Garde.Identifiant(dossierId, "dossier");
        PersonneId = personneId;
        Date = date;
        AuteurId = Garde.Requis(auteurId, "auteur", 100);
        Contenu = Valider(contenu);
        Statut = StatutDeclarationMp.Brouillon;
    }

    public Guid DossierId { get; private set; }

    public Guid PersonneId { get; private set; }

    public DateOnly Date { get; private set; }

    public string AuteurId { get; private set; } = string.Empty;

    public StatutDeclarationMp Statut { get; private set; }

    /// <summary>Chiffré (ARC-45).</summary>
    public ContenuDeclarationMp Contenu { get; private set; } = null!;

    public string? ReferenceFedris { get; private set; }

    public DateOnly? DateEnvoi { get; private set; }

    public DateOnly? DateIssue { get; private set; }

    public IReadOnlyList<DemandeInformationFedris> DemandesInformation => _demandesInformation.AsReadOnly();

    public static DeclarationMaladieProfessionnelle Preparer(Guid dossierId, Guid personneId, DateOnly date, string auteurId, ContenuDeclarationMp contenu) =>
        new(NewId(), dossierId, personneId, date, auteurId, contenu);

    public void Completer(ContenuDeclarationMp contenu)
    {
        if (Statut != StatutDeclarationMp.Brouillon)
        {
            throw new DomainException("Une déclaration envoyée ne se modifie plus.");
        }

        Contenu = Valider(contenu);
    }

    public void MarquerEnvoyee(string referenceFedris, DateOnly date)
    {
        if (Statut != StatutDeclarationMp.Brouillon)
        {
            throw new DomainException("Cette déclaration a déjà été envoyée.");
        }

        ReferenceFedris = Garde.Requis(referenceFedris, "référence Fedris", 100);
        DateEnvoi = date;
        Statut = StatutDeclarationMp.Envoyee;
    }

    public DemandeInformationFedris EnregistrerDemandeInformation(DateOnly dateDemande, DateOnly? echeance, string objet)
    {
        VerifierEnvoyee();
        var demande = new DemandeInformationFedris(NewId(), dateDemande, echeance, objet);
        _demandesInformation.Add(demande);
        Statut = StatutDeclarationMp.InformationDemandee;
        return demande;
    }

    public void RepondreDemande(Guid demandeId, DateOnly date, string reponse)
    {
        var demande = _demandesInformation.Find(d => d.Id == demandeId) ?? throw new DomainException("Demande d'information inconnue.");
        demande.Repondre(date, reponse);
        if (_demandesInformation.TrueForAll(d => d.DateReponse is not null) && Statut == StatutDeclarationMp.InformationDemandee)
        {
            Statut = StatutDeclarationMp.Envoyee;
        }
    }

    /// <summary>SAN-71 : statut communiqué par Fedris (reconnaissance, refus, clôture).</summary>
    public void AppliquerStatutFedris(StatutDeclarationMp statut, DateOnly date)
    {
        VerifierEnvoyee();
        if (statut is StatutDeclarationMp.Brouillon)
        {
            throw new DomainException("Statut Fedris invalide.");
        }

        if (statut == Statut)
        {
            return;
        }

        Statut = statut;
        if (statut is StatutDeclarationMp.Reconnue or StatutDeclarationMp.Refusee or StatutDeclarationMp.Cloturee)
        {
            DateIssue = date;
        }
    }

    private void VerifierEnvoyee()
    {
        if (ReferenceFedris is null)
        {
            throw new DomainException("La déclaration n'a pas encore été envoyée à Fedris.");
        }
    }

    private static ContenuDeclarationMp Valider(ContenuDeclarationMp contenu) => contenu with
    {
        CodeMaladie = Garde.Code(contenu.CodeMaladie, "code de maladie"),
        Description = Garde.Requis(contenu.Description, "description", Garde.TexteCliniqueMaximum),
        AgentCausal = Garde.Facultatif(contenu.AgentCausal, "agent causal", 200),
    };
}
