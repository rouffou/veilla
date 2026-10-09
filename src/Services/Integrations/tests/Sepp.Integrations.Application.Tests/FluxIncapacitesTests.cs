using Sepp.Contracts.Integrations;
using Sepp.Contracts.Reintegration;
using Sepp.Integrations.Application.Externe;
using Sepp.Integrations.Application.Flux;
using Sepp.Integrations.Domain.Correspondances;
using Sepp.Integrations.Domain.Flux;

using Shouldly;

namespace Sepp.Integrations.Application.Tests;

/// <summary>SAN-60 : flux eHealth des notifications d'incapacité → integrations.incapacite-notifiee.</summary>
public sealed class FluxIncapacitesTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static NotificationIncapacite Notification(string reference = "INC-001", DateOnly? fin = null, EmetteurIncapacite emetteur = EmetteurIncapacite.Employeur) =>
        new(reference, Contexte.Niss, Contexte.Employeur, emetteur, new DateOnly(2026, 3, 2), fin);

    [Fact]
    public async Task Une_notification_d_un_travailleur_suivi_publie_l_incapacite_sans_niss()
    {
        var ctx = new Contexte();
        var affilie = ctx.AffilieConnu();
        var personne = Guid.CreateVersion7();
        ctx.Personnes.Suivies[Contexte.Niss] = personne;
        ctx.Incapacites.Lot.Add(Notification(fin: new DateOnly(2026, 4, 30), emetteur: EmetteurIncapacite.MedecinConseil));

        var rapport = await ctx.Execution.ExecuterAsync(TypeFlux.Incapacites, _ct);

        rapport.Traites.ShouldBe(1);
        var evenement = ctx.Store.Published.OfType<IncapaciteNotifiee>().ShouldHaveSingleItem();
        evenement.PersonneId.ShouldBe(personne);
        evenement.AffilieId.ShouldBe(affilie);
        evenement.DateDebut.ShouldBe(new DateOnly(2026, 3, 2));
        evenement.DateFinPrevue.ShouldBe(new DateOnly(2026, 4, 30));
        evenement.Source.ShouldBe(CodesReintegration.SourcesIncapacite.MedecinConseil);
        var echange = ctx.Store.Journal.ShouldHaveSingleItem();
        echange.TypeMessage.ShouldBe(TypesMessage.NotificationIncapacite);
        echange.CleIdempotence.ShouldNotContain(Contexte.Niss);
        ctx.Store.Correspondances.Single(c => c.TypeExterne == TypeIdentifiantExterne.ReferenceIncapacite).IdentifiantInterne.ShouldBe(evenement.IncapaciteId);
    }

    [Fact]
    public async Task Une_prolongation_republie_le_meme_identifiant_et_un_renvoi_identique_n_est_pas_retraite()
    {
        var ctx = new Contexte();
        ctx.AffilieConnu();
        ctx.Personnes.Suivies[Contexte.Niss] = Guid.CreateVersion7();
        ctx.Incapacites.Lot.AddRange([Notification(fin: new DateOnly(2026, 4, 30)), Notification(fin: new DateOnly(2026, 6, 30)), Notification(fin: new DateOnly(2026, 6, 30))]);

        var rapport = await ctx.Execution.ExecuterAsync(TypeFlux.Incapacites, _ct);

        rapport.Recus.ShouldBe(2);
        rapport.DejaRecus.ShouldBe(1);
        var publies = ctx.Store.Published.OfType<IncapaciteNotifiee>().ToList();
        publies.Count.ShouldBe(2);
        publies.Select(p => p.IncapaciteId).Distinct().ShouldHaveSingleItem();
        publies[^1].DateFinPrevue.ShouldBe(new DateOnly(2026, 6, 30));

        // Une seule correspondance référence ↔ incapacité : la prolongation reprend l'identifiant connu.
        ctx.Store.Correspondances.Count(c => c.TypeExterne == TypeIdentifiantExterne.ReferenceIncapacite).ShouldBe(1);
    }

    [Fact]
    public async Task Employeur_inconnu_ou_personne_non_suivie_rejettent_l_echange_et_une_panne_le_met_en_erreur()
    {
        var ctx = new Contexte();
        ctx.Incapacites.Lot.Add(Notification("INC-EMP"));
        await ctx.Execution.ExecuterAsync(TypeFlux.Incapacites, _ct);
        ctx.Store.Journal.Single().CodeErreur.ShouldBe("integrations.employeur-inconnu");

        var autre = new Contexte();
        autre.AffilieConnu();
        autre.Incapacites.Lot.Add(Notification("INC-PERS"));
        await autre.Execution.ExecuterAsync(TypeFlux.Incapacites, _ct);
        var rejete = autre.Store.Journal.Single();
        rejete.Statut.ShouldBe(StatutEchange.Rejete);
        rejete.CodeErreur.ShouldBe("integrations.personne-inconnue");
        (rejete.MessageErreur ?? string.Empty).ShouldNotContain(Contexte.Niss);

        var panne = new Contexte();
        panne.AffilieConnu();
        panne.Personnes.ReponseIdentification = () => ResultatAppel<Guid?>.Erreur("integrations.personnes-indisponible", "Service Personnes injoignable.");
        panne.Incapacites.Lot.Add(Notification("INC-PANNE"));
        await panne.Execution.ExecuterAsync(TypeFlux.Incapacites, _ct);
        panne.Store.Journal.Single().Statut.ShouldBe(StatutEchange.EnErreur);
        panne.Store.Published.OfType<IncapaciteNotifiee>().ShouldBeEmpty();
    }

    [Fact]
    public void La_notification_ne_revele_pas_le_niss_dans_ses_traces() =>
        Notification().ToString().ShouldNotContain(Contexte.Niss);
}
