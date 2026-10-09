using System.Net;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using Sepp.Bff.Employeur.Aval;
using Sepp.Bff.Employeur.Ecrans;

using Shouldly;

namespace Sepp.Bff.Employeur.Tests;

/// <summary>POR-04 : relais synchrone des reprises vers Obligations (ARC-30) sans logique métier ni donnée médicale.</summary>
public sealed class EcransRepriseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly Guid _affilie = Guid.CreateVersion7();
    private readonly IObligationsApi _obligations = Substitute.For<IObligationsApi>();

    private EcransReprises Ecrans => new(_obligations);

    private static RepriseAval Reprise(Guid affilieId, string dateReprise, string statut = "Annoncee") =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), affilieId, DateOnly.Parse(dateReprise), new DateOnly(2026, 8, 1), statut, new DateOnly(2026, 10, 26), false, false);

    [Fact]
    public async Task L_annonce_est_relayee_avec_l_affilie_de_la_route_et_non_celui_du_corps()
    {
        var personne = Guid.CreateVersion7();
        var repriseId = Guid.CreateVersion7();
        _obligations.AnnoncerRepriseAsync(Arg.Any<RepriseCorpsAval>(), Ct)
            .Returns(new ResultatEnregistrementRepriseAval(repriseId, true, false, "ObligationOuverte"));

        var annoncee = await Ecrans.AnnoncerAsync(_affilie, new RepriseAnnonceCorps(personne, new DateOnly(2026, 10, 12), new DateOnly(2026, 8, 1)), Ct);

        annoncee.ShouldBe(new RepriseAnnoncee(repriseId, true, "ObligationOuverte"));
        await _obligations.Received(1).AnnoncerRepriseAsync(
            new RepriseCorpsAval(personne, _affilie, new DateOnly(2026, 10, 12), new DateOnly(2026, 8, 1)), Ct);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Les_refus_de_l_aval_sont_propages_tels_quels(HttpStatusCode status)
    {
        _obligations.AnnoncerRepriseAsync(Arg.Any<RepriseCorpsAval>(), Ct)
            .Throws(new ErreurAvalException(NomsServices.Obligations, status, "reprise.refusee", "Refusée."));

        var erreur = await Should.ThrowAsync<ErreurAvalException>(() =>
            Ecrans.AnnoncerAsync(_affilie, new RepriseAnnonceCorps(Guid.CreateVersion7(), new DateOnly(2026, 10, 12), new DateOnly(2026, 8, 1)), Ct));

        erreur.Status.ShouldBe(status);
        erreur.Code.ShouldBe("reprise.refusee");
    }

    [Fact]
    public async Task La_liste_ne_garde_que_les_reprises_de_l_affilie_les_plus_recentes_d_abord()
    {
        var ancienne = Reprise(_affilie, "2026-09-01");
        var recente = Reprise(_affilie, "2026-10-12");
        var etrangere = Reprise(Guid.CreateVersion7(), "2026-10-20");
        _obligations.ListerReprisesAsync(_affilie, Ct).Returns([ancienne, etrangere, recente]);

        var liste = await Ecrans.ListerAsync(_affilie, Ct);

        liste.Select(r => r.Id).ShouldBe([recente.Id, ancienne.Id]);
    }

    [Fact]
    public async Task Une_reprise_d_un_autre_affilie_est_inconnue()
    {
        var etrangere = Reprise(Guid.CreateVersion7(), "2026-10-12");
        _obligations.ObtenirRepriseAsync(etrangere.Id, Ct).Returns(etrangere);

        var erreur = await Should.ThrowAsync<ErreurAvalException>(() => Ecrans.ObtenirAsync(_affilie, etrangere.Id, Ct));

        erreur.Status.ShouldBe(HttpStatusCode.NotFound);
        erreur.Code.ShouldBe("reprise.inconnue");
    }

    [Fact]
    public async Task Le_suivi_porte_le_statut_et_la_date_limite_mais_aucun_identifiant_d_examen_ou_de_decision()
    {
        var reprise = Reprise(_affilie, "2026-10-12", "Convoquee");
        _obligations.ObtenirRepriseAsync(reprise.Id, Ct).Returns(reprise);

        var ecran = await Ecrans.ObtenirAsync(_affilie, reprise.Id, Ct);

        ecran.Statut.ShouldBe("Convoquee");
        ecran.DateLimite.ShouldBe(new DateOnly(2026, 10, 26));
        typeof(RepriseEcran).GetProperties().Select(p => p.Name)
            .ShouldNotContain(n => n.Contains("Examen") || n.Contains("Decision") || n.Contains("Obligation"));
    }
}
