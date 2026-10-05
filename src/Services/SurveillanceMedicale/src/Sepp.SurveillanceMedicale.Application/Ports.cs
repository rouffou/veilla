using Sepp.SurveillanceMedicale.Domain.Conservation;
using Sepp.SurveillanceMedicale.Domain.Decisions;
using Sepp.SurveillanceMedicale.Domain.Dossiers;
using Sepp.SurveillanceMedicale.Domain.Examens;
using Sepp.SurveillanceMedicale.Domain.MaladiesProfessionnelles;
using Sepp.SurveillanceMedicale.Domain.Projections;
using Sepp.SurveillanceMedicale.Domain.Protocoles;
using Sepp.SurveillanceMedicale.Domain.Transferts;
using Sepp.SurveillanceMedicale.Domain.Vaccinations;

namespace Sepp.SurveillanceMedicale.Application;

/// <summary>Dépôt des dossiers de santé (§14.5 : <c>IDossierSanteRepository</c>).</summary>
public interface IDossierSanteRepository
{
    /// <summary>Dossier avec toutes ses parties (expositions, pièces jointes, questionnaires, vaccinations, tests).</summary>
    Task<DossierSante?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<DossierSante?> GetParPersonneAsync(Guid personneId, CancellationToken cancellationToken);

    /// <summary>Dossiers des personnes rattachées (à une date quelconque) à un groupe d'exposition.</summary>
    Task<IReadOnlyList<DossierSante>> ListerRattachesAuGroupeAsync(Guid groupeExpositionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DossierSante>> ListerParStatutAsync(StatutArchivage statut, CancellationToken cancellationToken);

    void Add(DossierSante dossier);
}

public interface IExamenRepository
{
    Task<Examen?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Examen>> ListerParDossierAsync(Guid dossierId, CancellationToken cancellationToken);

    /// <summary>Relation de soin : le professionnel a un examen (en cours ou clôturé) dans ce dossier.</summary>
    Task<bool> ExisteExamenDuProfessionnelAsync(Guid dossierId, string professionnelId, CancellationToken cancellationToken);

    void Add(Examen examen);
}

public interface IDecisionRepository
{
    Task<Decision?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<Decision?> GetParExamenAsync(Guid examenId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Decision>> ListerParDossierAsync(Guid dossierId, CancellationToken cancellationToken);

    void Add(Decision decision);
}

/// <summary>Protocoles médicaux : valeurs de référence, schémas vaccinaux, questionnaires, modèles de texte, durées de conservation.</summary>
public interface IProtocolesRepository
{
    Task<IReadOnlyList<ValeurReference>> ListerValeursReferenceAsync(CancellationToken cancellationToken);

    Task<ValeurReference?> GetValeurReferenceAsync(Guid id, CancellationToken cancellationToken);

    void Add(ValeurReference valeur);

    Task<IReadOnlyList<SchemaVaccinal>> ListerSchemasVaccinauxAsync(CancellationToken cancellationToken);

    void Add(SchemaVaccinal schema);

    /// <summary>Dernière version d'un modèle de questionnaire.</summary>
    Task<ModeleQuestionnaire?> GetModeleQuestionnaireAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<ModeleQuestionnaire>> ListerModelesQuestionnaireAsync(CancellationToken cancellationToken);

    void Add(ModeleQuestionnaire modele);

    Task<IReadOnlyList<ModeleTexte>> ListerModelesTexteAsync(string cpmtId, CancellationToken cancellationToken);

    Task<ModeleTexte?> GetModeleTexteAsync(Guid id, CancellationToken cancellationToken);

    void Add(ModeleTexte modele);

    void Remove(ModeleTexte modele);

    Task<IReadOnlyList<DureeConservationExposition>> ListerDureesConservationAsync(CancellationToken cancellationToken);

    void Add(DureeConservationExposition duree);
}

public interface ILotVaccinRepository
{
    Task<LotVaccin?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<LotVaccin>> ListerParCentreAsync(Guid centreId, CancellationToken cancellationToken);

    void Add(LotVaccin lot);
}

public interface IDeclarationMpRepository
{
    Task<DeclarationMaladieProfessionnelle?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<DeclarationMaladieProfessionnelle>> ListerParDossierAsync(Guid dossierId, CancellationToken cancellationToken);

    void Add(DeclarationMaladieProfessionnelle declaration);
}

public interface ITransfertRepository
{
    Task<TransfertDossier?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<TransfertDossier>> ListerParDossierAsync(Guid dossierId, CancellationToken cancellationToken);

    void Add(TransfertDossier transfert);
}

/// <summary>
/// NF-22 : purge légale. Seul ce port supprime physiquement des données (DAT-05) : le dossier et tous les agrégats
/// qui le référencent sont détruits et la preuve de destruction enregistrée dans une même transaction, avec les
/// traces et événements en attente dans l'outbox.
/// </summary>
public interface IPurgeDossiers
{
    Task DetruireAsync(DossierSante dossier, PreuveDestruction preuve, CancellationToken cancellationToken);

    Task<IReadOnlyList<PreuveDestruction>> ListerPreuvesAsync(CancellationToken cancellationToken);
}

/// <summary>Modèles de lecture alimentés par les événements des autres services (ARC-31, ARC-35).</summary>
public interface IProjectionRepository
{
    Task<ObligationDue?> GetObligationAsync(Guid obligationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ObligationDue>> ListerObligationsAsync(Guid personneId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ObligationDue>> ListerObligationsParIdsAsync(IReadOnlyCollection<Guid> obligationIds, CancellationToken cancellationToken);

    void Add(ObligationDue obligation);

    Task<RendezVousPrevu?> GetRendezVousAsync(Guid rendezVousId, CancellationToken cancellationToken);

    void Add(RendezVousPrevu rendezVous);

    Task<AffectationPersonne?> GetAffectationAsync(Guid affectationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AffectationPersonne>> ListerAffectationsAsync(Guid personneId, CancellationToken cancellationToken);

    void Add(AffectationPersonne affectation);

    Task<ProfilRisquePoste?> GetProfilAsync(Guid posteId, DateOnly valideDu, CancellationToken cancellationToken);

    /// <summary>Profils des postes (toutes dates d'effet), pour retrouver les risques en vigueur.</summary>
    Task<IReadOnlyList<ProfilRisquePoste>> ListerProfilsAsync(IReadOnlyCollection<Guid> posteIds, CancellationToken cancellationToken);

    void Add(ProfilRisquePoste profil);

    Task<MesurageExposition?> GetMesurageAsync(Guid mesurageId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MesurageExposition>> ListerMesuragesAsync(Guid groupeExpositionId, CancellationToken cancellationToken);

    void Add(MesurageExposition mesurage);

    Task<ParametreLegalLocal?> GetParametreAsync(string code, DateOnly valideDu, CancellationToken cancellationToken);

    Task<ParametreLegalLocal?> ParametreApplicableAsync(string code, DateOnly date, CancellationToken cancellationToken);

    void Add(ParametreLegalLocal parametre);
}

/// <summary>
/// Contexte d'accès de la requête : motif saisi (accès hors relation de soin, §3.3), bris de glace, et identifiants
/// portés par le jeton d'un utilisateur externe (claims <c>affilie_id</c>, <c>personne_id</c>).
/// </summary>
public interface IContexteAcces
{
    string? Motif { get; }

    bool BrisDeGlace { get; }

    Guid? AffilieIdJeton { get; }

    Guid? PersonneIdJeton { get; }
}

// ---- Ports vers l'extérieur (§14.5 « External ») : adaptateurs simulés, réels hors périmètre ----

/// <summary>SAN-21 : import direct des appareils (HL7 v2, fichiers, pilotes). Un adaptateur par format.</summary>
public interface IImportAppareil
{
    /// <summary>Format pris en charge, par ex. <c>HL7</c>, <c>CSV</c>, <c>SIMULATEUR</c>.</summary>
    string Format { get; }

    /// <exception cref="FormatException">Contenu illisible (le message ne reprend jamais de valeur clinique).</exception>
    IReadOnlyList<Mesure> Lire(TypeActe typeActe, string contenu);
}

/// <summary>Demande de signature qualifiée : empreinte du formulaire, jamais son contenu.</summary>
public sealed record DemandeSignature(Guid DecisionId, string SignataireId, string EmpreinteFormulaire);

public sealed record SignatureObtenue(string Reference, DateTimeOffset SigneeLe);

/// <summary>SAN-32 : signature électronique qualifiée du CPMT (eID ou itsme) — prestataire réel hors périmètre.</summary>
public interface ISignatureQualifiee
{
    Task<SignatureObtenue> SignerAsync(DemandeSignature demande, CancellationToken cancellationToken);
}

/// <summary>Déclaration transmise à Fedris (et en copie au médecin-inspecteur social, SAN-70).</summary>
public sealed record DeclarationFedris(Guid DeclarationId, Guid PersonneId, ContenuDeclarationMp Contenu, DateOnly Date);

public sealed record StatutFedris(StatutDeclarationMp Statut, DateOnly Date);

/// <summary>SAN-70, SAN-71 : échanges avec Fedris (format et canal réels à confirmer).</summary>
public interface IFedris
{
    /// <returns>Référence Fedris du dossier déclaré.</returns>
    Task<string> DeclarerAsync(DeclarationFedris declaration, CancellationToken cancellationToken);

    Task<StatutFedris> ConsulterStatutAsync(string referenceFedris, CancellationToken cancellationToken);
}

/// <summary>SAN-42 : canal d'échange sécurisé des dossiers entre SEPP et SIPP (canal réel à confirmer).</summary>
public interface ICanalTransfertDossier
{
    /// <returns>Référence de l'envoi sur le canal.</returns>
    Task<string> TransmettreAsync(string contrepartie, string paquet, string empreinte, CancellationToken cancellationToken);
}
