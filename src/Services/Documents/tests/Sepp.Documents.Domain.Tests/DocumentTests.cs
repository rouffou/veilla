using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Domain.Commun;
using Sepp.Documents.Domain.Documents;

using Shouldly;

namespace Sepp.Documents.Domain.Tests;

/// <summary>DOC-02, NF-21 : document archivé, publication et signatures.</summary>
public class DocumentTests
{
    private static readonly string Empreinte = new('a', 64);
    private static readonly string EmpreinteStockage = new('b', 64);
    private static readonly DateTimeOffset Maintenant = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    private static DocumentArchive Archive(TypeDestinataire type = TypeDestinataire.Personne, string? empreinte = null, Guid? destinataire = null) => new(
        Guid.CreateVersion7(), "MODELE", 1, Language.Fr, "test", ZoneDocument.Medicale, "surveillance-medicale", "decision", Guid.CreateVersion7(),
        type, destinataire ?? Guid.CreateVersion7(), "travailleur", "cle", "local://x", empreinte ?? Empreinte, EmpreinteStockage, "PDF/A-1a", 10, "k1",
        Maintenant, "simulateur", "jeton");

    [Fact]
    public void Un_document_est_archive_sans_etre_publie()
    {
        var document = Document.Archiver(Document.NouvelIdentifiant(), Archive());

        document.Statut.ShouldBe(StatutDocument.Archive);
        document.Zone.ShouldBe(ZoneDocument.Medicale);
        document.PublieLe.ShouldBeNull();
    }

    [Theory]
    [InlineData("xyz")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public void Une_empreinte_qui_n_est_pas_un_sha256_hexadecimal_minuscule_est_refusee(string empreinte) =>
        Should.Throw<DomainException>(() => Document.Archiver(Document.NouvelIdentifiant(), Archive(empreinte: empreinte)));

    [Fact]
    public void Un_destinataire_obligatoire() =>
        Should.Throw<DomainException>(() => Document.Archiver(Document.NouvelIdentifiant(), Archive(destinataire: Guid.Empty)));

    [Fact]
    public void Un_document_est_publie_une_seule_fois()
    {
        var document = Document.Archiver(Document.NouvelIdentifiant(), Archive());

        document.Publier(Maintenant);

        document.Statut.ShouldBe(StatutDocument.Publie);
        Should.Throw<DomainException>(() => document.Publier(Maintenant));
    }

    [Fact]
    public void L_exemplaire_verse_au_dossier_n_est_jamais_publie() =>
        Should.Throw<DomainException>(() => Document.Archiver(Document.NouvelIdentifiant(), Archive(TypeDestinataire.Dossier)).Publier(Maintenant));

    [Fact]
    public void Une_signature_porte_sur_l_empreinte_et_un_signataire_ne_signe_qu_une_fois()
    {
        var document = Document.Archiver(Document.NouvelIdentifiant(), Archive());

        var signature = document.Signer("medecin", "qualifiee-simulee", Maintenant, "preuve");

        signature.EmpreinteSignee.ShouldBe(Empreinte);
        document.Signatures.ShouldHaveSingleItem();
        Should.Throw<DomainException>(() => document.Signer("medecin", "qualifiee-simulee", Maintenant, "preuve"));
    }

    [Fact]
    public void Les_codes_de_zone_sont_stables_et_reversibles()
    {
        foreach (var zone in Zones.Toutes)
        {
            Zones.Depuis(zone.Code()).ShouldBe(zone);
        }

        Should.Throw<DomainException>(() => Zones.Depuis("autre"));
    }
}
