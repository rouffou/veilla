using NSubstitute;

using Sepp.Bff.Travailleur.Aval;
using Sepp.Bff.Travailleur.Ecrans;

using Shouldly;

namespace Sepp.Bff.Travailleur.Tests;

/// <summary>POR-13, SAN-43, SAN-50 : demande de copie du dossier et carnet de vaccination, sans lecture du dossier.</summary>
public sealed class EcransDossierSanteTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Guid _personne = Guid.CreateVersion7();
    private readonly IDemandesCopieApi _api = Substitute.For<IDemandesCopieApi>();

    private EcransDossierSante Ecrans => new(_api);

    private static DemandeCopieAval Demande(Guid personne, string statut = "Deposee", string deposee = "2026-10-01T08:00:00Z") =>
        new(Guid.CreateVersion7(), personne, DateTimeOffset.Parse(deposee), new DateOnly(2026, 10, 16), statut, false, null, null, null, null);

    [Fact]
    public async Task La_demande_est_deposee_sans_identifiant_et_renvoie_son_suivi()
    {
        var demande = Demande(_personne);
        _api.DeposerAsync(Ct).Returns(demande);

        var ecran = await Ecrans.DemanderCopieAsync(_personne, Ct);

        ecran.Id.ShouldBe(demande.Id);
        ecran.Statut.ShouldBe("Deposee");
        ecran.DateLimite.ShouldBe(new DateOnly(2026, 10, 16));
    }

    [Fact]
    public async Task Le_suivi_ecarte_les_demandes_d_une_autre_personne_et_trie_les_plus_recentes_d_abord()
    {
        var ancienne = Demande(_personne, "Publiee", "2026-01-01T08:00:00Z");
        var recente = Demande(_personne);
        _api.ListerMiennesAsync(Ct).Returns([ancienne, Demande(Guid.CreateVersion7()), recente]);

        var liste = await Ecrans.MesDemandesAsync(_personne, Ct);

        liste.Select(d => d.Id).ShouldBe([recente.Id, ancienne.Id]);
    }

    [Fact]
    public async Task Une_reponse_portant_une_autre_personne_est_refusee()
    {
        _api.DeposerAsync(Ct).Returns(Demande(Guid.CreateVersion7()));

        await Should.ThrowAsync<ErreurAvalException>(() => Ecrans.DemanderCopieAsync(_personne, Ct));
    }

    [Theory]
    [InlineData("SANTE.EVALUATION.TRAVAILLEUR", "evaluation-sante")]
    [InlineData("SANTE.CARNET_VACCINATION", "carnet-vaccination")]
    [InlineData("SANTE.COPIE_DOSSIER", "copie-dossier")]
    [InlineData("COURRIER.CONVOCATION", "autre")]
    public void Le_carnet_et_la_copie_ont_leur_categorie_dans_mes_documents(string code, string categorie) =>
        EcransDocuments.Categorie(code).ShouldBe(categorie);
}
