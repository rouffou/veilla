using Sepp.Affilies.Domain.Affilies;
using Sepp.Affilies.Domain.Groupes;
using Sepp.Affilies.Domain.Historique;
using Sepp.BuildingBlocks.Domain;

using Shouldly;

namespace Sepp.Affilies.Domain.Tests;

public class OperationsTests
{
    private static readonly DateOnly Effet = new(2026, 7, 1);
    private readonly Affilie _affilie = Exemples.Affilie();

    [Fact]
    public void Une_fusion_realisee_cloture_l_affiliation_la_veille_de_la_date_d_effet()
    {
        var absorbant = Guid.CreateVersion7();
        var fusion = _affilie.ProjeterFusion(absorbant, Effet);

        _affilie.RealiserOperation(fusion.Id, Effet);

        fusion.Statut.ShouldBe(StatutOperation.Realisee);
        fusion.DateRealisation.ShouldBe(Effet);
        fusion.AffilieAbsorbantId.ShouldBe(absorbant);
        _affilie.Statut.ShouldBe(StatutAffilie.Absorbe);
        _affilie.DateFin.ShouldBe(new DateOnly(2026, 6, 30));
    }

    [Fact]
    public void Une_operation_ne_se_realise_pas_avant_sa_date_d_effet()
    {
        var fusion = _affilie.ProjeterFusion(Guid.CreateVersion7(), Effet);

        Should.Throw<DomainException>(() => _affilie.RealiserOperation(fusion.Id, Effet.AddDays(-1)));
        fusion.Statut.ShouldBe(StatutOperation.Projetee);
    }

    [Fact]
    public void Une_seule_operation_projetee_a_la_fois()
    {
        _affilie.ProjeterTransfertSortant("SEPP Exemple", Effet);

        Should.Throw<DomainException>(() => _affilie.ProjeterFusion(Guid.CreateVersion7(), Effet));
    }

    [Fact]
    public void Une_scission_designe_des_beneficiaires_distincts_de_l_affilie()
    {
        Should.Throw<DomainException>(() => _affilie.ProjeterScission([], Effet));
        Should.Throw<DomainException>(() => _affilie.ProjeterScission([_affilie.Id], Effet));
        var b1 = Guid.CreateVersion7();
        var b2 = Guid.CreateVersion7();

        var scission = _affilie.ProjeterScission([b1, b2, b1], Effet);
        _affilie.RealiserOperation(scission.Id, Effet.AddDays(3));

        scission.AffiliesBeneficiaires.ShouldBe([b1, b2]);
        _affilie.Statut.ShouldBe(StatutAffilie.Scinde);
    }

    [Fact]
    public void Un_affilie_ne_fusionne_pas_avec_lui_meme() =>
        Should.Throw<DomainException>(() => _affilie.ProjeterFusion(_affilie.Id, Effet));

    [Fact]
    public void Un_affilie_transfere_est_fige()
    {
        var transfert = _affilie.ProjeterTransfertSortant("SEPP Exemple", Effet);
        _affilie.RealiserOperation(transfert.Id, Effet);

        _affilie.Statut.ShouldBe(StatutAffilie.Transfere);
        Should.Throw<DomainException>(() => _affilie.ModifierFiche(Exemples.Fiche(), null));
        Should.Throw<DomainException>(() => _affilie.AjouterContact(new DonneesContact("X", null, RoleContact.PersonneDeContact, null, null), Effet));
        Should.Throw<DomainException>(() => _affilie.ProjeterFusion(Guid.CreateVersion7(), Effet.AddDays(10)));
    }

    [Fact]
    public void Une_operation_annulee_libere_l_affilie()
    {
        var transfert = _affilie.ProjeterTransfertSortant("SEPP Exemple", Effet);
        _affilie.AnnulerOperation(transfert.Id);

        transfert.Statut.ShouldBe(StatutOperation.Annulee);
        _affilie.OperationEnCours.ShouldBeNull();
        Should.Throw<DomainException>(() => _affilie.RealiserOperation(transfert.Id, Effet));
        _affilie.ProjeterFusion(Guid.CreateVersion7(), Effet).Statut.ShouldBe(StatutOperation.Projetee);
    }

    [Fact]
    public void La_date_d_effet_suit_la_date_d_affiliation() =>
        Should.Throw<DomainException>(() => _affilie.ProjeterTransfertSortant("SEPP Exemple", Exemples.Affiliation));

    [Fact]
    public void Une_resiliation_est_impossible_pendant_une_operation()
    {
        _affilie.ProjeterTransfertSortant("SEPP Exemple", Effet);
        Should.Throw<DomainException>(() => _affilie.Resilier(Effet));
    }

    [Fact]
    public void Un_transfert_entrant_est_enregistre_a_l_affiliation()
    {
        var affilie = Exemples.Affilie(seppOrigine: "SEPP d'origine");

        var operation = affilie.Operations.ShouldHaveSingleItem();
        operation.Type.ShouldBe(TypeOperation.TransfertEntrant);
        operation.Statut.ShouldBe(StatutOperation.Realisee);
        operation.SeppContrepartie.ShouldBe("SEPP d'origine");
        operation.DateEffet.ShouldBe(Exemples.Affiliation);
    }

    [Fact]
    public void Un_groupe_a_un_nom()
    {
        var groupe = Groupe.Creer("  Groupe Dupont ");
        groupe.Nom.ShouldBe("Groupe Dupont");
        groupe.Renommer("Dupont Holding");
        groupe.Nom.ShouldBe("Dupont Holding");
        Should.Throw<DomainException>(() => groupe.Renommer(" "));
    }

    [Fact]
    public void Une_entree_d_historique_identifie_version_action_et_auteur()
    {
        var entree = ModificationAffilie.Enregistrer(_affilie.Id, 2, "fiche.modifiee", "u1", DateTimeOffset.UnixEpoch, "{}", "{}");
        entree.NumeroVersion.ShouldBe(2);
        Should.Throw<DomainException>(() => ModificationAffilie.Enregistrer(_affilie.Id, 0, "fiche.modifiee", "u1", DateTimeOffset.UnixEpoch, null, null));
        Should.Throw<DomainException>(() => ModificationAffilie.Enregistrer(_affilie.Id, 1, " ", "u1", DateTimeOffset.UnixEpoch, null, null));
    }
}
