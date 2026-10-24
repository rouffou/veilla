using Sepp.BuildingBlocks.Domain;
using Sepp.SurveillanceMedicale.Domain.DemandesCopie;

using Shouldly;

namespace Sepp.SurveillanceMedicale.Domain.Tests;

/// <summary>SAN-43 : cycle de la demande de copie du dossier (déposée → validée → publiée, ou refusée).</summary>
public sealed class DemandeCopieDossierTests
{
    private static readonly DateTimeOffset Depot = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly JourDepot = new(2026, 3, 2);

    private static DemandeCopieDossier Demande() => DemandeCopieDossier.Deposer(Guid.CreateVersion7(), Guid.CreateVersion7(), Depot, JourDepot, 15);

    [Fact]
    public void La_date_limite_suit_le_delai_en_jours_calendrier_et_le_retard_ne_vaut_que_pour_une_demande_non_traitee()
    {
        var demande = Demande();

        demande.DateLimite.ShouldBe(new DateOnly(2026, 3, 17));
        demande.EstEnRetard(new DateOnly(2026, 3, 17)).ShouldBeFalse();
        demande.EstEnRetard(new DateOnly(2026, 3, 18)).ShouldBeTrue();

        demande.Valider(Guid.CreateVersion7(), "cpmt-1", Depot.AddDays(20));
        demande.EstEnRetard(new DateOnly(2026, 3, 30)).ShouldBeFalse();
    }

    [Fact]
    public void Un_delai_nul_est_refuse() =>
        Should.Throw<DomainException>(() => DemandeCopieDossier.Deposer(Guid.CreateVersion7(), null, Depot, JourDepot, 0));

    [Fact]
    public void La_validation_leve_un_evenement_d_identifiants_et_ne_se_fait_qu_une_fois()
    {
        var demande = Demande();

        demande.Valider(Guid.CreateVersion7(), "cpmt-1", Depot);

        demande.Statut.ShouldBe(StatutDemandeCopie.Validee);
        demande.DomainEvents.OfType<CopieDossierValideeLocal>().ShouldHaveSingleItem().DemandeId.ShouldBe(demande.Id);
        Should.Throw<DomainException>(() => demande.Valider(Guid.CreateVersion7(), "cpmt-1", Depot));
        Should.Throw<DomainException>(() => demande.Refuser(MotifRefusCopie.AucunDossier, "cpmt-1", Depot));
    }

    [Fact]
    public void La_publication_exige_une_validation_et_est_idempotente()
    {
        var demande = Demande();
        Should.Throw<DomainException>(() => demande.EnregistrerPublication(Guid.CreateVersion7(), Depot));

        demande.Valider(Guid.CreateVersion7(), "cpmt-1", Depot);
        var document = Guid.CreateVersion7();
        demande.EnregistrerPublication(document, Depot).ShouldBeTrue();
        demande.EnregistrerPublication(Guid.CreateVersion7(), Depot).ShouldBeFalse();

        demande.DocumentId.ShouldBe(document);
        demande.EstEnCours.ShouldBeFalse();
    }

    [Fact]
    public void Un_refus_porte_un_motif_code_et_clot_la_demande()
    {
        var demande = Demande();

        demande.Refuser(MotifRefusCopie.IdentiteNonConfirmee, "cpmt-1", Depot);

        demande.Statut.ShouldBe(StatutDemandeCopie.Refusee);
        demande.EstEnCours.ShouldBeFalse();
        Should.Throw<DomainException>(() => Demande().Refuser((MotifRefusCopie)42, "cpmt-1", Depot));
    }
}
