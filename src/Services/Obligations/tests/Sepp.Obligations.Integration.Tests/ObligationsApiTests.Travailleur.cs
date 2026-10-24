using System.Net;
using System.Net.Http.Json;

using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application.Security;
using Sepp.Contracts.Personnes;
using Sepp.Obligations.Application.Consultation;

using Shouldly;

namespace Sepp.Obligations.Integration.Tests;

// POR-11, POR-12, SAN-02 : routes du portail travailleur (permissions dédiées, personne = claim personne_id) contre un vrai PostgreSQL.
public sealed partial class ObligationsApiTests
{
    private HttpClient ClientTravailleur(params Guid[] personnes)
    {
        var client = Client(Roles.Travailleur, "travailleur-1");
        foreach (var personne in personnes)
        {
            client.DefaultRequestHeaders.Add(TestAuth.PersonnesHeader, personne.ToString());
        }

        return client;
    }

    [Fact]
    public async Task Le_travailleur_demande_pour_la_personne_de_son_jeton_et_jamais_celle_du_corps()
    {
        var moi = Guid.CreateVersion7();
        var autre = Guid.CreateVersion7();
        await OccuperAsync(moi);
        await OccuperAsync(autre);

        var reponse = await ClientTravailleur(moi).PostAsJsonAsync("/api/v1/mes-demandes",
            new { personneId = autre, affilieId = _affilie, type = "CONSULTATION_SPONTANEE", dateDemande = "2026-06-01", motif = "mal au dos" }, Json, Ct);

        reponse.StatusCode.ShouldBe(HttpStatusCode.Created, await reponse.Content.ReadAsStringAsync(Ct));
        var mienne = (await Obligations(moi)).ShouldHaveSingleItem();
        mienne.Type.ShouldBe("CONSULTATION_SPONTANEE");
        mienne.DateLimite.ShouldBe(new DateOnly(2026, 6, 15));
        (await Obligations(autre)).ShouldBeEmpty();

        // Type confidentiel : invisible pour l'employeur, mais le travailleur le voit parmi ses examens à planifier.
        (await Obligations(moi, Employeur)).ShouldBeEmpty();
        var examens = await ClientTravailleur(moi).GetFromJsonAsync<List<ObligationDto>>("/api/v1/mes-examens-a-planifier", Json, Ct);
        examens!.ShouldHaveSingleItem().Id.ShouldBe(mienne.Id);

        // Le motif n'est conservé nulle part (ARC-06).
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Sepp.Obligations.Adapters.Persistence.ObligationsDbContext>();
        db.Demandes.Where(d => d.PersonneId == moi).ToList().ShouldHaveSingleItem().EnregistreePar.ShouldBe("travailleur-1");
        db.Demandes.Any(d => d.PersonneId == autre).ShouldBeFalse();
    }

    [Fact]
    public async Task Le_travailleur_ne_peut_pas_demander_sans_jeton_de_personne_chez_un_inconnu_ou_hors_de_son_employeur()
    {
        var moi = Guid.CreateVersion7();
        await OccuperAsync(moi);
        var corps = new { affilieId = _affilie, type = "VISITE_PRE_REPRISE" };

        // Claim absent, ambigu (deux personnes) ou invalide : aucune personne.
        (await ClientTravailleur().PostAsJsonAsync("/api/v1/mes-demandes", corps, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ClientTravailleur(moi, Guid.CreateVersion7()).PostAsJsonAsync("/api/v1/mes-demandes", corps, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var invalide = Client(Roles.Travailleur);
        invalide.DefaultRequestHeaders.Add(TestAuth.PersonnesHeader, "pas-un-uuid");
        (await invalide.PostAsJsonAsync("/api/v1/mes-demandes", corps, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Personne sans occupation, ou employeur qui ne l'occupe pas.
        (await ClientTravailleur(Guid.CreateVersion7()).PostAsJsonAsync("/api/v1/mes-demandes", corps, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ClientTravailleur(moi).PostAsJsonAsync("/api/v1/mes-demandes", new { affilieId = Guid.CreateVersion7(), type = "VISITE_PRE_REPRISE" }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Type non offert, inconnu, ou date future : refus de validation.
        (await ClientTravailleur(moi).PostAsJsonAsync("/api/v1/mes-demandes", new { affilieId = _affilie, type = "EXAMEN_REPRISE" }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ClientTravailleur(moi).PostAsJsonAsync("/api/v1/mes-demandes", new { affilieId = _affilie, type = "bidon" }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ClientTravailleur(moi).PostAsJsonAsync("/api/v1/mes-demandes", new { affilieId = _affilie, type = "VISITE_PRE_REPRISE", dateDemande = "2027-01-01" }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await Obligations(moi)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Le_travailleur_n_a_acces_ni_aux_routes_internes_ni_a_celles_d_un_autre_profil()
    {
        var moi = Guid.CreateVersion7();
        await OccuperAsync(moi);
        var corps = new { personneId = moi, affilieId = _affilie, type = "CONSULTATION_SPONTANEE" };

        // Pas de obligation:gerer ni obligation:lire : les routes internes restent fermées.
        (await ClientTravailleur(moi).PostAsJsonAsync("/api/v1/demandes-travailleur", corps, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ClientTravailleur(moi).PostAsJsonAsync("/api/v1/recalcul", new { personneId = moi }, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ClientTravailleur(moi).GetAsync($"/api/v1/personnes/{moi}/obligations", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ClientTravailleur(moi).GetAsync($"/api/v1/affilies/{_affilie}/obligations", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Les routes du travailleur sont réservées au rôle travailleur.
        (await Employeur.PostAsJsonAsync("/api/v1/mes-demandes", new { affilieId = _affilie, type = "CONSULTATION_SPONTANEE" }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Cpmt.PostAsJsonAsync("/api/v1/mes-demandes", new { affilieId = _affilie, type = "CONSULTATION_SPONTANEE" }, Json, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Employeur.GetAsync("/api/v1/mes-examens-a-planifier", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Cpmt.GetAsync("/api/v1/mes-examens-a-planifier", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _factory.CreateClient().GetAsync("/api/v1/mes-examens-a-planifier", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Obligations(moi)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Mes_examens_a_planifier_sont_ceux_de_la_personne_du_jeton_seulement()
    {
        var t = NouveauTravailleur();
        var autre = NouveauTravailleur();
        await Appliquer(t);
        await Appliquer(autre);
        var mien = (await Obligations(t.Id)).ShouldHaveSingleItem();

        var miens = await ClientTravailleur(t.Id).GetFromJsonAsync<List<ObligationDto>>("/api/v1/mes-examens-a-planifier?langue=nl", Json, Ct);
        var siens = await ClientTravailleur(autre.Id).GetFromJsonAsync<List<ObligationDto>>("/api/v1/mes-examens-a-planifier", Json, Ct);

        var examen = miens!.ShouldHaveSingleItem();
        examen.Id.ShouldBe(mien.Id);
        examen.PersonneId.ShouldBe(t.Id);
        examen.AffilieId.ShouldBe(_affilie);
        examen.TypeLibelle.ShouldBe("Voorafgaande gezondheidsbeoordeling");
        siens!.ShouldHaveSingleItem().PersonneId.ShouldBe(autre.Id);
        (await ClientTravailleur().GetAsync("/api/v1/mes-examens-a-planifier", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ClientTravailleur(Guid.CreateVersion7()).GetFromJsonAsync<List<ObligationDto>>("/api/v1/mes-examens-a-planifier", Json, Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_examen_deja_planifie_ne_figure_plus_dans_mes_examens_a_planifier()
    {
        var t = NouveauTravailleur();
        await Appliquer(t);
        var obligation = (await Obligations(t.Id)).Single();
        await Livrer(Guid.CreateVersion7(), new Sepp.Contracts.Planification.RendezVousPlanifie(
            Guid.CreateVersion7(), t.Id, _affilie, new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero), [obligation.Id]));

        (await ClientTravailleur(t.Id).GetFromJsonAsync<List<ObligationDto>>("/api/v1/mes-examens-a-planifier", Json, Ct))!.ShouldBeEmpty();
    }
}
