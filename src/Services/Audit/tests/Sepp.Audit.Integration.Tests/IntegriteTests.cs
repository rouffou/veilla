using System.Net.Http.Json;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Sepp.Audit.Application.Journal;
using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;

using Shouldly;

namespace Sepp.Audit.Integration.Tests;

/// <summary>
/// Journal infalsifiable (NF-04) : la base refuse modifications et suppressions, et toute altération faite en
/// contournant ces protections (SQL brut d'un administrateur) est détectée par la vérification de la chaîne.
/// </summary>
public sealed class IntegriteTests : AuditServiceTestBase
{
    private const string InsufficientPrivilege = "42501";

    private Task<IntegriteDto?> Integrite(string zone) =>
        Client(Roles.Dpo).GetFromJsonAsync<IntegriteDto>($"/api/v1/audit/integrite/{zone}", Ct);

    [Fact]
    public async Task Une_chaine_intacte_est_declaree_integre()
    {
        await RecevoirTous([Acces(), Acces(), Acces()]);

        var integrite = await Integrite("medicale");

        integrite!.Integre.ShouldBeTrue();
        integrite.EntreesVerifiees.ShouldBe(3);
        integrite.DernierNumero.ShouldBe(3);
        integrite.Anomalie.ShouldBeNull();
    }

    [Fact]
    public async Task Une_ligne_modifiee_en_sql_brut_est_detectee()
    {
        await RecevoirTous([Acces(), Acces(), Acces()]);

        (await Sql("UPDATE entree_audit SET utilisateur_id = 'quelqu-un-d-autre' WHERE zone = 'Medicale' AND numero = 2", contournerDeclencheurs: true)).ShouldBe(1);

        var integrite = await Integrite("medicale");
        integrite!.Integre.ShouldBeFalse();
        integrite.EntreesVerifiees.ShouldBe(1);
        integrite.Anomalie!.Numero.ShouldBe(2);
        integrite.Anomalie.Type.ShouldBe("EmpreinteInvalide");
    }

    [Fact]
    public async Task Une_ligne_supprimee_en_sql_brut_est_detectee()
    {
        await RecevoirTous([Acces(), Acces(), Acces()]);

        (await Sql("DELETE FROM entree_audit WHERE zone = 'Medicale' AND numero = 2", contournerDeclencheurs: true)).ShouldBe(1);

        var integrite = await Integrite("medicale");
        integrite!.Integre.ShouldBeFalse();
        integrite.Anomalie!.Numero.ShouldBe(2);
        integrite.Anomalie.Type.ShouldBe("EntreeManquante");
    }

    [Fact]
    public async Task Une_alteration_ne_touche_que_la_chaine_de_sa_zone()
    {
        await RecevoirTous([Acces("medicale"), Acces("psychosociale"), Acces("medicale")]);

        await Sql("UPDATE entree_audit SET motif = 'ajout' WHERE zone = 'Medicale' AND numero = 1", contournerDeclencheurs: true);

        (await Integrite("medicale"))!.Integre.ShouldBeFalse();
        (await Integrite("psychosociale"))!.Integre.ShouldBeTrue();
    }

    [Theory]
    [InlineData("UPDATE entree_audit SET motif = 'x'")]
    [InlineData("DELETE FROM entree_audit")]
    [InlineData("TRUNCATE entree_audit")]
    [InlineData("DELETE FROM sceau_purge")]
    public async Task La_base_refuse_toute_modification_du_journal(string sql)
    {
        await RecevoirTous([Acces(), Acces()]);

        var erreur = await Should.ThrowAsync<PostgresException>(() => Sql(sql));

        erreur.SqlState.ShouldBe(InsufficientPrivilege);
        (await Scalaire("SELECT count(*) FROM entree_audit")).ShouldBe(2);
    }

    [Fact]
    public async Task Meme_autorisee_une_purge_ne_supprime_pas_une_entree_de_moins_de_dix_ans()
    {
        await RecevoirTous([Acces(), Acces()]);

        var erreur = await Should.ThrowAsync<PostgresException>(() => Sql("""
            BEGIN;
            SET LOCAL sepp.audit_purge = 'on';
            DELETE FROM entree_audit WHERE numero = 1;
            COMMIT;
            """));

        erreur.MessageText.ShouldContain("moins de 10 ans");
        (await Scalaire("SELECT count(*) FROM entree_audit")).ShouldBe(2);
    }

    [Fact]
    public async Task Meme_autorisee_une_suppression_unitaire_ancienne_est_refusee()
    {
        var ancien = DateTimeOffset.UtcNow.AddYears(-11);
        await RecevoirTous([Acces(horodatage: ancien), Acces(horodatage: ancien), Acces(horodatage: ancien)]);

        var erreur = await Should.ThrowAsync<PostgresException>(() => Sql("""
            BEGIN;
            SET LOCAL sepp.audit_purge = 'on';
            DELETE FROM entree_audit WHERE zone = 'Medicale' AND numero = 2;
            COMMIT;
            """));

        erreur.MessageText.ShouldContain("préfixe");
        (await Scalaire("SELECT count(*) FROM entree_audit")).ShouldBe(3);
    }

    [Fact]
    public async Task La_purge_legale_supprime_le_prefixe_echu_scelle_et_la_chaine_reste_verifiable()
    {
        var ancien = DateTimeOffset.UtcNow.AddYears(-11);
        await RecevoirTous(
        [
            Acces("psychosociale", horodatage: ancien),
            Acces("psychosociale", horodatage: ancien.AddDays(1)),
            Acces("psychosociale", horodatage: ancien.AddDays(2)),
            Acces("psychosociale"),
            Acces("psychosociale", horodatage: ancien.AddDays(3)), // arrivée tardive : après une entrée récente, conservée
            Acces("medicale"),
        ]);

        ResultatPurge resultat;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<PurgerJournal, ResultatPurge>>();
            resultat = (await handler.HandleAsync(new PurgerJournal(10), Ct)).Value;
        }

        resultat.EntreesPurgeesParZone["psychosociale"].ShouldBe(3);
        resultat.EntreesPurgeesParZone["medicale"].ShouldBe(0);
        (await Scalaire("SELECT numero_final FROM sceau_purge WHERE zone = 'Psychosociale'")).ShouldBe(3);
        (await Scalaire("SELECT nombre_entrees FROM sceau_purge WHERE zone = 'Psychosociale'")).ShouldBe(3);

        var integrite = await Integrite("psychosociale");
        integrite!.Integre.ShouldBeTrue();
        integrite.NumeroDepart.ShouldBe(3);
        integrite.EntreesVerifiees.ShouldBe(2);

        // La chaîne se poursuit après la purge.
        await Recevoir(Acces("psychosociale"));
        var apres = await Integrite("psychosociale");
        apres!.Integre.ShouldBeTrue();
        apres.DernierNumero.ShouldBe(6);
    }

    [Fact]
    public async Task Une_purge_du_prefixe_sans_sceau_est_detectee()
    {
        var ancien = DateTimeOffset.UtcNow.AddYears(-11);
        await RecevoirTous([Acces(horodatage: ancien), Acces()]);

        await Sql("DELETE FROM entree_audit WHERE zone = 'Medicale' AND numero = 1", contournerDeclencheurs: true);

        var integrite = await Integrite("medicale");
        integrite!.Integre.ShouldBeFalse();
        integrite.Anomalie!.Numero.ShouldBe(1);
    }
}
