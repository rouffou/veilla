using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Obligations.Application.Travailleur;
using Sepp.Obligations.Domain.Calcul;
using Sepp.Obligations.Domain.Obligations;

using Shouldly;

namespace Sepp.Obligations.Application.Tests;

/// <summary>Portail travailleur : demandes et examens à planifier de la personne du jeton, jamais d'une autre (POR-11, POR-12, SAN-02).</summary>
public class TravailleurTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Banc _banc = new();

    private InMemoryStore Store => _banc.Store;

    private sealed class ContexteFixe(Guid? personne) : IContextePersonne
    {
        public Guid? PersonneId => personne;
    }

    private EnregistrerMaDemandeHandler Demander(ICurrentUser user, Guid? personneDuJeton) =>
        new(Store, Store, _banc.Recalcul, Store, user, new ContexteFixe(personneDuJeton), _banc.Clock);

    private ListerMesExamensAPlanifierHandler Lister(ICurrentUser user, Guid? personneDuJeton) =>
        new(Store, user, new ContexteFixe(personneDuJeton), _banc.Clock, _banc.Options);

    private Obligation Ajouter(string cle, DateOnly due, TypeObligation type, Guid personne, StatutObligation? statut = null)
    {
        var echeance = new EcheanceCalculee(
            cle, _banc.Affilie, type, OrigineObligation.Regle, ["R1"], due, due, null,
            new Justification("regle-surveillance:R1", 1, $"Calcul {cle}", [new KeyValuePair<string, string>("frequence_mois", "12")]));
        var obligation = Obligation.Creer(personne, echeance, Banc.Midi);
        if (statut == StatutObligation.Realise)
        {
            obligation.Realiser(new Realisation(new DateOnly(2026, 6, 1), Guid.CreateVersion7()));
        }

        Store.Obligations.Add(obligation);
        return obligation;
    }

    // --- POR-12 : demande ---

    [Fact]
    public async Task Le_travailleur_demande_pour_lui_meme_chez_un_employeur_qui_l_occupe()
    {
        _banc.OccupationActive();

        var resultat = await Demander(Banc.Travailleur, _banc.Personne)
            .HandleAsync(new EnregistrerMaDemande(_banc.Affilie, TypeObligation.ConsultationSpontanee), Ct);

        resultat.IsSuccess.ShouldBeTrue(resultat.Error?.Message);
        var demande = Store.Demandes.ShouldHaveSingleItem();
        demande.PersonneId.ShouldBe(_banc.Personne);
        demande.AffilieId.ShouldBe(_banc.Affilie);
        demande.EnregistreePar.ShouldBe("travailleur-1");
        Store.Obligations.ShouldHaveSingleItem().PersonneId.ShouldBe(_banc.Personne);
    }

    [Fact]
    public async Task Sans_personne_dans_le_jeton_la_demande_est_refusee_sans_effet()
    {
        _banc.OccupationActive();

        var resultat = await Demander(Banc.Travailleur, null)
            .HandleAsync(new EnregistrerMaDemande(_banc.Affilie, TypeObligation.ConsultationSpontanee), Ct);

        resultat.Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        resultat.Error.Code.ShouldBe("perimetre.interdit");
        Store.Demandes.ShouldBeEmpty();
        Store.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Le_travailleur_ne_demande_pas_chez_un_employeur_qui_ne_l_occupe_pas()
    {
        _banc.OccupationActive();

        var resultat = await Demander(Banc.Travailleur, _banc.Personne)
            .HandleAsync(new EnregistrerMaDemande(Guid.CreateVersion7(), TypeObligation.VisitePreReprise), Ct);

        resultat.Error!.Code.ShouldBe("perimetre.interdit");
        Store.Demandes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Une_autre_personne_occupee_chez_l_employeur_ne_donne_aucun_droit_au_travailleur_du_jeton()
    {
        // Seul l'autre travailleur est occupé chez l'affilié : le jeton désigne une personne sans occupation.
        _banc.OccupationActive();
        var autre = Guid.CreateVersion7();

        var resultat = await Demander(Banc.Travailleur, autre)
            .HandleAsync(new EnregistrerMaDemande(_banc.Affilie, TypeObligation.ConsultationSpontanee), Ct);

        resultat.Error!.Code.ShouldBe("perimetre.interdit");
        Store.Demandes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_type_hors_demande_du_travailleur_ou_une_date_future_est_refuse()
    {
        _banc.OccupationActive();
        var handler = Demander(Banc.Travailleur, _banc.Personne);

        (await handler.HandleAsync(new EnregistrerMaDemande(_banc.Affilie, TypeObligation.ExamenReprise), Ct)).Error!.Kind.ShouldBe(ErrorKind.Validation);
        (await handler.HandleAsync(new EnregistrerMaDemande(_banc.Affilie, TypeObligation.ConsultationSpontanee, new DateOnly(2027, 1, 1)), Ct))
            .Error!.Kind.ShouldBe(ErrorKind.Validation);
        Store.Demandes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Seul_le_travailleur_a_la_permission_de_demander_pour_soi()
    {
        _banc.OccupationActive();

        foreach (var user in new[] { Banc.Employeur, Banc.Cpmt, new FakeUser("sipp-1", Roles.Sipp) })
        {
            var resultat = await Demander(user, _banc.Personne)
                .HandleAsync(new EnregistrerMaDemande(_banc.Affilie, TypeObligation.ConsultationSpontanee), Ct);

            resultat.Error!.Code.ShouldBe("obligations.interdit");
        }

        Store.Demandes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Le_travailleur_n_a_ni_obligation_gerer_ni_obligation_lire()
    {
        Banc.Travailleur.HasPermission(Permissions.ObligationGerer).ShouldBeFalse();
        Banc.Travailleur.HasPermission(Permissions.ObligationLire).ShouldBeFalse();
        Banc.Travailleur.HasPermission(Permissions.ObligationDemander).ShouldBeTrue();
        Banc.Cpmt.HasPermission(Permissions.ObligationDemander).ShouldBeFalse();
        await Task.CompletedTask;
    }

    // --- POR-11 : examens à planifier ---

    [Fact]
    public async Task Les_examens_a_planifier_sont_ceux_de_la_personne_du_jeton_tries_par_echeance()
    {
        var tard = Ajouter("B", new DateOnly(2026, 9, 1), TypeObligation.EvaluationPeriodique, _banc.Personne);
        var tot = Ajouter("A", new DateOnly(2026, 7, 1), TypeObligation.ExamenReprise, _banc.Personne);
        Ajouter("AUTRE", new DateOnly(2026, 6, 1), TypeObligation.EvaluationPeriodique, Guid.CreateVersion7());
        Ajouter("FAIT", new DateOnly(2026, 5, 1), TypeObligation.EvaluationPeriodique, _banc.Personne, StatutObligation.Realise);

        var resultat = await Lister(Banc.Travailleur, _banc.Personne).HandleAsync(new ListerMesExamensAPlanifier(Language.Fr), Ct);

        resultat.Value.Select(o => o.Id).ShouldBe([tot.Id, tard.Id]);
        resultat.Value.ShouldAllBe(o => o.PersonneId == _banc.Personne && o.AffilieId == _banc.Affilie);
    }

    [Fact]
    public async Task Un_examen_deja_planifie_n_est_plus_a_planifier()
    {
        var planifie = Ajouter("P", new DateOnly(2026, 7, 1), TypeObligation.EvaluationPeriodique, _banc.Personne);
        planifie.Planifier(Guid.CreateVersion7(), Banc.Midi.AddDays(20));
        var libre = Ajouter("L", new DateOnly(2026, 8, 1), TypeObligation.EvaluationPeriodique, _banc.Personne);

        var resultat = await Lister(Banc.Travailleur, _banc.Personne).HandleAsync(new ListerMesExamensAPlanifier(Language.Fr), Ct);

        resultat.Value.ShouldHaveSingleItem().Id.ShouldBe(libre.Id);
    }

    [Fact]
    public async Task Le_travailleur_voit_ses_propres_types_confidentiels()
    {
        Ajouter("C", new DateOnly(2026, 6, 20), TypeObligation.ConsultationSpontanee, _banc.Personne);

        var resultat = await Lister(Banc.Travailleur, _banc.Personne).HandleAsync(new ListerMesExamensAPlanifier(Language.Fr), Ct);

        resultat.Value.ShouldHaveSingleItem().Type.ShouldBe("CONSULTATION_SPONTANEE");
    }

    [Fact]
    public async Task Sans_personne_dans_le_jeton_ou_sans_permission_la_liste_est_refusee()
    {
        Ajouter("A", new DateOnly(2026, 7, 1), TypeObligation.EvaluationPeriodique, _banc.Personne);

        (await Lister(Banc.Travailleur, null).HandleAsync(new ListerMesExamensAPlanifier(Language.Fr), Ct)).Error!.Code.ShouldBe("perimetre.interdit");
        (await Lister(Banc.Employeur, _banc.Personne).HandleAsync(new ListerMesExamensAPlanifier(Language.Fr), Ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
        (await Lister(Banc.Cpmt, _banc.Personne).HandleAsync(new ListerMesExamensAPlanifier(Language.Fr), Ct)).Error!.Kind.ShouldBe(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task Un_travailleur_ne_voit_pas_les_examens_d_une_autre_personne()
    {
        Ajouter("A", new DateOnly(2026, 7, 1), TypeObligation.EvaluationPeriodique, _banc.Personne);

        var resultat = await Lister(Banc.Travailleur, Guid.CreateVersion7()).HandleAsync(new ListerMesExamensAPlanifier(Language.Fr), Ct);

        resultat.Value.ShouldBeEmpty();
    }
}
