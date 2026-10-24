using System.Text.Json;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Documents;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.SurveillanceMedicale.Application.DemandesCopie;
using Sepp.SurveillanceMedicale.Application.Examens;
using Sepp.SurveillanceMedicale.Application.Vaccinations;
using Sepp.SurveillanceMedicale.Domain.DemandesCopie;
using Sepp.SurveillanceMedicale.Domain.Vaccinations;

using Shouldly;

namespace Sepp.SurveillanceMedicale.Application.Tests;

/// <summary>
/// SAN-43 (droit d'accès du travailleur, §3.3 « copie sur demande ») et SAN-50 (carnet de vaccination) : demande sans
/// contenu médical, traitement par le CPMT, transmission au seul service Documents, audit de chaque étape, événements
/// d'identifiants seulement.
/// </summary>
public sealed class CopieDossierTests
{
    private const string Anamnese = "Asthme-Zorglub-ancien";
    private const string RemarqueVaccin = "Malaise-Bidulon après injection";

    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Monde _monde = new();
    private readonly Guid _personne = Guid.CreateVersion7();
    private readonly OptionsSurveillanceMedicale _options = new();

    private AccesDemandesCopie Acces => new(_monde.Store, _monde.Utilisateur, _monde.Contexte, _monde.Horloge);

    private void Travailleur(Guid? personne = null)
    {
        _monde.Agir("travailleur-1", Roles.Travailleur);
        _monde.Contexte.PersonneIdJeton = personne ?? _personne;
    }

    private Task<Result<DemandeCopieDto>> DeposerAsync() =>
        new DeposerDemandeCopieHandler(Acces, _monde.Store, _monde.Store, _monde.Audit, _options, _monde.Store, _monde.Horloge)
            .HandleAsync(new DeposerDemandeCopie(), _ct);

    private Task<Result<DemandeCopieDto>> ValiderAsync(Guid demandeId) =>
        new ValiderDemandeCopieHandler(Acces, _monde.Store, _monde.Garde, _monde.Utilisateur, _monde.Store, _monde.Store, _monde.Horloge)
            .HandleAsync(new ValiderDemandeCopie(demandeId), _ct);

    private Task<Result<CopieDossierDto>> ContenuAsync(Guid demandeId) =>
        new ObtenirContenuCopieHandler(Acces, _monde.Store, _monde.Export, _monde.Audit, _monde.Store).HandleAsync(new ObtenirContenuCopie(demandeId), _ct);

    private async Task<Guid> DossierAvecContenuAsync()
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        var examenId = await _monde.OuvrirExamenAsync(dossierId, Guid.CreateVersion7(), _ct);
        (await new SaisirObservationHandler(_monde.AccesExamens, _monde.Store, _monde.Horloge).HandleAsync(new SaisirObservation(examenId, Anamnese, null), _ct)).IsSuccess.ShouldBeTrue();
        return dossierId;
    }

    [Fact]
    public async Task Le_travailleur_depose_une_demande_sans_contenu_medical_avec_une_date_limite_et_une_trace()
    {
        var dossierId = await DossierAvecContenuAsync();
        Travailleur();

        var demande = await DeposerAsync();

        demande.IsSuccess.ShouldBeTrue();
        demande.Value.Statut.ShouldBe(StatutDemandeCopie.Deposee);
        demande.Value.PersonneId.ShouldBe(_personne);
        demande.Value.DateLimite.ShouldBe(new DateOnly(2026, 3, 17), "15 jours calendrier par défaut (à valider).");
        JsonSerializer.Serialize(demande.Value).ShouldNotContain(Anamnese);
        _monde.Audit.Traces.ShouldContain(t => t.Action == ActionAudit.Creation && t.ObjetType == "dossier-sante.copie" && t.ObjetId == dossierId);
    }

    /// <summary>Contrat avec Documents, qui a un libellé FR, NL et DE pour chacun de ces codes (test miroir côté Documents).</summary>
    [Fact]
    public void Les_codes_de_rubrique_de_la_copie_sont_ceux_que_documents_sait_imprimer() =>
        RubriquesCopie.Connus.ShouldBe(
        [
            "NIVEAU", "FIN", "STATUT", "ANAMNESE", "EXAMEN_CLINIQUE", "RESULTAT", "COMMENTAIRE", "MESURES", "VALIDITE", "JUSTIFICATION",
            "RECOMMANDATIONS", "REPONSE", "DOSE", "REMARQUE", "LECTURE", "INDURATION", "TITRE", "DESCRIPTION", "AGENT", "LOT",
        ]);

    [Fact]
    public async Task Une_seule_demande_en_cours_par_personne()
    {
        await DossierAvecContenuAsync();
        Travailleur();
        (await DeposerAsync()).IsSuccess.ShouldBeTrue();

        (await DeposerAsync()).Error!.Code.ShouldBe("demande-copie.en-cours");
    }

    [Theory]
    [InlineData(Roles.Employeur)]
    [InlineData(Roles.Cpmt)]
    [InlineData(Roles.GestionnaireDossiers)]
    [InlineData(Roles.Documents)]
    public async Task Seul_le_travailleur_depose_une_demande(string role)
    {
        _monde.Agir("x", role);
        _monde.Contexte.PersonneIdJeton = _personne;

        (await DeposerAsync()).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        _monde.Store.DemandesCopie.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_travailleur_sans_claim_personne_est_refuse()
    {
        Travailleur();
        _monde.Contexte.PersonneIdJeton = null;

        (await DeposerAsync()).Error!.Code.ShouldBe("demande-copie.hors-perimetre");
    }

    [Fact]
    public async Task Le_travailleur_ne_suit_que_ses_propres_demandes()
    {
        await DossierAvecContenuAsync();
        Travailleur();
        await DeposerAsync();
        Travailleur(Guid.CreateVersion7());
        await DeposerAsync();

        var miennes = await new ListerMesDemandesCopieHandler(Acces, _monde.Store).HandleAsync(new ListerMesDemandesCopie(), _ct);

        miennes.Value.ShouldHaveSingleItem().PersonneId.ShouldNotBe(_personne);
    }

    [Fact]
    public async Task Le_cpmt_valide_la_copie_avec_trace_et_un_evenement_d_identifiants_seulement()
    {
        var dossierId = await DossierAvecContenuAsync();
        Travailleur();
        var demande = (await DeposerAsync()).Value;
        _monde.Agir("cpmt-1", Roles.Cpmt);

        var validee = await ValiderAsync(demande.Id);

        validee.Value.Statut.ShouldBe(StatutDemandeCopie.Validee);
        _monde.Audit.Traces.ShouldContain(t => t.Action == ActionAudit.Modification && t.ObjetType == "dossier-sante.copie" && t.ObjetId == dossierId);
        var evenement = _monde.Store.Published.OfType<CopieDossierValidee>().ShouldHaveSingleItem();
        evenement.DemandeId.ShouldBe(demande.Id);
        evenement.PersonneId.ShouldBe(_personne);
        JsonSerializer.Serialize(_monde.Store.Published.Cast<object>()).ShouldNotContain(Anamnese);
    }

    [Fact]
    public async Task Un_cpmt_hors_relation_de_soin_doit_motiver_la_validation()
    {
        await DossierAvecContenuAsync();
        Travailleur();
        var demande = (await DeposerAsync()).Value;
        _monde.Agir("cpmt-2", Roles.Cpmt);

        (await ValiderAsync(demande.Id)).Error!.Code.ShouldBe("dossier-sante.motif-obligatoire");

        _monde.Agir("cpmt-2", Roles.Cpmt, motif: "Remplacement du médecin gestionnaire");
        (await ValiderAsync(demande.Id)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(Roles.Infirmier)]
    [InlineData(Roles.Travailleur)]
    [InlineData(Roles.Employeur)]
    [InlineData(Roles.Documents)]
    public async Task Seul_le_cpmt_traite_une_demande(string role)
    {
        await DossierAvecContenuAsync();
        Travailleur();
        var demande = (await DeposerAsync()).Value;
        _monde.Agir("x", role);

        (await ValiderAsync(demande.Id)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new RefuserDemandeCopieHandler(Acces, _monde.Garde, _monde.Utilisateur, _monde.Store, _monde.Horloge)
            .HandleAsync(new RefuserDemandeCopie(demande.Id, MotifRefusCopie.AucunDossier), _ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await new ListerDemandesCopieHandler(Acces, _monde.Store).HandleAsync(new ListerDemandesCopie(null), _ct)).Error!.Kind
            .ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Sans_dossier_le_cpmt_refuse_avec_un_motif_code()
    {
        Travailleur();
        var demande = (await DeposerAsync()).Value;
        _monde.Agir("cpmt-1", Roles.Cpmt);
        (await ValiderAsync(demande.Id)).Error!.Code.ShouldBe("demande-copie.aucun-dossier");

        var refusee = await new RefuserDemandeCopieHandler(Acces, _monde.Garde, _monde.Utilisateur, _monde.Store, _monde.Horloge)
            .HandleAsync(new RefuserDemandeCopie(demande.Id, MotifRefusCopie.AucunDossier), _ct);

        refusee.Value.Statut.ShouldBe(StatutDemandeCopie.Refusee);
        refusee.Value.MotifRefus.ShouldBe(MotifRefusCopie.AucunDossier);
        _monde.Audit.Traces.Count(t => t.ObjetType == "dossier-sante.copie" && t.ObjetId == demande.Id).ShouldBe(2);
    }

    [Fact]
    public async Task Seul_documents_lit_le_contenu_d_une_copie_validee_et_la_lecture_est_tracee()
    {
        var dossierId = await DossierAvecContenuAsync();
        Travailleur();
        var demande = (await DeposerAsync()).Value;

        _monde.Agir("service-documents", Roles.Documents);
        (await ContenuAsync(demande.Id)).Error!.Code.ShouldBe("demande-copie.non-validee");

        _monde.Agir("cpmt-1", Roles.Cpmt);
        (await ContenuAsync(demande.Id)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        await ValiderAsync(demande.Id);

        foreach (var role in new[] { Roles.Travailleur, Roles.Cpmt, Roles.Employeur })
        {
            _monde.Agir("x", role);
            (await ContenuAsync(demande.Id)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        }

        _monde.Agir("service-documents", Roles.Documents);
        var contenu = await ContenuAsync(demande.Id);

        contenu.IsSuccess.ShouldBeTrue();
        contenu.Value.EvaluationsSante.ShouldHaveSingleItem().Rubriques.ShouldContain(r => r.Code == RubriquesCopie.Anamnese && r.Valeur == Anamnese);
        _monde.Audit.Traces.Last().ShouldBe(new Trace(ActionAudit.Export, "dossier-sante.copie", dossierId, AccesDemandesCopie.MotifTransmissionCopie, false, false));
    }

    [Fact]
    public async Task La_publication_par_documents_passe_la_demande_au_statut_publiee_une_seule_fois()
    {
        await DossierAvecContenuAsync();
        Travailleur();
        var demande = (await DeposerAsync()).Value;
        _monde.Agir("cpmt-1", Roles.Cpmt);
        await ValiderAsync(demande.Id);
        var documentId = Guid.CreateVersion7();
        var handler = new DocumentPublieHandler(_monde.Store, _monde.Audit, _monde.Store);

        await handler.HandleAsync(new DocumentPublie(Guid.CreateVersion7(), "medicale", "personne", _personne, "SANTE.EVALUATION.TRAVAILLEUR", "decision", Guid.CreateVersion7()), _ct);
        await handler.HandleAsync(new DocumentPublie(documentId, "medicale", "personne", _personne, "SANTE.COPIE_DOSSIER", DocumentPublieHandler.ObjetCopie, demande.Id), _ct);
        var traces = _monde.Audit.Traces.Count;
        await handler.HandleAsync(new DocumentPublie(documentId, "medicale", "personne", _personne, "SANTE.COPIE_DOSSIER", DocumentPublieHandler.ObjetCopie, demande.Id), _ct);

        var suivie = _monde.Store.DemandesCopie.Single();
        suivie.Statut.ShouldBe(StatutDemandeCopie.Publiee);
        suivie.DocumentId.ShouldBe(documentId);
        _monde.Audit.Traces.Count.ShouldBe(traces);
    }

    [Fact]
    public async Task Le_carnet_de_vaccination_est_transmis_a_documents_sans_remarque_et_trace()
    {
        var dossierId = await _monde.OuvrirDossierAsync(_personne, _ct);
        _monde.Store.Schemas.Add(SchemaVaccinal.Definir("HEPATITE_B", new LocalizedLabel("Hépatite B", "Hepatitis B", "Hepatitis B"), ["EX.BIO"], 3, [1, 5], null));
        _monde.Agir("cpmt-1", Roles.Cpmt);
        var vaccination = await new AdministrerVaccinHandler(_monde.Garde, _monde.Store, _monde.Utilisateur, _monde.Store, _monde.Store, _monde.Horloge)
            .HandleAsync(new AdministrerVaccin(dossierId, "HEPATITE_B", 1, null, null, RemarqueVaccin), _ct);
        var handler = () => new ObtenirCarnetVaccinationHandler(Acces, _monde.Store, _monde.Store, _monde.Store, new RisquesPersonne(_monde.Store), _monde.Audit, _monde.Store, _monde.Horloge)
            .HandleAsync(new ObtenirCarnetVaccination(vaccination.Value), _ct);

        (await handler()).Error!.Kind.ShouldBe(ErrorKind.Forbidden, "Même le CPMT passe par le dossier, pas par ce canal.");

        _monde.Agir("service-documents", Roles.Documents);
        var carnet = await handler();

        var dose = carnet.Value.Doses.ShouldHaveSingleItem();
        dose.VaccinCode.ShouldBe("HEPATITE_B");
        dose.Libelle!.Fr.ShouldBe("Hépatite B");
        carnet.Value.Rappels.ShouldHaveSingleItem().DoseSuivante.ShouldBe(2);
        JsonSerializer.Serialize(carnet.Value).ShouldNotContain(RemarqueVaccin);
        _monde.Audit.Traces.Last().ShouldBe(new Trace(ActionAudit.Export, "dossier-sante.vaccinations", dossierId, AccesDemandesCopie.MotifTransmissionCarnet, false, false));
    }
}
