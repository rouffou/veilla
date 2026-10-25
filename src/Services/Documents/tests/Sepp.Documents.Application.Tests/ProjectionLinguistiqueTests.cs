using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Affilies;
using Sepp.Contracts.Personnes;
using Sepp.Documents.Application.Langues;
using Sepp.Documents.Domain.Langues;

using Shouldly;

namespace Sepp.Documents.Application.Tests;

/// <summary>NF-41, ADR 0009 : projection du contexte linguistique alimentée par les événements d'Affiliés et de Personnes.</summary>
public sealed class ProjectionLinguistiqueTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 8, 0, 0, TimeSpan.Zero);
    private readonly ContextesEnMemoire _contextes = new();
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private ProjectionLinguistiqueHandler Handler => new(_contextes, _contextes);

    [Fact]
    public async Task AffilieCree_projette_le_regime_et_la_langue_de_l_affilie()
    {
        var affilie = Guid.CreateVersion7();

        await Handler.HandleAsync(new AffilieCree(affilie, "0202239951", "B", "BruxellesCapitale", "Nl") { OccurredAt = T0 }, _ct);

        var contexte = _contextes.Lignes.ShouldHaveSingleItem();
        contexte.Sujet.ShouldBe(SujetLinguistique.Affilie);
        contexte.SujetId.ShouldBe(affilie);
        contexte.Regime.ShouldBe(RegimeLinguistique.BruxellesCapitale);
        contexte.Langue.ShouldBe(Language.Nl);
        contexte.EvenementDu.ShouldBe(T0);
        _contextes.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task AffilieModifie_plus_recent_remplace_l_etat_connu()
    {
        var affilie = Guid.CreateVersion7();
        await Handler.HandleAsync(new AffilieCree(affilie, "0202239951", "B", "Francais", "Fr") { OccurredAt = T0 }, _ct);

        await Handler.HandleAsync(new AffilieModifie(affilie, "0202239951", "B", "Actif", "Neerlandais", "Nl") { OccurredAt = T0.AddMinutes(5) }, _ct);

        var contexte = _contextes.Lignes.ShouldHaveSingleItem();
        contexte.Regime.ShouldBe(RegimeLinguistique.Neerlandais);
        contexte.Langue.ShouldBe(Language.Nl);
    }

    [Fact]
    public async Task Un_evenement_plus_ancien_que_l_etat_connu_est_ignore()
    {
        var affilie = Guid.CreateVersion7();
        await Handler.HandleAsync(new AffilieModifie(affilie, "0202239951", "B", "Actif", "Neerlandais", "Nl") { OccurredAt = T0.AddMinutes(5) }, _ct);

        // Livraison désordonnée : la création arrive après la modification.
        await Handler.HandleAsync(new AffilieCree(affilie, "0202239951", "B", "Francais", "Fr") { OccurredAt = T0 }, _ct);

        var contexte = _contextes.Lignes.ShouldHaveSingleItem();
        contexte.Regime.ShouldBe(RegimeLinguistique.Neerlandais);
        contexte.EvenementDu.ShouldBe(T0.AddMinutes(5));
        _contextes.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task Un_evenement_d_un_producteur_anterieur_sans_les_champs_facultatifs_ne_change_rien()
    {
        var affilie = Guid.CreateVersion7();
        var personne = Guid.CreateVersion7();

        await Handler.HandleAsync(new AffilieCree(affilie, "0202239951", "B") { OccurredAt = T0 }, _ct);
        await Handler.HandleAsync(new OccupationDebutee(Guid.CreateVersion7(), personne, affilie, new DateOnly(2026, 1, 1)) { OccurredAt = T0 }, _ct);

        _contextes.Lignes.ShouldBeEmpty();
        _contextes.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Une_valeur_inconnue_est_ignoree_sans_effacer_la_valeur_connue()
    {
        var affilie = Guid.CreateVersion7();
        await Handler.HandleAsync(new AffilieCree(affilie, "0202239951", "B", "Allemand", "De") { OccurredAt = T0 }, _ct);

        await Handler.HandleAsync(new AffilieModifie(affilie, "0202239951", "B", "Actif", "Klingon", "7") { OccurredAt = T0.AddMinutes(1) }, _ct);

        var contexte = _contextes.Lignes.ShouldHaveSingleItem();
        contexte.Regime.ShouldBe(RegimeLinguistique.Allemand);
        contexte.Langue.ShouldBe(Language.De);
    }

    [Fact]
    public async Task OccupationDebutee_puis_LangueModifiee_projettent_la_langue_du_travailleur()
    {
        var personne = Guid.CreateVersion7();
        await Handler.HandleAsync(new OccupationDebutee(Guid.CreateVersion7(), personne, Guid.CreateVersion7(), new DateOnly(2026, 1, 1), "fr") { OccurredAt = T0 }, _ct);
        _contextes.Lignes.ShouldHaveSingleItem().Langue.ShouldBe(Language.Fr);

        await Handler.HandleAsync(new LangueModifiee(personne, "Nl") { OccurredAt = T0.AddDays(1) }, _ct);
        // Une nouvelle occupation publiée avant le changement, reçue après, ne rétablit pas l'ancienne langue.
        await Handler.HandleAsync(new OccupationDebutee(Guid.CreateVersion7(), personne, Guid.CreateVersion7(), new DateOnly(2026, 2, 1), "Fr") { OccurredAt = T0.AddHours(1) }, _ct);

        var contexte = _contextes.Lignes.ShouldHaveSingleItem();
        contexte.Sujet.ShouldBe(SujetLinguistique.Personne);
        contexte.Regime.ShouldBeNull();
        contexte.Langue.ShouldBe(Language.Nl);
    }

    [Fact]
    public async Task Un_affilie_et_une_personne_de_meme_identifiant_ne_se_confondent_pas()
    {
        var id = Guid.CreateVersion7();
        await Handler.HandleAsync(new AffilieCree(id, "0202239951", "B", "Francais", "Fr") { OccurredAt = T0 }, _ct);
        await Handler.HandleAsync(new LangueModifiee(id, "De") { OccurredAt = T0 }, _ct);

        _contextes.Lignes.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("Fr", Language.Fr)]
    [InlineData("NL", Language.Nl)]
    [InlineData(" de ", Language.De)]
    [InlineData("1", null)]
    [InlineData("-1", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("Klingon", null)]
    public void Les_codes_sont_lus_sans_tenir_compte_de_la_casse_et_les_valeurs_numeriques_sont_refusees(string? code, Language? attendu) =>
        ProjectionLinguistiqueHandler.Enumeration<Language>(code).ShouldBe(attendu);

    [Fact]
    public async Task Les_instantanes_de_rattrapage_remplissent_la_projection_datee_de_l_etat()
    {
        var affilie = Guid.CreateVersion7();
        var personne = Guid.CreateVersion7();

        await Handler.HandleAsync(new AffilieInstantane(affilie, "0202239951", "B", "Actif", "Allemand", "De", T0) { OccurredAt = T0.AddDays(30) }, _ct);
        await Handler.HandleAsync(new PersonneInstantanee(personne, "Nl", T0), _ct);

        var contexteAffilie = _contextes.Lignes.Single(c => c.Sujet == SujetLinguistique.Affilie);
        contexteAffilie.Regime.ShouldBe(RegimeLinguistique.Allemand);
        contexteAffilie.Langue.ShouldBe(Language.De);
        contexteAffilie.EvenementDu.ShouldBe(T0, "un instantané est daté de l'état chez le producteur, pas de sa republication.");
        _contextes.Lignes.Single(c => c.Sujet == SujetLinguistique.Personne).Langue.ShouldBe(Language.Nl);
    }

    [Fact]
    public async Task Un_instantane_n_ecrase_pas_un_etat_plus_recent_et_son_rejeu_ne_cree_pas_de_doublon()
    {
        var personne = Guid.CreateVersion7();
        await Handler.HandleAsync(new LangueModifiee(personne, "De") { OccurredAt = T0.AddMinutes(10) }, _ct);

        // Instantané lu avant le changement de langue et livré après lui.
        await Handler.HandleAsync(new PersonneInstantanee(personne, "Fr", T0), _ct);
        await Handler.HandleAsync(new PersonneInstantanee(personne, "Fr", T0), _ct);

        var contexte = _contextes.Lignes.ShouldHaveSingleItem();
        contexte.Langue.ShouldBe(Language.De);
        contexte.EvenementDu.ShouldBe(T0.AddMinutes(10));
    }

    private sealed class ContextesEnMemoire : IContexteLinguistiqueRepository, IUnitOfWork
    {
        public List<ContexteLinguistiqueProjete> Lignes { get; } = [];

        public int Saves { get; private set; }

        public Task<ContexteLinguistiqueProjete?> GetAsync(SujetLinguistique sujet, Guid sujetId, CancellationToken cancellationToken) =>
            Task.FromResult(Lignes.SingleOrDefault(c => c.Sujet == sujet && c.SujetId == sujetId));

        public void Add(ContexteLinguistiqueProjete contexte) => Lignes.Add(contexte);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }
}
