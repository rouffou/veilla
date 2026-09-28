using System.Globalization;
using System.Text;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts;
using Sepp.PostesRisques.Domain.Risques;

namespace Sepp.PostesRisques.Application.Risques;

/// <summary>
/// AFF-11 : import du référentiel des risques (annexe I.4-5) et de leurs règles (AFF-12) depuis un fichier CSV
/// séparé par des points-virgules, encodé en UTF-8, avec une ligne d'en-tête. Les lignes commençant par <c>#</c>
/// sont des commentaires. Colonnes :
/// <c>code;categorie;libelle_fr;libelle_nl;libelle_de;libelle_en;reference_legale;type_surveillance;frequence_mois;actes_supplementaires;vaccins;surveillance_prolongee;valide_du</c>.
/// Les listes (actes, vaccins) sont séparées par <c>|</c>. L'import est idempotent (une ligne identique ne change rien)
/// et atomique : la moindre ligne en erreur annule tout l'import.
/// </summary>
public sealed record ImporterRisques(string Contenu, DateOnly ValideDuParDefaut);

public sealed record ImportRisquesDto(int RisquesCrees, int RisquesModifies, int ReglesVersionnees, int LignesInchangees);

public sealed class ImporterRisquesHandler(
    IRisqueRepository repository,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    ICurrentUser currentUser) : ICommandHandler<ImporterRisques, ImportRisquesDto>
{
    public static readonly IReadOnlyList<string> Colonnes =
    [
        "code", "categorie", "libelle_fr", "libelle_nl", "libelle_de", "libelle_en", "reference_legale", "type_surveillance",
        "frequence_mois", "actes_supplementaires", "vaccins", "surveillance_prolongee", "valide_du",
    ];

    private const int ErreursAffichees = 20;

    public async Task<Result<ImportRisquesDto>> HandleAsync(ImporterRisques command, CancellationToken cancellationToken)
    {
        if (Acces.Permission(currentUser, Permissions.ReferentielsAdministrer, "Seul l'administrateur fonctionnel importe le référentiel des risques.") is { } refus)
        {
            return refus;
        }

        var erreurs = new List<string>();
        var lignes = Csv.Lire(command.Contenu, erreurs);
        if (erreurs.Count > 0)
        {
            return Invalide(erreurs);
        }

        if (lignes.Count == 0)
        {
            return Error.Validation("import.vide", "Le fichier ne contient aucun risque.");
        }

        var existants = (await repository.ListAsync(cancellationToken)).ToDictionary(r => r.Code, StringComparer.Ordinal);
        var vus = new HashSet<string>(StringComparer.Ordinal);
        var nouveaux = new List<Risque>();
        var evenements = new List<IntegrationEvent>();
        int crees = 0, modifies = 0, regles = 0, inchanges = 0;

        foreach (var (numero, champs) in lignes)
        {
            try
            {
                var code = Risque.NormaliserCode(champs["code"]);
                if (!vus.Add(code))
                {
                    throw new DomainException($"le code {code} apparaît plusieurs fois dans le fichier.");
                }

                var categorie = Enumeration<CategorieRisque>(champs["categorie"], "categorie");
                var libelle = new LocalizedLabel(champs["libelle_fr"], champs["libelle_nl"], champs["libelle_de"], champs["libelle_en"]);
                var type = Enumeration<TypeSurveillance>(champs["type_surveillance"], "type_surveillance");
                var frequence = Entier(champs["frequence_mois"]);
                var valideDu = Date(champs["valide_du"]) ?? command.ValideDuParDefaut;
                var prolongee = Booleen(champs["surveillance_prolongee"]);

                var changement = false;
                if (!existants.TryGetValue(code, out var risque))
                {
                    risque = Risque.Creer(code, categorie, libelle, champs["reference_legale"]);
                    nouveaux.Add(risque);
                    existants[code] = risque;
                    crees++;
                    changement = true;
                }
                else if (risque.MettreAJour(categorie, libelle, champs["reference_legale"]))
                {
                    modifies++;
                    changement = true;
                }

                if (risque.DefinirRegle(type, frequence, Liste(champs["actes_supplementaires"]), Liste(champs["vaccins"]), prolongee, valideDu) is { } regle)
                {
                    evenements.Add(Evenements.RegleModifiee(risque, regle));
                    regles++;
                    changement = true;
                }

                if (!changement)
                {
                    inchanges++;
                }
            }
            catch (DomainException ex)
            {
                erreurs.Add($"ligne {numero} : {ex.Message}");
            }
        }

        if (erreurs.Count > 0)
        {
            return Invalide(erreurs);
        }

        // Rien n'est ajouté au dépôt ni à l'outbox avant que toutes les lignes soient valides (import atomique).
        nouveaux.ForEach(repository.Add);
        evenements.ForEach(outbox.Add);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new ImportRisquesDto(crees, modifies, regles, inchanges);
    }

    private static Error Invalide(List<string> erreurs) =>
        Error.Validation(
            "import.invalide",
            $"Import refusé, aucune ligne n'a été enregistrée ({erreurs.Count} erreur(s)) : "
            + string.Join(" ; ", erreurs.Take(ErreursAffichees))
            + (erreurs.Count > ErreursAffichees ? " ; …" : string.Empty));

    private static TEnum Enumeration<TEnum>(string valeur, string colonne)
        where TEnum : struct, Enum =>
        Enumerations.TryParse<TEnum>(valeur, out var resultat)
            ? resultat
            : throw new DomainException($"{colonne} « {valeur} » inconnu (valeurs admises : {string.Join(", ", Enum.GetNames<TEnum>())}).");

    private static int? Entier(string valeur) =>
        string.IsNullOrWhiteSpace(valeur) ? null
        : int.TryParse(valeur.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n
        : throw new DomainException($"frequence_mois « {valeur} » n'est pas un nombre entier.");

    private static DateOnly? Date(string valeur) =>
        string.IsNullOrWhiteSpace(valeur) ? null
        : DateOnly.TryParseExact(valeur.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d
        : throw new DomainException($"valide_du « {valeur} » : date attendue au format AAAA-MM-JJ.");

    private static bool Booleen(string valeur) => valeur.Trim().ToUpperInvariant() switch
    {
        "" or "NON" or "NEE" or "NEIN" or "NO" or "FALSE" or "0" => false,
        "OUI" or "JA" or "YES" or "TRUE" or "1" => true,
        _ => throw new DomainException($"surveillance_prolongee « {valeur} » : attendu oui ou non."),
    };

    private static string[] Liste(string valeur) =>
        valeur.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Lecteur CSV minimal (RFC 4180 : champs entre guillemets, guillemets doublés), séparateur « ; ».</summary>
    internal static class Csv
    {
        public static List<(int Numero, Dictionary<string, string> Champs)> Lire(string contenu, List<string> erreurs)
        {
            var resultat = new List<(int, Dictionary<string, string>)>();
            string[]? entete = null;
            var numero = 0;
            foreach (var brute in (contenu ?? string.Empty).TrimStart('﻿').Split('\n'))
            {
                numero++;
                var ligne = brute.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(ligne) || ligne.TrimStart().StartsWith('#'))
                {
                    continue;
                }

                var champs = Decouper(ligne);
                if (champs is null)
                {
                    erreurs.Add($"ligne {numero} : guillemets non fermés.");
                    continue;
                }

                if (entete is null)
                {
                    entete = champs.Select(c => c.Trim().ToLowerInvariant()).ToArray();
                    var manquantes = Colonnes.Except(entete, StringComparer.Ordinal).ToList();
                    if (manquantes.Count > 0)
                    {
                        erreurs.Add($"en-tête : colonne(s) manquante(s) {string.Join(", ", manquantes)}.");
                        return resultat;
                    }

                    continue;
                }

                if (champs.Count != entete.Length)
                {
                    erreurs.Add($"ligne {numero} : {champs.Count} champ(s) au lieu de {entete.Length}.");
                    continue;
                }

                resultat.Add((numero, entete.Zip(champs).ToDictionary(p => p.First, p => p.Second.Trim(), StringComparer.Ordinal)));
            }

            if (entete is null)
            {
                erreurs.Add("en-tête absent.");
            }

            return resultat;
        }

        private static List<string>? Decouper(string ligne)
        {
            var champs = new List<string>();
            var courant = new StringBuilder();
            var entreGuillemets = false;
            for (var i = 0; i < ligne.Length; i++)
            {
                var c = ligne[i];
                if (entreGuillemets)
                {
                    if (c == '"' && i + 1 < ligne.Length && ligne[i + 1] == '"')
                    {
                        courant.Append('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        entreGuillemets = false;
                    }
                    else
                    {
                        courant.Append(c);
                    }
                }
                else if (c == '"')
                {
                    entreGuillemets = true;
                }
                else if (c == ';')
                {
                    champs.Add(courant.ToString());
                    courant.Clear();
                }
                else
                {
                    courant.Append(c);
                }
            }

            if (entreGuillemets)
            {
                return null;
            }

            champs.Add(courant.ToString());
            return champs;
        }
    }
}
