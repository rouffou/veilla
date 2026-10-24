using System.Text.Json;

using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Documents;
using Sepp.Contracts.SurveillanceMedicale;
using Sepp.Documents.Application.DossierSante;
using Sepp.Documents.Domain.Commun;
using Sepp.Documents.Domain.Documents;
using Sepp.Documents.Domain.Modeles;

using Shouldly;

namespace Sepp.Documents.Application.Tests;

internal sealed class FakeSourceDossierSante : ISourceDossierSante
{
    public Dictionary<Guid, CopieDossierMedicale> Copies { get; } = [];

    public Dictionary<Guid, CarnetVaccination> Carnets { get; } = [];

    public Task<CopieDossierMedicale> ObtenirCopieAsync(Guid demandeId, CancellationToken cancellationToken) =>
        Copies.TryGetValue(demandeId, out var copie) ? Task.FromResult(copie) : throw new InvalidOperationException("Copie non validée (HTTP 409).");

    public Task<CarnetVaccination?> ObtenirCarnetAsync(Guid vaccinationId, CancellationToken cancellationToken) =>
        Task.FromResult(Carnets.GetValueOrDefault(vaccinationId));
}

/// <summary>
/// POR-13 : copie du dossier (SAN-43) et carnet de vaccination (SAN-50) générés en zone médicale et publiés au travailleur ;
/// le contenu vient de la Surveillance médicale, jamais de l'événement ni du portail.
/// </summary>
public sealed class DossierSanteTravailleurTests
{
    private const string Anamnese = "Asthme-Zorglub-ancien";

    private static readonly Guid Personne = Guid.CreateVersion7();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Contexte _ctx = new(Roles.Documents);
    private readonly FakeSourceDossierSante _source = new();

    private CopieDossierValideeHandler Copie() => new(_source, _ctx.Generateur, _ctx.Store, _ctx.Store, _ctx.Store);

    private VaccinationAdministreeHandler Carnet() => new(_source, _ctx.Generateur, _ctx.Store, _ctx.Store, _ctx.Store);

    private void PublierModeles()
    {
        foreach (var d in ModelesDossierSante.Definitions)
        {
            var modele = Modele.Creer(d.Code, d.Langue, TypeModele.Formulaire, ZoneDocument.Medicale, d.Libelle, ModelesDossierSante.Avertissement, d.Contenu, d.Champs);
            modele.Valider("cpmt-dirigeant", _ctx.Horloge.GetUtcNow());
            modele.Publier("admin", _ctx.Horloge.GetUtcNow());
            _ctx.Store.Add(modele);
        }
    }

    private string Texte(Document document) =>
        FakeRendu.Texte(_ctx.Chiffrement.Dechiffrer(document.Zone, document.Id, _ctx.Stockage.Objets[document.StockageUri]));

    private Guid CopieValidee(int examens = 1)
    {
        var demande = Guid.CreateVersion7();
        _source.Copies[demande] = new CopieDossierMedicale(
            demande, Personne, new DateOnly(2020, 1, 15), new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero),
            [],
            [.. Enumerable.Range(0, examens).Select(_ => new ElementCopie(new DateOnly(2026, 3, 2), "EVALUATION_PERIODIQUE", [new RubriqueCopie("ANAMNESE", Anamnese)]))],
            [new ElementCopie(new DateOnly(2026, 3, 2), "APTE_AVEC_MESURES", [new RubriqueCopie("MESURES", "PAS_DE_CHARGES")])],
            [], [], [], [], []);
        return demande;
    }

    [Fact]
    public async Task La_copie_validee_est_generee_en_zone_medicale_et_publiee_au_travailleur()
    {
        PublierModeles();
        var demande = CopieValidee();

        await Copie().HandleAsync(new CopieDossierValidee(demande, Personne), _ct);

        var document = _ctx.Store.Documents.ShouldHaveSingleItem();
        document.Zone.ShouldBe(ZoneDocument.Medicale);
        document.TypeDestinataire.ShouldBe(TypeDestinataire.Personne);
        document.DestinataireId.ShouldBe(Personne);
        document.Statut.ShouldBe(StatutDocument.Publie);
        var texte = Texte(document);
        texte.ShouldContain(Anamnese);
        texte.ShouldContain("Anamnèse");
        texte.ShouldContain("Apte avec mesures");
        texte.ShouldNotContain("APTE_AVEC_MESURES");

        var publie = _ctx.Store.Published.OfType<DocumentPublie>().ShouldHaveSingleItem();
        publie.ObjetType.ShouldBe(ModelesDossierSante.ObjetCopie);
        publie.ObjetId.ShouldBe(demande);
        JsonSerializer.Serialize(_ctx.Store.Published.Cast<object>()).ShouldNotContain(Anamnese);
        _ctx.Store.Acces.ShouldContain((ActionAudit.Creation, ZoneDocument.Medicale, document.Id));
        _ctx.Store.Acces.ShouldContain((ActionAudit.Modification, ZoneDocument.Medicale, document.Id));
    }

    [Fact]
    public async Task Rejouee_la_copie_n_est_ni_regeneree_ni_republiee()
    {
        PublierModeles();
        var demande = CopieValidee();

        await Copie().HandleAsync(new CopieDossierValidee(demande, Personne), _ct);
        await Copie().HandleAsync(new CopieDossierValidee(demande, Personne), _ct);

        _ctx.Store.Documents.Count.ShouldBe(1);
        _ctx.Store.Published.OfType<DocumentPublie>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Sans_modele_valide_par_le_cpmt_dirigeant_l_evenement_echoue_pour_etre_rejoue()
    {
        await new EvaluationSante.InitialiserModelesParDefaut(_ctx.Store, _ctx.Store).ExecuteAsync(_ct);
        _ctx.Store.Modeles.Where(m => m.Code is ModelesDossierSante.CodeCopie or ModelesDossierSante.CodeCarnet)
            .ShouldAllBe(m => m.Zone == ZoneDocument.Medicale && m.Statut == StatutModele.Brouillon);

        await Should.ThrowAsync<InvalidOperationException>(() => Copie().HandleAsync(new CopieDossierValidee(CopieValidee(), Personne), _ct));

        _ctx.Store.Documents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_copie_non_validee_ou_d_une_autre_personne_n_est_pas_publiee()
    {
        PublierModeles();

        await Should.ThrowAsync<InvalidOperationException>(() => Copie().HandleAsync(new CopieDossierValidee(Guid.CreateVersion7(), Personne), _ct));
        await Should.ThrowAsync<InvalidOperationException>(() => Copie().HandleAsync(new CopieDossierValidee(CopieValidee(), Guid.CreateVersion7()), _ct));

        _ctx.Store.Documents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_partie_trop_longue_est_bornee_et_signalee_comme_copie_partielle()
    {
        PublierModeles();

        await Copie().HandleAsync(new CopieDossierValidee(CopieValidee(examens: 80), Personne), _ct);

        Texte(_ctx.Store.Documents.Single()).ShouldContain("le médecin du travail vous en remet le complément");
    }

    [Fact]
    public async Task Le_carnet_est_publie_au_travailleur_apres_chaque_vaccination()
    {
        PublierModeles();
        var vaccination = Guid.CreateVersion7();
        _source.Carnets[vaccination] = new CarnetVaccination(vaccination, Personne, new DateOnly(2026, 10, 5),
            [new DoseCarnet(new DateOnly(2026, 10, 5), "HEPATITE_B", new LibelleVaccin("Hépatite B", "Hepatitis B", "Hepatitis B", null), 1, "LOT42")],
            [],
            [new RappelCarnet("HEPATITE_B", null, 2, new DateOnly(2026, 11, 5), false)]);

        await Carnet().HandleAsync(new VaccinationAdministree(vaccination, Personne, "HEPATITE_B", 1, new DateOnly(2026, 10, 5)), _ct);

        var document = _ctx.Store.Documents.ShouldHaveSingleItem();
        document.CodeModele.ShouldBe(ModelesDossierSante.CodeCarnet);
        document.Zone.ShouldBe(ZoneDocument.Medicale);
        var texte = Texte(document);
        texte.ShouldContain("5 octobre 2026 — Hépatite B — dose 1 — lot LOT42");
        texte.ShouldContain("HEPATITE_B — dose 2 — à partir du 5 novembre 2026");
        _ctx.Store.Published.OfType<DocumentPublie>().ShouldHaveSingleItem().ObjetType.ShouldBe(ModelesDossierSante.ObjetCarnet);
    }

    [Fact]
    public async Task Une_vaccination_disparue_ne_produit_aucun_carnet()
    {
        PublierModeles();

        await Carnet().HandleAsync(new VaccinationAdministree(Guid.CreateVersion7(), Personne, "HEPATITE_B", 1, new DateOnly(2026, 10, 5)), _ct);

        _ctx.Store.Documents.ShouldBeEmpty();
    }

    [Fact]
    public void Les_modeles_medicaux_du_portail_sont_valides_par_le_moteur_de_fusion_et_chaque_rubrique_a_un_libelle()
    {
        foreach (var d in ModelesDossierSante.Definitions)
        {
            Should.NotThrow(() => Modele.Creer(d.Code, d.Langue, TypeModele.Formulaire, ZoneDocument.Medicale, d.Libelle, null, d.Contenu, d.Champs));
        }

        ModelesDossierSante.Definitions.Select(d => d.Langue).Distinct().Order().ShouldBe([Language.Fr, Language.Nl, Language.De]);

        // Codes émis par la Surveillance médicale (RubriquesCopie.Connus, même liste vérifiée de son côté).
        string[] rubriques =
        [
            "NIVEAU", "FIN", "STATUT", "ANAMNESE", "EXAMEN_CLINIQUE", "RESULTAT", "COMMENTAIRE", "MESURES", "VALIDITE", "JUSTIFICATION",
            "RECOMMANDATIONS", "REPONSE", "DOSE", "REMARQUE", "LECTURE", "INDURATION", "TITRE", "DESCRIPTION", "AGENT", "LOT",
        ];
        foreach (var code in rubriques)
        {
            foreach (var langue in new[] { Language.Fr, Language.Nl, Language.De })
            {
                ModelesDossierSante.LibelleRubrique(code, langue).ShouldNotBeNullOrWhiteSpace($"{code} sans libellé en {langue}.");
            }
        }
    }
}
