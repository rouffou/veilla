using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Sepp.BuildingBlocks.Infrastructure.Security;
using Sepp.SurveillanceMedicale.Adapters.Persistence;

using Shouldly;

namespace Sepp.SurveillanceMedicale.Integration.Tests;

/// <summary>
/// Rechiffrement des champs (#396, ADR 0012) sur la base réelle du service (contenu clinique, zone médicale) : toutes les colonnes chiffrées du
/// modèle sont couvertes (garde-fou : une colonne ajoutée ou renommée doit apparaître ici), et une base dont toutes les valeurs
/// utilisent déjà la DEK courante n'est pas réécrite.
/// </summary>
public sealed partial class SurveillanceMedicaleApiTests
{
    /// <summary>
    /// Garde-fou d'architecture et de données (#482) : tout convertisseur qui chiffre porte le marqueur et le suffixe
    /// (sinon <see cref="ColonnesChiffrees.Depuis"/> échoue), et la vérification fondée sur les données ne trouve aucune
    /// valeur chiffrée dans une colonne que le modèle ne déclare pas chiffrée.
    /// </summary>
    [Fact]
    public async Task Les_convertisseurs_de_chiffrement_sont_marques_et_aucune_valeur_chiffree_n_est_hors_du_modele()
    {
        IReadOnlyList<ColonneChiffree> colonnes;
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            colonnes = ColonnesChiffrees.Depuis(scope.ServiceProvider.GetRequiredService<SurveillanceMedicaleDbContext>().Model);
        }

        var verification = await RechiffrementHote.VerifierAsync<SurveillanceMedicaleDbContext>(
            fixture.Factory.Services, s => s.GetRequiredService<ChiffrementZoneMedicale>().Encryptor, new OptionsRechiffrement(), NullLogger.Instance, _ct);

        colonnes.ShouldNotBeEmpty();
        verification.ColonnesHorsModele.ShouldBeEmpty();
        verification.ColonnesAbsentes.ShouldBeEmpty();
        verification.Colonnes.Where(c => c.DuModele).Select(c => c.Colonne).ShouldBe(colonnes.Select(c => c.Nom));
    }

    [Fact]
    public async Task Le_rechiffrement_couvre_toutes_les_colonnes_chiffrees_et_ne_reecrit_pas_les_valeurs_a_jour()
    {
        var rapport = await RechiffrementHote.ExecuterAsync<SurveillanceMedicaleDbContext>(
            fixture.Factory.Services, s => s.GetRequiredService<ChiffrementZoneMedicale>().Encryptor, new OptionsRechiffrement { PauseEntreLots = TimeSpan.Zero }, NullLogger.Instance, _ct);

        rapport.Colonnes.Select(c => c.Colonne).ShouldBe(
        [
            "decision.justification_chiffre",
            "decision.recommandations_chiffre",
            "declaration_mp.contenu_chiffre",
            "demande_information_fedris.objet_chiffre",
            "demande_information_fedris.reponse_chiffre",
            "observation_clinique.anamnese_chiffre",
            "observation_clinique.examen_clinique_chiffre",
            "piece_jointe.description_chiffre",
            "piece_jointe.titre_chiffre",
            "questionnaire_rempli.reponses_chiffre",
            "recours.commentaire_chiffre",
            "resultat_acte.commentaire_chiffre",
            "resultat_acte.valeurs_chiffre",
            "test_tuberculinique.lecture_chiffre",
            "transfert_dossier.paquet_chiffre",
            "vaccination.remarque_chiffre",
        ]);
        rapport.Complet.ShouldBeTrue();
        rapport.Rechiffrees.ShouldBe(0);
        rapport.Colonnes.ShouldAllBe(c => c.Examinees == 0);
    }
}
