using System.Text.Json;

using Sepp.BuildingBlocks.Domain;

namespace Sepp.Personnes.Domain.Personnes;

/// <summary>Sexe administratif (source : registre national / BCSS).</summary>
public enum Sexe
{
    Masculin,
    Feminin,
    Inconnu,
}

/// <summary>Canal de communication préféré du travailleur (convocations, documents).</summary>
public enum CanalCommunication
{
    Courrier,
    Email,
    Sms,
    Portail,
}

/// <summary>Adresse postale (objet-valeur).</summary>
public sealed record Adresse
{
    public Adresse(string rue, string numero, string? boite, string codePostal, string localite, string pays = "BE")
    {
        Rue = Requis(rue, "rue", 200);
        Numero = Requis(numero, "numéro", 20);
        Boite = string.IsNullOrWhiteSpace(boite) ? null : Requis(boite, "boîte", 20);
        CodePostal = Requis(codePostal, "code postal", 20);
        Localite = Requis(localite, "localité", 100);
        var codePays = Requis(pays, "pays", 2).ToUpperInvariant();
        if (codePays.Length != 2 || !codePays.All(char.IsAsciiLetterUpper))
        {
            throw new DomainException("Le pays de l'adresse est un code ISO 3166-1 alpha-2 (par ex. BE).");
        }

        Pays = codePays;
    }

    public string Rue { get; }

    public string Numero { get; }

    public string? Boite { get; }

    public string CodePostal { get; }

    public string Localite { get; }

    public string Pays { get; }

    private static string Requis(string? valeur, string champ, int longueurMax)
    {
        if (string.IsNullOrWhiteSpace(valeur) || valeur.Trim().Length > longueurMax)
        {
            throw new DomainException($"Adresse : le champ « {champ} » est obligatoire ({longueurMax} caractères maximum).");
        }

        return valeur.Trim();
    }
}

/// <summary>Identité civile d'un travailleur (source BCSS ou saisie contrôlée).</summary>
public sealed record Identite(string Nom, string Prenom, DateOnly DateNaissance, Sexe Sexe, Language Langue);

/// <summary>Coordonnées de contact et canal préféré.</summary>
public sealed record Coordonnees(Adresse? Adresse, string? Email, string? Telephone, CanalCommunication CanalPrefere);

/// <summary>Données d'une nouvelle occupation (DIMONA, import ou saisie).</summary>
public sealed record NouvelleOccupation(
    Guid AffilieId,
    Guid? AffilieUtilisateurId,
    TypeTravailleur TypeTravailleur,
    TypeContrat TypeContrat,
    DateOnly DateDebut,
    DateOnly? DateFin,
    string? ReferenceDimona);

/// <summary>
/// Travailleur suivi par le SEPP (§4.3, §15.3). Racine d'agrégat : identité, occupations chez les affiliés,
/// affectations aux postes et états particuliers. DAT-06 : le NISS n'est connu que de ce service,
/// les autres ne manipulent que <c>personne_id</c>.
/// </summary>
public sealed class Personne : AggregateRoot
{
    private readonly List<Occupation> _occupations = [];
    private readonly List<EtatParticulier> _etatsParticuliers = [];
    private readonly List<MutationRegistreNational> _mutations = [];

    private Personne()
    {
    }

    private Personne(Guid id, Niss niss, string nissHash) : base(id)
    {
        Niss = niss;
        NissHash = nissHash;
    }

    /// <summary>Chiffré au repos (colonne <c>niss_chiffre</c>, ARC-45).</summary>
    public Niss Niss { get; private set; } = null!;

    /// <summary>Index aveugle (HMAC) permettant la recherche sans NISS en clair (DAT-06).</summary>
    public string NissHash { get; private set; } = string.Empty;

    public string Nom { get; private set; } = string.Empty;

    public string Prenom { get; private set; } = string.Empty;

    public DateOnly DateNaissance { get; private set; }

    public Sexe Sexe { get; private set; }

    public Language Langue { get; private set; }

    public Adresse? Adresse { get; private set; }

    public string? Email { get; private set; }

    public string? Telephone { get; private set; }

    public CanalCommunication CanalPrefere { get; private set; }

    /// <summary>Date du décès communiquée par le registre national ; <c>null</c> tant que la personne n'est pas décédée.</summary>
    public DateOnly? DateDeces { get; private set; }

    /// <summary>Historique des mutations du registre national appliquées (DAT-04).</summary>
    public IReadOnlyList<MutationRegistreNational> Mutations => _mutations.AsReadOnly();

    public IReadOnlyList<Occupation> Occupations => _occupations.AsReadOnly();

    public IReadOnlyList<EtatParticulier> EtatsParticuliers => _etatsParticuliers.AsReadOnly();

    public IEnumerable<Affectation> Affectations => _occupations.SelectMany(o => o.Affectations);

    public static Personne Creer(Niss niss, string nissHash, Identite identite, Coordonnees coordonnees)
    {
        if (string.IsNullOrWhiteSpace(nissHash))
        {
            throw new DomainException("L'index de recherche du NISS est obligatoire.");
        }

        var personne = new Personne(NewId(), niss, nissHash);
        personne.ModifierIdentite(identite);
        personne.ModifierCoordonnees(coordonnees);
        return personne;
    }

    /// <summary>Met à jour l'identité ; la date de naissance doit correspondre à celle encodée dans le NISS.</summary>
    public void ModifierIdentite(Identite identite)
    {
        var nom = Texte(identite.Nom, "nom", 100);
        var prenom = Texte(identite.Prenom, "prénom", 100);
        if (Niss.DateNaissance() is { } dateNiss && dateNiss != identite.DateNaissance)
        {
            throw new DomainException("La date de naissance ne correspond pas à celle encodée dans le NISS.");
        }

        Nom = nom;
        Prenom = prenom;
        DateNaissance = identite.DateNaissance;
        Sexe = identite.Sexe;
        Langue = identite.Langue;
    }

    public void ModifierCoordonnees(Coordonnees coordonnees)
    {
        var email = string.IsNullOrWhiteSpace(coordonnees.Email) ? null : coordonnees.Email.Trim();
        if (email is not null && (email.Length > 254 || email.Count(c => c == '@') != 1 || email.StartsWith('@') || email.EndsWith('@')))
        {
            throw new DomainException("L'adresse e-mail est invalide.");
        }

        var telephone = string.IsNullOrWhiteSpace(coordonnees.Telephone) ? null : coordonnees.Telephone.Trim();
        if (telephone is not null &&
            (telephone.Length > 30 || !telephone.All(c => char.IsAsciiDigit(c) || c is '+' or ' ' or '.' or '/' or '-')))
        {
            throw new DomainException("Le numéro de téléphone est invalide.");
        }

        switch (coordonnees.CanalPrefere)
        {
            case CanalCommunication.Email when email is null:
                throw new DomainException("Le canal préféré « e-mail » exige une adresse e-mail.");
            case CanalCommunication.Sms when telephone is null:
                throw new DomainException("Le canal préféré « SMS » exige un numéro de téléphone.");
            case CanalCommunication.Courrier when coordonnees.Adresse is null:
                throw new DomainException("Le canal préféré « courrier » exige une adresse postale.");
        }

        Adresse = coordonnees.Adresse;
        Email = email;
        Telephone = telephone;
        CanalPrefere = coordonnees.CanalPrefere;
    }

    /// <summary>AFF-20, AFF-21, AFF-23 : enregistre une occupation chez un affilié.</summary>
    /// <remarks>
    /// Après un décès connu, une occupation ne peut pas commencer après la date du décès : elle est refusée
    /// (<see cref="OccupationApresDecesException"/>, code <c>occupation.apres-deces</c>), qu'elle vienne de DIMONA, d'un import ou d'une saisie.
    /// Une occupation commencée avant le décès est clôturée à la date du décès, comme le fait le traitement du décès.
    /// </remarks>
    public Occupation DebuterOccupation(NouvelleOccupation nouvelle)
    {
        if (DateDeces is { } deces)
        {
            if (nouvelle.DateDebut > deces)
            {
                throw new OccupationApresDecesException(deces, nouvelle.DateDebut);
            }

            if (nouvelle.DateFin is null || nouvelle.DateFin > deces)
            {
                nouvelle = nouvelle with { DateFin = deces };
            }
        }

        var occupation = Occupation.Creer(NewId(), nouvelle);
        if (occupation.ReferenceDimona is { } reference && _occupations.Any(o => o.ReferenceDimona == reference))
        {
            throw new DomainException($"La référence DIMONA {reference} est déjà enregistrée.");
        }

        if (_occupations.Any(o => o.AffilieId == occupation.AffilieId && o.AffilieUtilisateurId == occupation.AffilieUtilisateurId && o.Chevauche(occupation)))
        {
            throw new DomainException("Doublon : une occupation chez cet affilié existe déjà sur cette période.");
        }

        _occupations.Add(occupation);
        Raise(new OccupationEnregistree(Id, occupation.Id, occupation.AffilieId, occupation.DateDebut, DateTimeOffset.UtcNow));
        if (occupation.DateFin is { } fin)
        {
            Raise(new OccupationCloturee(Id, occupation.Id, occupation.AffilieId, fin, DateTimeOffset.UtcNow));
        }

        return occupation;
    }

    /// <summary>
    /// AFF-20 : sortie de service (dernier jour travaillé inclus). Les affectations en cours sont clôturées
    /// le lendemain (DAT-04). Idempotent si l'occupation est déjà terminée à la même date.
    /// </summary>
    /// <returns><c>false</c> si l'occupation était déjà terminée à cette date.</returns>
    public bool TerminerOccupation(Guid occupationId, DateOnly dateFin)
    {
        var occupation = OccupationExistante(occupationId);
        if (occupation.DateFin == dateFin)
        {
            return false;
        }

        occupation.Terminer(dateFin);
        CloturerAffectations(occupation, dateFin);
        Raise(new OccupationCloturee(Id, occupation.Id, occupation.AffilieId, dateFin, DateTimeOffset.UtcNow));
        return true;
    }

    private void CloturerAffectations(Occupation occupation, DateOnly dateFin)
    {
        foreach (var affectation in occupation.Affectations.Where(a => a.Validite.ValidTo is not { } fin || fin > dateFin.AddDays(1)))
        {
            affectation.Cloturer(dateFin.AddDays(1));
            Raise(new AffectationHistorisee(Id, affectation.Id, affectation.PosteId, affectation.Validite, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>
    /// AFF-20, AFF-22 : applique une mutation du registre national (changement d'adresse, de nom, de prénom, de langue ou décès).
    /// Idempotent sur la référence de la mutation : un rejeu identique ne change rien. Chaque mutation appliquée est conservée
    /// dans l'historique (DAT-04) mais ne remplace la valeur courante que si sa date d'effet est la plus récente de son type
    /// (à égalité, la dernière reçue l'emporte) : une mutation ancienne reçue tardivement est seulement historisée
    /// (voir <see cref="EstValeurCourante"/>). Un décès échappe à cet ordre. Un décès clôt à la date du décès les occupations encore actives (et leurs affectations).
    /// </summary>
    /// <returns>La mutation, et <c>false</c> si elle avait déjà été appliquée.</returns>
    public (MutationRegistreNational Mutation, bool Appliquee) AppliquerMutation(DemandeMutationRegistreNational demande)
    {
        var reference = demande.Reference?.Trim() ?? string.Empty;
        if (reference.Length is 0 or > 100)
        {
            throw new DomainException("La référence de la mutation est obligatoire (100 caractères maximum).");
        }

        var existante = _mutations.SingleOrDefault(m => m.Reference == reference);
        if (existante is not null)
        {
            if (existante.Type != demande.Type || existante.DateEffet != demande.DateEffet)
            {
                throw new DomainException($"La référence de mutation {reference} est déjà utilisée pour une autre mutation.");
            }

            return (existante, false);
        }

        // Valeur de la mutation, validée avant toute modification de la personne.
        string apres;
        Action appliquer;
        switch (demande.Type)
        {
            case TypeMutationRegistreNational.ChangementAdresse:
                var adresse = demande.Adresse ?? throw new DomainException("Le changement d'adresse exige la nouvelle adresse.");
                apres = JsonSerializer.Serialize(adresse);
                appliquer = () => Adresse = adresse;
                break;
            case TypeMutationRegistreNational.ChangementNom:
                var nom = Texte(demande.Nom, "nom", 100);
                apres = JsonSerializer.Serialize(nom);
                appliquer = () => Nom = nom;
                break;
            case TypeMutationRegistreNational.ChangementPrenom:
                var prenom = Texte(demande.Prenom, "prénom", 100);
                apres = JsonSerializer.Serialize(prenom);
                appliquer = () => Prenom = prenom;
                break;
            case TypeMutationRegistreNational.ChangementLangue:
                if (demande.Langue is not { } langue || !Enum.IsDefined(langue))
                {
                    throw new DomainException("Le changement de langue exige une langue connue.");
                }

                apres = JsonSerializer.Serialize(langue.ToString());
                appliquer = () => Langue = langue;
                break;
            case TypeMutationRegistreNational.Deces:
                // Le décès n'est pas soumis à l'ordre des dates d'effet : sa correction reste manuelle (voir EnregistrerDeces).
                var avantDeces = DateDeces is { } precedente ? JsonSerializer.Serialize(precedente) : null;
                EnregistrerDeces(demande.DateEffet);
                return Historiser(reference, demande, avantDeces, JsonSerializer.Serialize(demande.DateEffet));
            default:
                throw new DomainException("Type de mutation du registre national inconnu.");
        }

        // AFF-20, AFF-22, DAT-04 : la valeur courante est celle de la mutation dont la date d'effet est la plus récente ;
        // à dates d'effet égales, la mutation reçue en dernier l'emporte (ordre de réception, Rang). Une mutation plus
        // ancienne reçue tardivement est historisée sans toucher à la valeur courante.
        var historique = _mutations.Where(m => m.Type == demande.Type).OrderBy(m => m.DateEffet).ThenBy(m => m.Rang).ToList();
        string? avant;
        if (historique.Count == 0 || demande.DateEffet >= historique[^1].DateEffet)
        {
            avant = ValeurCourante(demande.Type);
            appliquer();
        }
        else
        {
            // Valeur en vigueur à la date d'effet de la mutation tardive : le résultat de la mutation qui la précède,
            // à défaut la valeur qui précédait la toute première mutation de ce type.
            avant = historique.LastOrDefault(m => m.DateEffet <= demande.DateEffet)?.Apres ?? historique[0].Avant;
        }

        return Historiser(reference, demande, avant, apres);
    }

    /// <summary>
    /// <c>true</c> si la mutation détermine la valeur courante de son attribut : aucune mutation du même type n'a une date d'effet
    /// plus récente, ni la même date reçue après elle. Toujours <c>true</c> pour un décès.
    /// </summary>
    public bool EstValeurCourante(MutationRegistreNational mutation) =>
        mutation.Type == TypeMutationRegistreNational.Deces
        || !_mutations.Any(m => m.Type == mutation.Type && (m.DateEffet > mutation.DateEffet || (m.DateEffet == mutation.DateEffet && m.Rang > mutation.Rang)));

    private string? ValeurCourante(TypeMutationRegistreNational type) => type switch
    {
        TypeMutationRegistreNational.ChangementAdresse => Adresse is null ? null : JsonSerializer.Serialize(Adresse),
        TypeMutationRegistreNational.ChangementNom => JsonSerializer.Serialize(Nom),
        TypeMutationRegistreNational.ChangementPrenom => JsonSerializer.Serialize(Prenom),
        TypeMutationRegistreNational.ChangementLangue => JsonSerializer.Serialize(Langue.ToString()),
        _ => null,
    };

    private (MutationRegistreNational Mutation, bool Appliquee) Historiser(
        string reference, DemandeMutationRegistreNational demande, string? avant, string? apres)
    {
        var rang = _mutations.Count == 0 ? 1 : _mutations.Max(m => m.Rang) + 1;
        var mutation = new MutationRegistreNational(NewId(), reference, demande.Type, demande.DateEffet, rang, avant, apres);
        _mutations.Add(mutation);
        return (mutation, true);
    }

    private void EnregistrerDeces(DateOnly dateDeces)
    {
        if (DateDeces is { } connue && connue != dateDeces)
        {
            throw new DomainException($"Un décès est déjà enregistré au {connue:yyyy-MM-dd} : la correction d'une date de décès est traitée manuellement.");
        }

        if (dateDeces < DateNaissance)
        {
            throw new DomainException("La date du décès précède la date de naissance.");
        }

        var aClore = _occupations.Where(o => o.DateFin is null || o.DateFin > dateDeces).ToList();
        if (aClore.Any(o => o.DateDebut > dateDeces))
        {
            throw new DomainException("Le décès précède le début d'une occupation : à corriger manuellement.");
        }

        DateDeces = dateDeces;
        foreach (var occupation in aClore)
        {
            occupation.ClotureAu(dateDeces);
            CloturerAffectations(occupation, dateDeces);
            Raise(new OccupationCloturee(Id, occupation.Id, occupation.AffilieId, dateDeces, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>AFF-22 : rattache le travailleur à un poste d'un site pour une période [valideDu, valideJusquAu[.</summary>
    public Affectation Affecter(Guid occupationId, Guid posteId, Guid siteId, DateOnly valideDu, DateOnly? valideJusquAu = null)
    {
        var occupation = OccupationExistante(occupationId);
        var affectation = occupation.Affecter(NewId(), posteId, siteId, valideDu, valideJusquAu);
        Raise(new AffectationHistorisee(Id, affectation.Id, affectation.PosteId, affectation.Validite, DateTimeOffset.UtcNow));
        return affectation;
    }

    /// <summary>AFF-22, DAT-04 : clôture une affectation à partir d'une date (exclusive) ; la ligne reste dans l'historique.</summary>
    /// <returns><c>false</c> si l'affectation était déjà clôturée à cette date.</returns>
    public bool TerminerAffectation(Guid affectationId, DateOnly aPartirDu)
    {
        var affectation = AffectationExistante(affectationId);
        if (affectation.Validite.ValidTo == aPartirDu)
        {
            return false;
        }

        if (!affectation.Validite.IsOpen)
        {
            throw new DomainException($"L'affectation est déjà clôturée au {affectation.Validite.ValidTo:yyyy-MM-dd}.");
        }

        if (aPartirDu <= affectation.Validite.ValidFrom)
        {
            throw new DomainException($"La clôture de l'affectation doit prendre effet après le {affectation.Validite.ValidFrom:yyyy-MM-dd}.");
        }

        affectation.Cloturer(aPartirDu);
        Raise(new AffectationHistorisee(Id, affectation.Id, affectation.PosteId, affectation.Validite, DateTimeOffset.UtcNow));
        return true;
    }

    /// <summary>
    /// AFF-22, DAT-04 : change de poste ou de site à partir d'une date. L'affectation en cours est clôturée,
    /// une nouvelle est créée : on ne réécrit jamais une période passée.
    /// </summary>
    public Affectation ChangerAffectation(Guid affectationId, Guid posteId, Guid siteId, DateOnly aPartirDu)
    {
        var courante = AffectationExistante(affectationId);
        var occupation = _occupations.Single(o => o.Affectations.Contains(courante));
        var finPrevue = courante.Validite.ValidTo;
        TerminerAffectation(affectationId, aPartirDu);
        return Affecter(occupation.Id, posteId, siteId, aPartirDu, finPrevue);
    }

    /// <summary>AFF-23, AFF-24 : déclare un état particulier (grossesse, allaitement, travail de nuit, jeune).</summary>
    public EtatParticulier DeclarerEtatParticulier(TypeEtatParticulier type, DateOnly dateDebut, DateOnly? dateFin, Guid? affilieDeclarantId = null)
    {
        if (type == TypeEtatParticulier.Jeune && dateDebut >= DateNaissance.AddYears(18))
        {
            throw new DomainException("Le statut de jeune travailleur ne s'applique qu'avant 18 ans.");
        }

        if (affilieDeclarantId is { } declarant && !EstRattacheA(declarant))
        {
            throw new DomainException("L'affilié déclarant n'occupe pas cette personne.");
        }

        var etat = new EtatParticulier(NewId(), type, dateDebut, dateFin, affilieDeclarantId);
        if (_etatsParticuliers.Any(e => e.Type == type && e.Chevauche(etat)))
        {
            throw new DomainException("Un état particulier du même type est déjà déclaré sur cette période.");
        }

        _etatsParticuliers.Add(etat);
        Raise(new EtatParticulierEnregistre(Id, etat.Id, etat.Type, etat.DateDebut, etat.DateFin, DateTimeOffset.UtcNow));
        return etat;
    }

    /// <summary>Fin (anticipée ou constatée) d'un état particulier, dernier jour inclus.</summary>
    /// <returns><c>false</c> si l'état se terminait déjà à cette date.</returns>
    public bool TerminerEtatParticulier(Guid etatId, DateOnly dateFin)
    {
        var etat = _etatsParticuliers.SingleOrDefault(e => e.Id == etatId)
                   ?? throw new DomainException("État particulier inconnu pour cette personne.");
        if (etat.DateFin == dateFin)
        {
            return false;
        }

        etat.Terminer(dateFin);
        Raise(new EtatParticulierEnregistre(Id, etat.Id, etat.Type, etat.DateDebut, etat.DateFin, DateTimeOffset.UtcNow));
        return true;
    }

    /// <summary>Le travailleur est-il occupé (ou mis à disposition, pour un intérimaire) chez cet affilié, à une date quelconque ?</summary>
    public bool EstRattacheA(Guid affilieId) =>
        _occupations.Any(o => o.AffilieId == affilieId || o.AffilieUtilisateurId == affilieId);

    private Occupation OccupationExistante(Guid occupationId) =>
        _occupations.SingleOrDefault(o => o.Id == occupationId)
        ?? throw new DomainException("Occupation inconnue pour cette personne.");

    private Affectation AffectationExistante(Guid affectationId) =>
        Affectations.SingleOrDefault(a => a.Id == affectationId)
        ?? throw new DomainException("Affectation inconnue pour cette personne.");

    private static string Texte(string? valeur, string champ, int longueurMax)
    {
        if (string.IsNullOrWhiteSpace(valeur) || valeur.Trim().Length > longueurMax)
        {
            throw new DomainException($"Le {champ} est obligatoire ({longueurMax} caractères maximum).");
        }

        return valeur.Trim();
    }
}
