using System.Reflection;

using Sepp.Audit.Domain.Journal;
using Sepp.BuildingBlocks.Domain;

using Shouldly;

namespace Sepp.Audit.Domain.Tests;

public class JournalTests
{
    private static readonly DateTimeOffset Instant = new(2026, 9, 28, 8, 30, 15, TimeSpan.FromHours(2));

    internal static TraceAcces Trace(Zone zone = Zone.Medicale, string? motif = null, bool brisDeGlace = false, DateTimeOffset? horodatage = null) => new(
        Guid.CreateVersion7(), horodatage ?? Instant, zone, "surveillance-medicale", "cpmt-42", "cpmt", ActionAudit.Lecture,
        "dossier-sante", Guid.CreateVersion7(), motif, brisDeGlace);

    internal static List<EntreeAudit> Chaine(int longueur, Zone zone = Zone.Medicale)
    {
        var entrees = new List<EntreeAudit>();
        var precedent = MaillonChaine.Origine;
        for (var i = 0; i < longueur; i++)
        {
            var entree = EntreeAudit.Enregistrer(precedent, Trace(zone));
            entrees.Add(entree);
            precedent = entree.Maillon;
        }

        return entrees;
    }

    private static void Alterer(EntreeAudit entree, string propriete, object? valeur) =>
        typeof(EntreeAudit).GetProperty(propriete, BindingFlags.Public | BindingFlags.Instance)!.SetValue(entree, valeur);

    [Fact]
    public void La_premiere_entree_se_rattache_a_l_origine_et_porte_une_empreinte_sha256()
    {
        var entree = EntreeAudit.Enregistrer(MaillonChaine.Origine, Trace());

        entree.Numero.ShouldBe(1);
        entree.EmpreintePrecedente.ShouldBe(new string('0', 64));
        entree.Empreinte.Length.ShouldBe(64);
        entree.Empreinte.ShouldMatch("^[0-9a-f]{64}$");
        entree.EstIntegre().ShouldBeTrue();
    }

    [Fact]
    public void Chaque_entree_porte_l_empreinte_de_la_precedente()
    {
        var chaine = Chaine(3);

        chaine[1].EmpreintePrecedente.ShouldBe(chaine[0].Empreinte);
        chaine[2].EmpreintePrecedente.ShouldBe(chaine[1].Empreinte);
        chaine.Select(e => e.Numero).ShouldBe([1L, 2L, 3L]);
    }

    [Fact]
    public void L_horodatage_est_tronque_a_la_microseconde_et_exprime_en_utc()
    {
        var entree = EntreeAudit.Enregistrer(MaillonChaine.Origine, Trace(horodatage: Instant.AddTicks(7)));

        entree.Horodatage.Offset.ShouldBe(TimeSpan.Zero);
        (entree.Horodatage.UtcTicks % 10).ShouldBe(0);
        entree.Horodatage.ShouldBe(Instant);
    }

    [Fact]
    public void Un_bris_de_glace_sans_motif_est_refuse() =>
        Should.Throw<DomainException>(() => EntreeAudit.Enregistrer(MaillonChaine.Origine, Trace(motif: "  ", brisDeGlace: true)))
            .Message.ShouldContain("motif");

    [Fact]
    public void Un_bris_de_glace_motive_est_accepte()
    {
        var entree = EntreeAudit.Enregistrer(MaillonChaine.Origine, Trace(motif: " Remplacement du Dr X en congé ", brisDeGlace: true));

        entree.BrisDeGlace.ShouldBeTrue();
        entree.Motif.ShouldBe("Remplacement du Dr X en congé");
    }

    [Fact]
    public void Un_motif_trop_long_est_refuse() =>
        Should.Throw<DomainException>(() => EntreeAudit.Enregistrer(MaillonChaine.Origine, Trace(motif: new string('x', EntreeAudit.LongueurMaximaleMotif + 1))));

    [Fact]
    public void Un_utilisateur_absent_est_refuse() =>
        Should.Throw<DomainException>(() => EntreeAudit.Enregistrer(MaillonChaine.Origine, Trace() with { UtilisateurId = "" }));

    [Theory]
    [InlineData("medicale", true)]
    [InlineData("PSYCHOSOCIALE", true)]
    [InlineData("1", false)]
    [InlineData("inconnue", false)]
    [InlineData("", false)]
    public void Les_codes_de_zone_sont_analyses_strictement(string code, bool valide) =>
        CodesAudit.TryParse<Zone>(code, out _).ShouldBe(valide);

    [Fact]
    public void Une_chaine_intacte_est_verifiee_integralement()
    {
        var verification = new VerificationChaine(Zone.Medicale, MaillonChaine.Origine);

        Chaine(5).All(verification.Verifier).ShouldBeTrue();

        verification.EstIntegre.ShouldBeTrue();
        verification.EntreesVerifiees.ShouldBe(5);
        verification.Dernier.Numero.ShouldBe(5);
    }

    [Fact]
    public void Une_ligne_modifiee_est_detectee()
    {
        var chaine = Chaine(3);
        Alterer(chaine[1], nameof(EntreeAudit.Motif), "motif réécrit");
        var verification = new VerificationChaine(Zone.Medicale, MaillonChaine.Origine);

        chaine.TakeWhile(verification.Verifier).Count().ShouldBe(1);

        verification.Anomalie!.Numero.ShouldBe(2);
        verification.Anomalie.Type.ShouldBe(TypeAnomalie.EmpreinteInvalide);
    }

    [Fact]
    public void Une_ligne_modifiee_puis_rescellee_rompt_le_chainage_suivant()
    {
        var chaine = Chaine(3);
        Alterer(chaine[1], nameof(EntreeAudit.UtilisateurId), "autre-utilisateur");
        Alterer(chaine[1], nameof(EntreeAudit.Empreinte), chaine[1].CalculerEmpreinte());
        var verification = new VerificationChaine(Zone.Medicale, MaillonChaine.Origine);

        chaine.TakeWhile(verification.Verifier).Count().ShouldBe(2);

        verification.Anomalie!.Numero.ShouldBe(3);
        verification.Anomalie.Type.ShouldBe(TypeAnomalie.ChainageRompu);
    }

    [Fact]
    public void Une_ligne_supprimee_est_detectee()
    {
        var chaine = Chaine(3);
        chaine.RemoveAt(1);
        var verification = new VerificationChaine(Zone.Medicale, MaillonChaine.Origine);

        chaine.TakeWhile(verification.Verifier).Count().ShouldBe(1);

        verification.Anomalie!.Numero.ShouldBe(2);
        verification.Anomalie.Type.ShouldBe(TypeAnomalie.EntreeManquante);
    }

    [Fact]
    public void La_verification_reprend_au_sceau_de_la_derniere_purge()
    {
        var chaine = Chaine(4);
        var sceau = new SceauPurge(Zone.Medicale, chaine[1].Numero, chaine[1].Empreinte, 2, Instant);
        var verification = new VerificationChaine(Zone.Medicale, sceau.Maillon);

        chaine.Skip(2).All(verification.Verifier).ShouldBeTrue();
        verification.EntreesVerifiees.ShouldBe(2);
    }

    [Fact]
    public void La_conservation_est_d_au_moins_dix_ans()
    {
        Should.Throw<DomainException>(() => PolitiqueConservation.LimitePurge(Instant, 9));
        PolitiqueConservation.LimitePurge(Instant, 12).ShouldBe(Instant.AddYears(-12));
    }
}
