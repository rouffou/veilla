using System.Globalization;

using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.UniversalAccessibility;

using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Application;
using Sepp.Documents.Domain.Fusion;

namespace Sepp.Documents.Adapters.Pdf;

/// <summary>
/// Rendu PDF/A-1a (NF-21) avec PDFsharp (licence MIT) : polices Lato incorporées (SIL Open Font License 1.1), profil de
/// sortie sRGB, métadonnées XMP et document balisé (structure titres/paragraphes/listes, langue). Les tests vérifient les marqueurs
/// PDF/A ; la validation complète par veraPDF reste à passer avant la production (voir README du service).
/// </summary>
public sealed class RenduPdfA : IRenduPdf
{
    public const string FormatProduit = "PDF/A-1a";

    private const double Marge = 56.7; // 2 cm
    private const double TailleCorps = 10.5;
    private const double RetraitPuce = 16;

    public RenduPdfA()
    {
        PolicesIncorporees.Installer();
    }

    public RenduPdf Rendre(DocumentFusionne document, MetadonneesPdf metadonnees)
    {
        using var pdf = new PdfDocument();
        pdf.Info.Title = metadonnees.Titre;
        pdf.Info.Author = metadonnees.Auteur;
        pdf.Info.Subject = metadonnees.Reference;
        pdf.Info.Creator = metadonnees.Auteur;
        pdf.Info.CreationDate = metadonnees.Date.UtcDateTime;
        pdf.SetPdfA();
        var ua = UAManager.ForDocument(pdf);
        ua.SetDocumentLanguage(CodeLangue(metadonnees.Langue));

        var mise = new MiseEnPage(pdf, ua.StructureBuilder, metadonnees);
        mise.Ecrire(document.Blocs);
        mise.Terminer();

        using var flux = new MemoryStream();
        pdf.Save(flux);
        return new RenduPdf(flux.ToArray(), FormatProduit);
    }

    private static string CodeLangue(Language langue) => langue switch
    {
        Language.Nl => "nl-BE",
        Language.De => "de-BE",
        Language.En => "en-GB",
        _ => "fr-BE",
    };

    /// <summary>Mise en page simple : blocs les uns sous les autres, retour à la ligne par mots, pagination automatique.</summary>
    private sealed class MiseEnPage(PdfDocument pdf, StructureBuilder structure, MetadonneesPdf metadonnees)
    {
        private readonly XFont _corps = new(PolicesIncorporees.Famille, TailleCorps);
        private readonly XFont _titre = new(PolicesIncorporees.Famille, 16, XFontStyleEx.Bold);
        private readonly XFont _sousTitre = new(PolicesIncorporees.Famille, 12, XFontStyleEx.Bold);
        private readonly XFont _pied = new(PolicesIncorporees.Famille, 8);
        private XGraphics? _graphique;
        private PdfPage? _page;
        private double _y;
        private int _numero;
        private bool _dansListe;

        public void Ecrire(IReadOnlyList<Bloc> blocs)
        {
            structure.BeginElement(PdfGroupingElementTag.Document);
            NouvellePage();
            foreach (var bloc in blocs)
            {
                if (bloc.Type == TypeBloc.Puce && !_dansListe)
                {
                    structure.BeginElement(PdfBlockLevelElementTag.List);
                    _dansListe = true;
                }
                else if (bloc.Type != TypeBloc.Puce && _dansListe)
                {
                    structure.End();
                    _dansListe = false;
                }

                switch (bloc.Type)
                {
                    case TypeBloc.Titre:
                        Paragraphe(PdfBlockLevelElementTag.Heading1, bloc.Texte, _titre, 0, espaceAvant: 0, espaceApres: 10);
                        break;
                    case TypeBloc.SousTitre:
                        Paragraphe(PdfBlockLevelElementTag.Heading2, bloc.Texte, _sousTitre, 0, espaceAvant: 8, espaceApres: 4);
                        break;
                    case TypeBloc.Puce:
                        Puce(bloc.Texte);
                        break;
                    case TypeBloc.Separateur:
                        Separateur();
                        break;
                    default:
                        Paragraphe(PdfBlockLevelElementTag.Paragraph, bloc.Texte, _corps, 0, espaceAvant: 0, espaceApres: 6);
                        break;
                }
            }

            if (_dansListe)
            {
                structure.End();
            }

            structure.End();
        }

        public void Terminer() => _graphique?.Dispose();

        private double Largeur => _page!.Width.Point - (2 * Marge);

        private double Bas => _page!.Height.Point - Marge - 20;

        private void NouvellePage()
        {
            _graphique?.Dispose();
            _page = pdf.AddPage();
            _page.Size = PdfSharp.PageSize.A4;
            _graphique = XGraphics.FromPdfPage(_page);
            _numero++;
            _y = Marge;

            // Pied de page : élément de mise en page (artefact), hors de la structure logique du document.
            structure.BeginArtifact();
            var pied = string.Create(CultureInfo.InvariantCulture, $"{metadonnees.Reference} — {_numero}");
            _graphique.DrawString(pied, _pied, XBrushes.Gray, new XRect(Marge, _page.Height.Point - Marge, Largeur, 12), XStringFormats.TopRight);
            structure.End();
        }

        private void Paragraphe(PdfBlockLevelElementTag balise, string texte, XFont police, double retrait, double espaceAvant, double espaceApres)
        {
            var hauteur = police.GetHeight() * 1.2;
            var lignes = Couper(texte, police, Largeur - retrait);
            _y += espaceAvant;
            if (_y + hauteur > Bas)
            {
                NouvellePage();
            }

            structure.BeginElement(balise);
            foreach (var ligne in lignes)
            {
                if (_y + hauteur > Bas)
                {
                    structure.End();
                    NouvellePage();
                    structure.BeginElement(balise);
                }

                _graphique!.DrawString(ligne, police, XBrushes.Black, new XRect(Marge + retrait, _y, Largeur - retrait, hauteur), XStringFormats.TopLeft);
                _y += hauteur;
            }

            structure.End();
            _y += espaceApres;
        }

        private void Puce(string texte)
        {
            var hauteur = _corps.GetHeight() * 1.2;
            if (_y + hauteur > Bas)
            {
                NouvellePage();
            }

            structure.BeginElement(PdfBlockLevelElementTag.ListItem);
            structure.BeginElement(PdfBlockLevelElementTag.Label);
            _graphique!.DrawString("•", _corps, XBrushes.Black, new XRect(Marge + 4, _y, RetraitPuce, hauteur), XStringFormats.TopLeft);
            structure.End();
            structure.BeginElement(PdfBlockLevelElementTag.ListBody);
            Paragraphe(PdfBlockLevelElementTag.Paragraph, texte, _corps, RetraitPuce, espaceAvant: 0, espaceApres: 2);
            structure.End();
            structure.End();
        }

        private void Separateur()
        {
            _y += 6;
            if (_y + 12 > Bas)
            {
                NouvellePage();
            }

            structure.BeginArtifact();
            _graphique!.DrawLine(XPens.Gray, Marge, _y, Marge + Largeur, _y);
            structure.End();
            _y += 10;
        }

        private List<string> Couper(string texte, XFont police, double largeur)
        {
            var lignes = new List<string>();
            var courante = string.Empty;
            foreach (var mot in texte.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidat = courante.Length == 0 ? mot : $"{courante} {mot}";
                if (_graphique!.MeasureString(candidat, police).Width <= largeur)
                {
                    courante = candidat;
                    continue;
                }

                if (courante.Length > 0)
                {
                    lignes.Add(courante);
                }

                courante = mot;

                // Mot plus long que la ligne : coupé au caractère.
                while (_graphique.MeasureString(courante, police).Width > largeur && courante.Length > 1)
                {
                    var n = courante.Length - 1;
                    while (n > 1 && _graphique.MeasureString(courante[..n], police).Width > largeur)
                    {
                        n--;
                    }

                    lignes.Add(courante[..n]);
                    courante = courante[n..];
                }
            }

            if (courante.Length > 0 || lignes.Count == 0)
            {
                lignes.Add(courante);
            }

            return lignes;
        }
    }
}

/// <summary>
/// Polices Lato (régulière et grasse) incorporées comme ressources de l'assembly : l'image conteneur (chiseled) ne contient
/// aucune police système, et PDF/A exige l'incorporation des polices. Licence SIL OFL 1.1 (fichier <c>Polices/OFL.txt</c>).
/// </summary>
internal sealed class PolicesIncorporees : IFontResolver
{
    public const string Famille = "Lato";
    private const string Reguliere = "Lato-Regular";
    private const string Grasse = "Lato-Bold";

    private static readonly Lock Verrou = new();

    public static void Installer()
    {
        lock (Verrou)
        {
            if (GlobalFontSettings.FontResolver is not PolicesIncorporees)
            {
                GlobalFontSettings.FontResolver = new PolicesIncorporees();
            }
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(bold ? Grasse : Reguliere);

    public byte[]? GetFont(string faceName)
    {
        using var ressource = typeof(PolicesIncorporees).Assembly.GetManifestResourceStream($"Sepp.Documents.Adapters.Pdf.Polices.{faceName}.ttf")
                              ?? throw new InvalidOperationException($"Police incorporée {faceName} introuvable.");
        using var copie = new MemoryStream();
        ressource.CopyTo(copie);
        return copie.ToArray();
    }
}
