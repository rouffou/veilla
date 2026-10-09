using Sepp.Sagas.Tests.Plateforme;

using Shouldly;

namespace Sepp.Sagas.Tests;

/// <summary>
/// Le bus de test route comme Terraform (<c>subscribes_to</c>, <c>subject_filters</c> de infra/variables.tf) : par rubrique, comme
/// Service Bus ; un service abonné à la rubrique mais sans gestionnaire pour le contrat l'ignore (répartiteur du socle).
/// </summary>
public sealed class RoutageBusTests
{
    private static BusDeTest BusAvecLesCinqHotes()
    {
        var bus = new BusDeTest();
        foreach (var service in new[] { "obligations", "planification", "surveillance-medicale", "communications", "documents" })
        {
            bus.Abonner(service, (_, _, _, _) => Task.FromResult(1));
        }

        return bus;
    }

    private static MessagePublie Message(string sujet) => new("test", Guid.CreateVersion7(), sujet, sujet[..sujet.IndexOf('.', StringComparison.Ordinal)], "{}", DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("obligations.obligation-cloturee.v1", "planification,surveillance-medicale")]
    [InlineData("surveillance-medicale.decision-emise.v1", "documents,obligations")]
    [InlineData("surveillance-medicale.examen-cloture.v1", "documents,obligations")]
    [InlineData("planification.convocation-emise.v1", "communications,obligations,surveillance-medicale")]
    [InlineData("planification.convocation-envoyee.v1", "communications,obligations,surveillance-medicale")]
    [InlineData("communications.message-envoye.v1", "planification")]
    [InlineData("documents.document-publie.v1", "communications,obligations")]
    public void Un_message_est_livre_aux_services_abonnes_a_sa_rubrique(string sujet, string attendus) =>
        BusAvecLesCinqHotes().Destinataires(Message(sujet)).ShouldBe(attendus.Split(','));

    [Fact]
    public void Le_bris_de_glace_est_le_seul_message_d_audit_livre_a_communications()
    {
        var bus = BusAvecLesCinqHotes();

        bus.Destinataires(Message("audit.bris-de-glace-signale.v1")).ShouldBe(["communications"]);
        bus.Destinataires(Message("audit.acces-donnee-sensible.v1")).ShouldBeEmpty();
    }

    [Fact]
    public void L_etat_particulier_est_reserve_a_obligations()
    {
        var bus = BusAvecLesCinqHotes();

        bus.Destinataires(Message("personnes.etat-particulier-declare.v1")).ShouldBe(["obligations"]);
        bus.Destinataires(Message("personnes.affectation-modifiee.v1")).ShouldBe(["obligations", "surveillance-medicale"]);
    }
}
