using Sepp.Bff.Employeur.Aval;

namespace Sepp.Bff.Employeur.Tests;

/// <summary>Données fictives (NF-14) des réponses des services aval.</summary>
internal static class Jeu
{
    public static readonly DateOnly Aujourdhui = new(2026, 10, 5);

    public static TravailleurAval Travailleur(string nom, string prenom) =>
        new(Guid.CreateVersion7(), nom, prenom, new DateOnly(1990, 1, 1));

    public static PosteAval Poste(Guid affilieId, string intitule, string statut = "Actif", params string[] risques) =>
        new(Guid.CreateVersion7(), affilieId, intitule, null, null, statut,
            risques.Select(r => new LienRisqueAval(r, "Moyen", new DateOnly(2026, 1, 15), null)).ToList());

    public static ListeNominativeAval Liste(Guid affilieId, string type, int version, params LigneListeAval[] lignes) =>
        new(Guid.CreateVersion7(), affilieId, type, version, new DateOnly(2026, 9, 1), DateTimeOffset.Parse("2026-09-01T08:00:00Z"),
            null, new DateOnly(2031, 9, 1), null, lignes.Length, lignes);

    public static AffilieAval Affilie(Guid id, string denomination) => new(
        id, 1,
        new FicheAval("0123.456.749", denomination, "SRL", "62010", "200", "B", new DateOnly(2020, 1, 1), null, "Fr", "Francophone", "Actif"),
        [
            new UniteEtablissementAval(Guid.CreateVersion7(), "2.123.456.789", "Siège", Adresse(), new DateOnly(2020, 1, 1), null,
            [
                new SiteAval(Guid.CreateVersion7(), "Atelier", Adresse(), new DateOnly(2020, 1, 1), null),
                new SiteAval(Guid.CreateVersion7(), "Ancien dépôt", Adresse(), new DateOnly(2020, 1, 1), new DateOnly(2025, 1, 1)),
            ]),
        ],
        [
            new ContactAval(Guid.CreateVersion7(), "Alice Martin", "RH", "PersonneDeContact", "alice@example.test", null, new DateOnly(2024, 1, 1), null),
            new ContactAval(Guid.CreateVersion7(), "Bob Ancien", null, "PersonneDeContact", null, null, new DateOnly(2020, 1, 1), new DateOnly(2024, 1, 1)),
        ]);

    private static AdresseAval Adresse() => new("Rue de la Loi", "16", null, "1000", "Bruxelles", "BE");
}
