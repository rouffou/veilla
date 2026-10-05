using System.Globalization;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.Personnes.Application.Personnes;
using Sepp.Personnes.Domain.Personnes;

namespace Sepp.Personnes.Application.Imports;

/// <summary>Colonnes du fichier d'import (AFF-21), en-tête obligatoire, ordre libre.</summary>
public static class ColonnesImport
{
    public const string Niss = "niss";
    public const string Nom = "nom";
    public const string Prenom = "prenom";
    public const string DateNaissance = "date_naissance";
    public const string Sexe = "sexe";
    public const string Langue = "langue";
    public const string DateDebut = "date_debut";
    public const string DateFin = "date_fin";
    public const string TypeTravailleur = "type_travailleur";
    public const string TypeContrat = "type_contrat";
    public const string AffilieUtilisateurId = "affilie_utilisateur_id";
    public const string Email = "email";
    public const string Telephone = "telephone";
    public const string CanalPrefere = "canal_prefere";
    public const string Rue = "rue";
    public const string Numero = "numero";
    public const string Boite = "boite";
    public const string CodePostal = "code_postal";
    public const string Localite = "localite";
    public const string Pays = "pays";

    public static readonly IReadOnlyList<string> Obligatoires = [Niss, Nom, Prenom, DateNaissance, Sexe, Langue, DateDebut];

    public static readonly IReadOnlySet<string> Toutes = new HashSet<string>(StringComparer.Ordinal)
    {
        Niss, Nom, Prenom, DateNaissance, Sexe, Langue, DateDebut, DateFin, TypeTravailleur, TypeContrat, AffilieUtilisateurId,
        Email, Telephone, CanalPrefere, Rue, Numero, Boite, CodePostal, Localite, Pays,
    };
}

/// <summary>Ligne de données du fichier (numéro de ligne physique, en-tête = ligne 1) et ses valeurs par colonne.</summary>
public sealed record LigneImport(int Numero, IReadOnlyDictionary<string, string> Valeurs)
{
    public override string ToString() => $"LigneImport {{ Numero = {Numero} }}";
}

/// <summary>AFF-21 : import de travailleurs hors flux DIMONA pour un affilié. En simulation, rien n'est enregistré.</summary>
public sealed record ImporterTravailleurs(Guid AffilieId, IReadOnlyList<string> Colonnes, IReadOnlyList<LigneImport> Lignes, bool Simulation);

public enum StatutLigneImport
{
    PersonneCreee,

    /// <summary>Doublon détecté : la personne existait déjà (même NISS, même identité), l'occupation lui est ajoutée.</summary>
    OccupationAjoutee,
    Erreur,
}

/// <summary>Rapport d'une ligne : jamais le NISS (DAT-06), seulement le numéro de ligne et les erreurs.</summary>
public sealed record LigneRapportDto(int Ligne, StatutLigneImport Statut, Guid? PersonneId, IReadOnlyList<string> Erreurs);

public sealed record RapportImportDto(
    bool Simulation,
    int Total,
    int PersonnesCreees,
    int OccupationsAjoutees,
    int Erreurs,
    IReadOnlyList<LigneRapportDto> Lignes);

public sealed class ImporterTravailleursHandler(
    IPersonneRepository repository,
    EnregistrementTravailleurs enregistrement,
    INissIndex index,
    IUnitOfWork unitOfWork,
    IIntegrationEventOutbox outbox,
    ICurrentUser currentUser,
    PerimetreUtilisateur perimetre) : ICommandHandler<ImporterTravailleurs, RapportImportDto>
{
    public const int LignesMaximum = 5000;

    private static readonly string[] FormatsDate = ["yyyy-MM-dd", "dd/MM/yyyy", "dd-MM-yyyy", "dd.MM.yyyy"];

    public async Task<Result<RapportImportDto>> HandleAsync(ImporterTravailleurs command, CancellationToken cancellationToken)
    {
        if (!currentUser.HasPermission(Permissions.PersonneEcrire))
        {
            return Error.Forbidden("personnes.interdit", "Droits insuffisants pour importer des travailleurs.");
        }

        if (!perimetre.PeutAgirPour(command.AffilieId))
        {
            return Error.Forbidden("personnes.hors-perimetre", "Un employeur n'importe que les travailleurs de son affilié.");
        }

        var manquantes = ColonnesImport.Obligatoires.Except(command.Colonnes, StringComparer.Ordinal).ToList();
        var inconnues = command.Colonnes.Except(ColonnesImport.Toutes, StringComparer.Ordinal).ToList();
        if (manquantes.Count > 0 || inconnues.Count > 0)
        {
            return Error.Validation("import.en-tete-invalide",
                $"En-tête invalide. Colonnes manquantes : {Liste(manquantes)} ; colonnes inconnues : {Liste(inconnues)}.");
        }

        if (command.Lignes.Count == 0 || command.Lignes.Count > LignesMaximum)
        {
            return Error.Validation("import.taille", $"Le fichier doit contenir entre 1 et {LignesMaximum} lignes de données.");
        }

        var rapport = new List<LigneRapportDto>(command.Lignes.Count);
        var vus = new Dictionary<string, int>(StringComparer.Ordinal);
        var modifiees = new List<Personne>();
        foreach (var ligne in command.Lignes)
        {
            var lue = Lire(ligne, command.AffilieId);
            if (lue.Erreurs.Count > 0)
            {
                rapport.Add(new LigneRapportDto(ligne.Numero, StatutLigneImport.Erreur, null, lue.Erreurs));
                continue;
            }

            // AFF-21 : doublon dans le fichier lui-même.
            var hash = index.Calculer(lue.Niss!);
            if (vus.TryGetValue(hash, out var premiere))
            {
                rapport.Add(Erreur(ligne.Numero, $"Doublon : même NISS qu'à la ligne {premiere}."));
                continue;
            }

            vus[hash] = ligne.Numero;

            // AFF-21 : doublon avec une personne déjà connue du SEPP.
            var trouvee = await enregistrement.TrouverOuCreerAsync(lue.Niss!, lue.Identite!, lue.Coordonnees, false, cancellationToken);
            if (!trouvee.IsSuccess)
            {
                rapport.Add(Erreur(ligne.Numero, trouvee.Error!.Message));
                continue;
            }

            var (personne, nouvelle) = trouvee.Value;
            var occupation = Regles.Appliquer("occupation.invalide", () => personne.DebuterOccupation(lue.Occupation!));
            if (!occupation.IsSuccess)
            {
                rapport.Add(Erreur(ligne.Numero, occupation.Error!.Message));
                continue;
            }

            if (nouvelle && !command.Simulation)
            {
                repository.Add(personne);
            }

            modifiees.Add(personne);
            rapport.Add(new LigneRapportDto(
                ligne.Numero,
                nouvelle ? StatutLigneImport.PersonneCreee : StatutLigneImport.OccupationAjoutee,
                command.Simulation && nouvelle ? null : personne.Id,
                []));
        }

        if (!command.Simulation && modifiees.Count > 0)
        {
            foreach (var personne in modifiees)
            {
                EvenementsIntegration.Publier(personne, outbox);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new RapportImportDto(
            command.Simulation,
            rapport.Count,
            rapport.Count(l => l.Statut == StatutLigneImport.PersonneCreee),
            rapport.Count(l => l.Statut == StatutLigneImport.OccupationAjoutee),
            rapport.Count(l => l.Statut == StatutLigneImport.Erreur),
            rapport);
    }

    private static LigneRapportDto Erreur(int numero, string message) => new(numero, StatutLigneImport.Erreur, null, [message]);

    private static string Liste(List<string> colonnes) => colonnes.Count == 0 ? "aucune" : string.Join(", ", colonnes);

    private sealed record LigneLue(Niss? Niss, Identite? Identite, Coordonnees? Coordonnees, NouvelleOccupation? Occupation, List<string> Erreurs);

    /// <summary>Contrôle complet d'une ligne : toutes les erreurs sont rapportées, pas seulement la première.</summary>
    private static LigneLue Lire(LigneImport ligne, Guid affilieId)
    {
        var erreurs = new List<string>();
        string? Valeur(string colonne) =>
            ligne.Valeurs.TryGetValue(colonne, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        Niss? niss = null;
        if (Niss.TryParse(Valeur(ColonnesImport.Niss), out var n, out var erreurNiss))
        {
            niss = n;
        }
        else
        {
            erreurs.Add(erreurNiss);
        }

        var nom = Valeur(ColonnesImport.Nom);
        var prenom = Valeur(ColonnesImport.Prenom);
        if (nom is null)
        {
            erreurs.Add("Le nom est obligatoire.");
        }

        if (prenom is null)
        {
            erreurs.Add("Le prénom est obligatoire.");
        }

        var dateNaissance = Date(Valeur(ColonnesImport.DateNaissance), "date de naissance", true, erreurs);
        var dateDebut = Date(Valeur(ColonnesImport.DateDebut), "date de début", true, erreurs);
        var dateFin = Date(Valeur(ColonnesImport.DateFin), "date de fin", false, erreurs);

        Sexe? sexe = Valeur(ColonnesImport.Sexe)?.ToUpperInvariant() switch
        {
            "M" or "H" or "MASCULIN" => Sexe.Masculin,
            "F" or "V" or "FEMININ" => Sexe.Feminin,
            "X" or "INCONNU" => Sexe.Inconnu,
            _ => null,
        };
        if (sexe is null)
        {
            erreurs.Add("Sexe invalide (attendu : M, F ou X).");
        }

        var langue = Enumere<Language>(Valeur(ColonnesImport.Langue), "langue (fr, nl, de, en)", erreurs);
        var typeTravailleur = Valeur(ColonnesImport.TypeTravailleur) is null
            ? TypeTravailleur.Salarie
            : Enumere<TypeTravailleur>(Valeur(ColonnesImport.TypeTravailleur), "type de travailleur", erreurs);
        var typeContrat = Valeur(ColonnesImport.TypeContrat) is null
            ? ContratParDefaut(typeTravailleur ?? TypeTravailleur.Salarie)
            : Enumere<TypeContrat>(Valeur(ColonnesImport.TypeContrat), "type de contrat", erreurs);

        Guid? utilisateur = null;
        if (Valeur(ColonnesImport.AffilieUtilisateurId) is { } brut)
        {
            if (Guid.TryParse(brut, out var id))
            {
                utilisateur = id;
            }
            else
            {
                erreurs.Add("L'identifiant de l'affilié utilisateur est invalide.");
            }
        }

        Adresse? adresse = null;
        var champsAdresse = new[] { ColonnesImport.Rue, ColonnesImport.Numero, ColonnesImport.CodePostal, ColonnesImport.Localite };
        if (champsAdresse.Any(c => Valeur(c) is not null))
        {
            try
            {
                adresse = new Adresse(Valeur(ColonnesImport.Rue)!, Valeur(ColonnesImport.Numero)!, Valeur(ColonnesImport.Boite),
                    Valeur(ColonnesImport.CodePostal)!, Valeur(ColonnesImport.Localite)!, Valeur(ColonnesImport.Pays) ?? "BE");
            }
            catch (DomainException ex)
            {
                erreurs.Add(ex.Message);
            }
        }

        var email = Valeur(ColonnesImport.Email);
        var telephone = Valeur(ColonnesImport.Telephone);
        var canal = Valeur(ColonnesImport.CanalPrefere) is null
            ? email is not null ? CanalCommunication.Email : adresse is not null ? CanalCommunication.Courrier : CanalCommunication.Portail
            : Enumere<CanalCommunication>(Valeur(ColonnesImport.CanalPrefere), "canal préféré", erreurs);

        if (erreurs.Count > 0)
        {
            return new LigneLue(null, null, null, null, erreurs);
        }

        return new LigneLue(
            niss,
            new Identite(nom!, prenom!, dateNaissance!.Value, sexe!.Value, langue!.Value),
            new Coordonnees(adresse, email, telephone, canal!.Value),
            new NouvelleOccupation(affilieId, utilisateur, typeTravailleur!.Value, typeContrat!.Value, dateDebut!.Value, dateFin, null),
            erreurs);
    }

    private static TypeContrat ContratParDefaut(TypeTravailleur type) => type switch
    {
        TypeTravailleur.Interimaire => TypeContrat.Interim,
        TypeTravailleur.Etudiant => TypeContrat.Etudiant,
        TypeTravailleur.Stagiaire => TypeContrat.Stage,
        TypeTravailleur.Benevole => TypeContrat.Benevolat,
        _ => TypeContrat.DureeIndeterminee,
    };

    private static DateOnly? Date(string? valeur, string libelle, bool obligatoire, List<string> erreurs)
    {
        if (valeur is null)
        {
            if (obligatoire)
            {
                erreurs.Add($"La {libelle} est obligatoire.");
            }

            return null;
        }

        if (DateOnly.TryParseExact(valeur, FormatsDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        erreurs.Add($"La {libelle} est invalide (formats acceptés : AAAA-MM-JJ ou JJ/MM/AAAA).");
        return null;
    }

    private static T? Enumere<T>(string? valeur, string libelle, List<string> erreurs)
        where T : struct, Enum
    {
        if (valeur is not null && !int.TryParse(valeur, out _) && Enum.TryParse<T>(valeur, ignoreCase: true, out var resultat))
        {
            return resultat;
        }

        erreurs.Add($"Valeur invalide pour {libelle}.");
        return null;
    }
}
