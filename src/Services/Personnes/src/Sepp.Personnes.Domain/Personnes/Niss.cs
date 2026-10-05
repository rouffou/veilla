using Sepp.BuildingBlocks.Domain;

namespace Sepp.Personnes.Domain.Personnes;

/// <summary>
/// Numéro d'identification à la sécurité sociale (numéro de registre national ou numéro bis), 11 chiffres :
/// date de naissance AAMMJJ, numéro d'ordre (3 chiffres) et chiffre de contrôle modulo 97 (2 chiffres).
/// Pour les personnes nées à partir de 2000, le contrôle est calculé en préfixant les 9 premiers chiffres par « 2 ».
/// DAT-06 : cette valeur n'existe que dans le service Personnes ; elle est chiffrée au repos et n'apparaît
/// jamais dans une représentation textuelle (journaux, messages d'erreur, événements).
/// </summary>
public sealed record Niss
{
    private Niss(string valeur, bool neApres2000)
    {
        Valeur = valeur;
        NeApres2000 = neApres2000;
    }

    /// <summary>Les 11 chiffres, sans séparateur. À ne transmettre qu'au chiffrement et à l'index aveugle.</summary>
    public string Valeur { get; }

    /// <summary>Siècle déduit du chiffre de contrôle (règle du préfixe « 2 »).</summary>
    public bool NeApres2000 { get; }

    /// <summary>Accepte les formes usuelles (<c>85.07.30-033.28</c>, espaces) ; lève une <see cref="DomainException"/> sans révéler la valeur.</summary>
    public static Niss Parse(string? saisie) =>
        TryParse(saisie, out var niss, out var erreur) ? niss : throw new DomainException(erreur);

    public static bool TryParse(string? saisie, out Niss niss, out string erreur)
    {
        niss = null!;
        if (string.IsNullOrWhiteSpace(saisie))
        {
            erreur = "Le NISS est obligatoire.";
            return false;
        }

        if (saisie.Any(c => !char.IsAsciiDigit(c) && c is not ('.' or '-' or ' ')))
        {
            erreur = "Le NISS ne peut contenir que des chiffres (séparateurs « . », « - » et espaces tolérés).";
            return false;
        }

        var chiffres = new string(saisie.Where(char.IsAsciiDigit).ToArray());
        if (chiffres.Length != 11)
        {
            erreur = "Le NISS doit comporter exactement 11 chiffres.";
            return false;
        }

        var corps = long.Parse(chiffres.AsSpan(0, 9), provider: System.Globalization.CultureInfo.InvariantCulture);
        var controle = int.Parse(chiffres.AsSpan(9, 2), provider: System.Globalization.CultureInfo.InvariantCulture);
        if (97 - (corps % 97) == controle)
        {
            niss = new Niss(chiffres, neApres2000: false);
        }
        else if (97 - ((2_000_000_000L + corps) % 97) == controle)
        {
            niss = new Niss(chiffres, neApres2000: true);
        }
        else
        {
            erreur = "Le NISS est invalide (chiffre de contrôle modulo 97 incorrect).";
            return false;
        }

        erreur = string.Empty;
        return true;
    }

    /// <summary>
    /// Date de naissance encodée dans le NISS, ou <c>null</c> si elle n'est pas déterminable
    /// (date inconnue codée 00, numéro bis dont le mois est majoré de 20 ou 40 et la date incomplète).
    /// </summary>
    public DateOnly? DateNaissance()
    {
        var annee = (NeApres2000 ? 2000 : 1900) + int.Parse(Valeur.AsSpan(0, 2), provider: System.Globalization.CultureInfo.InvariantCulture);
        var mois = int.Parse(Valeur.AsSpan(2, 2), provider: System.Globalization.CultureInfo.InvariantCulture);
        var jour = int.Parse(Valeur.AsSpan(4, 2), provider: System.Globalization.CultureInfo.InvariantCulture);
        mois = mois switch
        {
            > 40 => mois - 40,
            > 20 => mois - 20,
            _ => mois,
        };

        if (mois is < 1 or > 12 || jour < 1 || jour > DateTime.DaysInMonth(annee, mois))
        {
            return null;
        }

        return new DateOnly(annee, mois, jour);
    }

    /// <summary>Forme masquée pour l'affichage : seul le chiffre de contrôle reste visible.</summary>
    public string Masque() => $"XX.XX.XX-XXX.{Valeur[9..]}";

    /// <summary>Jamais la valeur : protège les journaux et les messages d'erreur (DAT-06).</summary>
    public override string ToString() => "NISS(masqué)";
}
