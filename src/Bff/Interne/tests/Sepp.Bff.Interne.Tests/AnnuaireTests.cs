using System.Net;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using Sepp.Bff.Interne.Aval;
using Sepp.Bff.Interne.Ecrans;

using Shouldly;

namespace Sepp.Bff.Interne.Tests;

public sealed class AnnuaireTests
{
    private readonly IPersonnesApi _personnes = Substitute.For<IPersonnesApi>();
    private readonly IAffiliesApi _affilies = Substitute.For<IAffiliesApi>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Le_nom_d_une_personne_est_prenom_puis_nom_et_n_est_demande_qu_une_fois_par_requete()
    {
        var id = Guid.CreateVersion7();
        _personnes.ObtenirPersonneAsync(id, Arg.Any<CancellationToken>()).Returns(new PersonneAval(id, "Martin", "Anne"));
        var annuaire = new Annuaire(_personnes, _affilies);

        var noms = await Task.WhenAll(annuaire.NomPersonneAsync(id, Ct), annuaire.NomPersonneAsync(id, Ct), annuaire.NomPersonneAsync(id, Ct));

        noms.ShouldAllBe(n => n == "Anne Martin");
        await _personnes.Received(1).ObtenirPersonneAsync(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Le_cache_ne_survit_pas_a_la_requete()
    {
        var id = Guid.CreateVersion7();
        _personnes.ObtenirPersonneAsync(id, Arg.Any<CancellationToken>()).Returns(new PersonneAval(id, "Martin", "Anne"));

        await new Annuaire(_personnes, _affilies).NomPersonneAsync(id, Ct);
        await new Annuaire(_personnes, _affilies).NomPersonneAsync(id, Ct);

        await _personnes.Received(2).ObtenirPersonneAsync(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task La_denomination_d_un_affilie_est_lue_dans_sa_fiche()
    {
        var id = Guid.CreateVersion7();
        _affilies.ObtenirAffilieAsync(id, Arg.Any<CancellationToken>())
            .Returns(new AffilieAval(id, new FicheAffilieAval("0123.456.749", "Boulangerie Démo SRL")));

        (await new Annuaire(_personnes, _affilies).DenominationAffilieAsync(id, Ct)).ShouldBe("Boulangerie Démo SRL");
    }

    [Fact]
    public async Task Un_libelle_refuse_introuvable_ou_indisponible_vaut_null_sans_faire_echouer_l_ecran()
    {
        var refuse = Guid.CreateVersion7();
        var inconnu = Guid.CreateVersion7();
        var indisponible = Guid.CreateVersion7();
        var connu = Guid.CreateVersion7();
        _personnes.ObtenirPersonneAsync(refuse, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErreurAvalException("personnes", HttpStatusCode.Forbidden, "interdit", null));
        _personnes.ObtenirPersonneAsync(inconnu, Arg.Any<CancellationToken>())
            .ThrowsAsync(new ErreurAvalException("personnes", HttpStatusCode.NotFound, "introuvable", null));
        _personnes.ObtenirPersonneAsync(indisponible, Arg.Any<CancellationToken>()).ThrowsAsync(new ServiceIndisponibleException("personnes"));
        _personnes.ObtenirPersonneAsync(connu, Arg.Any<CancellationToken>()).Returns(new PersonneAval(connu, "Peeters", "Jan"));

        var noms = await new Annuaire(_personnes, _affilies).NomsPersonnesAsync([refuse, inconnu, indisponible, connu, connu], Ct);

        noms.Count.ShouldBe(4);
        noms[refuse].ShouldBeNull();
        noms[inconnu].ShouldBeNull();
        noms[indisponible].ShouldBeNull();
        noms[connu].ShouldBe("Jan Peeters");
        await _personnes.Received(1).ObtenirPersonneAsync(connu, Arg.Any<CancellationToken>());
    }
}
