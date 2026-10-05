using System.Net;
using System.Security.Claims;
using System.Text;

using Microsoft.AspNetCore.Http;

using Sepp.Bff.Employeur.Aval;
using Sepp.Bff.Employeur.Ecrans;
using Sepp.Bff.Employeur.Securite;
using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Web;

using Shouldly;

namespace Sepp.Bff.Employeur.Tests;

public sealed class PerimetreEmployeurTests
{
    private static readonly Guid A = Guid.Parse("0192a5c8-0000-7000-8000-000000000001");
    private static readonly Guid B = Guid.Parse("0192a5c8-0000-7000-8000-000000000002");

    private static ClaimsPrincipal Utilisateur(params string[] valeurs) =>
        new(new ClaimsIdentity(valeurs.Select(v => new Claim(PerimetreEmployeur.ClaimAffilie, v)), "test"));

    [Fact]
    public void Le_claim_affilie_id_repete_donne_plusieurs_affilies() =>
        PerimetreEmployeur.Affilies(Utilisateur(A.ToString(), B.ToString())).ShouldBe([A, B]);

    [Fact]
    public void Un_tableau_JSON_ou_une_liste_separee_par_des_virgules_sont_acceptes()
    {
        PerimetreEmployeur.Affilies(Utilisateur($"[\"{A}\",\"{B}\"]")).ShouldBe([A, B]);
        PerimetreEmployeur.Affilies(Utilisateur($"{A}, {B}")).ShouldBe([A, B]);
    }

    [Fact]
    public void Les_valeurs_qui_ne_sont_pas_des_UUID_sont_ignorees() =>
        PerimetreEmployeur.Affilies(Utilisateur("0123456749", A.ToString(), A.ToString())).ShouldBe([A]);

    [Fact]
    public void Sans_claim_aucun_affilie_n_est_accessible()
    {
        var utilisateur = Utilisateur();
        PerimetreEmployeur.Affilies(utilisateur).ShouldBeEmpty();
        PerimetreEmployeur.PeutAcceder(utilisateur, A).ShouldBeFalse();
    }

    [Fact]
    public void Seuls_les_affilies_du_jeton_sont_accessibles()
    {
        var utilisateur = Utilisateur(A.ToString());
        PerimetreEmployeur.PeutAcceder(utilisateur, A).ShouldBeTrue();
        PerimetreEmployeur.PeutAcceder(utilisateur, B).ShouldBeFalse();
    }
}

public sealed class ListeNominativeCsvTests
{
    private static ListeNominativeDetail Detail(params LigneListeEcran[] lignes) => new(
        new ListeNominativeEcran(Guid.CreateVersion7(), "PosteSecurite", 3, new DateOnly(2026, 9, 1), DateTimeOffset.UnixEpoch, lignes.Length, true),
        lignes);

    private static string Texte(byte[] contenu)
    {
        contenu.Take(3).ShouldBe(Encoding.UTF8.GetPreamble(), "BOM UTF-8 attendu pour l'ouverture dans un tableur.");
        return Encoding.UTF8.GetString(contenu, 3, contenu.Length - 3);
    }

    [Fact]
    public void Le_CSV_a_un_en_tete_traduit_et_une_ligne_par_inscription()
    {
        var csv = Texte(ListeNominativeCsv.Generer(Detail(
            new LigneListeEcran(Guid.CreateVersion7(), "Dupont", "Jean", Guid.CreateVersion7(), "Soudeur", ["BRUIT", "VIBRATIONS"], new DateOnly(2026, 3, 1), "Calcul")),
            Language.Nl));

        var lignes = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lignes[0].ShouldBe("Naam;Voornaam;Functie;Risico's;Datum van de laatste beoordeling;Oorsprong");
        lignes[1].ShouldBe("Dupont;Jean;Soudeur;\"BRUIT, VIBRATIONS\";2026-03-01;Calcul");
    }

    [Fact]
    public void Les_cellules_sont_echappees_et_neutralisees_contre_l_injection_de_formules()
    {
        ListeNominativeCsv.Cellule("=HYPERLINK(\"x\")").ShouldBe("\"'=HYPERLINK(\"\"x\"\")\"");
        ListeNominativeCsv.Cellule("Atelier; zone 2").ShouldBe("\"Atelier; zone 2\"");
        ListeNominativeCsv.Cellule("-12").ShouldBe("'-12");
        ListeNominativeCsv.Cellule("Soudeur").ShouldBe("Soudeur");
    }

    [Fact]
    public void Le_nom_de_fichier_porte_le_type_la_version_et_la_date() =>
        ListeNominativeCsv.NomFichier(Detail().Liste).ShouldBe("liste-nominative-poste-securite-v3-2026-09-01.csv");
}

public sealed class PropagationJetonHandlerTests
{
    [Fact]
    public async Task Le_jeton_et_la_correlation_de_l_utilisateur_sont_recopies_sur_l_appel_aval()
    {
        var contexte = new DefaultHttpContext();
        contexte.Request.Headers.Authorization = "Bearer jeton-utilisateur";
        contexte.Response.Headers[CorrelationIdMiddleware.Header] = "corr-123";
        var capture = new Capture();
        using var client = new HttpClient(new PropagationJetonHandler(new HttpContextAccessor { HttpContext = contexte }) { InnerHandler = capture });

        await client.GetAsync("http://affilies.test/api/v1/affilies", TestContext.Current.CancellationToken);

        capture.Requete!.Headers.Authorization!.ToString().ShouldBe("Bearer jeton-utilisateur");
        capture.Requete.Headers.GetValues(CorrelationIdMiddleware.Header).ShouldBe(["corr-123"]);
    }

    [Fact]
    public async Task Hors_requete_HTTP_aucun_jeton_n_est_invente()
    {
        var capture = new Capture();
        using var client = new HttpClient(new PropagationJetonHandler(new HttpContextAccessor()) { InnerHandler = capture });

        await client.GetAsync("http://affilies.test/", TestContext.Current.CancellationToken);

        capture.Requete!.Headers.Authorization.ShouldBeNull();
    }

    private sealed class Capture : HttpMessageHandler
    {
        public HttpRequestMessage? Requete { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requete = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
