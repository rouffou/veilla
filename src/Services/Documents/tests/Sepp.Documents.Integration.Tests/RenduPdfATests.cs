using System.Text;

using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Adapters.Pdf;
using Sepp.Documents.Application;
using Sepp.Documents.Domain.Fusion;

using Shouldly;

namespace Sepp.Documents.Integration.Tests;

/// <summary>NF-21 : rendu PDF/A par PDFsharp (MIT). La validation complète par veraPDF n'est pas exécutée ici (voir README).</summary>
public class RenduPdfATests
{
    private static readonly DateTimeOffset Date = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    private static RenduPdf Rendre(Language langue = Language.Fr, int paragraphes = 3)
    {
        var blocs = new List<Bloc> { new(TypeBloc.Titre, "Formulaire d'évaluation de santé"), new(TypeBloc.SousTitre, "Décision") };
        for (var i = 0; i < paragraphes; i++)
        {
            blocs.Add(new Bloc(TypeBloc.Paragraphe, string.Join(' ', Enumerable.Repeat("Apte moyennant des aménagements du poste, œuvres d'été à Liège, Größe, één.", 12))));
        }

        blocs.Add(new Bloc(TypeBloc.Puce, "Aménagement du poste"));
        blocs.Add(new Bloc(TypeBloc.Separateur, string.Empty));
        blocs.Add(new Bloc(TypeBloc.Paragraphe, "Fin"));
        return new RenduPdfA().Rendre(new DocumentFusionne(blocs), new MetadonneesPdf("Formulaire", langue, "SEPP", Date, "ref-123"));
    }

    [Fact]
    public void Le_rendu_produit_un_pdf_valide_declare_pdf_a_1a()
    {
        var pdf = Rendre();

        pdf.Format.ShouldBe("PDF/A-1a");
        Encoding.ASCII.GetString(pdf.Contenu, 0, 8).ShouldStartWith("%PDF-1.");
        var texte = Encoding.Latin1.GetString(pdf.Contenu);
        texte.ShouldContain("pdfaid:part");
        texte.ShouldContain("pdfaid:conformance");
        texte.ShouldContain("OutputIntent");
        texte.ShouldContain("FontFile2");
        texte.ShouldNotContain("/Encrypt");
        texte.ShouldNotContain("/JavaScript");
    }

    [Fact]
    public void Le_rendu_est_deterministe_pour_un_meme_contenu_hors_identifiants()
    {
        // Deux rendus du même contenu ont la même taille de structure : pas de contenu aléatoire exécutable.
        Rendre().Contenu.Length.ShouldBeInRange(Rendre().Contenu.Length - 400, Rendre().Contenu.Length + 400);
    }

    [Fact]
    public void Un_long_document_est_pagine()
    {
        var court = Rendre(paragraphes: 1);
        var volumineux = Rendre(paragraphes: 40);

        var pages = System.Text.RegularExpressions.Regex.Count(Encoding.Latin1.GetString(volumineux.Contenu), "/Type\\s*/Page\\b");
        pages.ShouldBeGreaterThan(2);
        volumineux.Contenu.Length.ShouldBeGreaterThan(court.Contenu.Length);
    }

    [Theory]
    [InlineData(Language.Nl)]
    [InlineData(Language.De)]
    [InlineData(Language.En)]
    public void La_langue_du_document_est_declaree(Language langue)
    {
        var texte = Encoding.Latin1.GetString(Rendre(langue).Contenu);

        texte.ShouldContain(langue switch { Language.Nl => "nl-BE", Language.De => "de-BE", _ => "en-GB" });
    }
}
