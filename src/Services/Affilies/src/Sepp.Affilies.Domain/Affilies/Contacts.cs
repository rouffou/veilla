using Sepp.BuildingBlocks.Domain;

namespace Sepp.Affilies.Domain.Affilies;

/// <summary>Rôle d'un contact chez l'affilié (AFF-03).</summary>
public enum RoleContact
{
    PersonneDeContact,
    ConseillerPreventionInterne,
    MembreComitePpt,
    DelegationSyndicale,
    PersonneDeConfiance,
}

/// <summary>Coordonnées d'un contact, validées.</summary>
public sealed record DonneesContact
{
    public DonneesContact(string nom, string? fonction, RoleContact role, string? email, string? telephone)
    {
        Nom = Texte.Obligatoire(nom, "Le nom du contact", 200);
        Fonction = Texte.Facultatif(fonction, "La fonction", 200);
        Role = Enum.IsDefined(role) ? role : throw new DomainException($"Rôle de contact inconnu : {role}.");
        Email = Texte.Email(email);
        Telephone = Texte.Facultatif(telephone, "Le téléphone", 30);
        if (Telephone is not null && !Telephone.All(c => char.IsAsciiDigit(c) || c is '+' or ' ' or '.' or '/' or '-' or '(' or ')'))
        {
            throw new DomainException($"Numéro de téléphone invalide : '{telephone}'.");
        }
    }

    public string Nom { get; }

    public string? Fonction { get; }

    public RoleContact Role { get; }

    public string? Email { get; }

    public string? Telephone { get; }
}

/// <summary>
/// AFF-03 — Contacts de l'affilié avec rôle et période de validité. Un contact n'est jamais modifié en place :
/// la ligne courante est clôturée et une nouvelle ligne prend effet (DAT-04).
/// </summary>
public sealed partial class Affilie
{
    public Contact AjouterContact(DonneesContact donnees, DateOnly valideDu)
    {
        VerifierModifiable();
        var contact = new Contact(NewId(), donnees, new Validity(valideDu));
        _contacts.Add(contact);
        IncrementerVersion();
        return contact;
    }

    /// <summary>Nouvelles coordonnées ou nouveau rôle à partir d'une date : renvoie la nouvelle ligne.</summary>
    public Contact ModifierContact(Guid contactId, DonneesContact donnees, DateOnly aPartirDu)
    {
        VerifierModifiable();
        var courant = ContactEnVigueur(contactId);
        courant.Cloturer(Cloturer(courant.Validite, aPartirDu, "Le contact"));
        var nouveau = new Contact(NewId(), donnees, new Validity(aPartirDu));
        _contacts.Add(nouveau);
        IncrementerVersion();
        return nouveau;
    }

    /// <summary>Met fin au rôle d'un contact (fin exclusive).</summary>
    public void TerminerContact(Guid contactId, DateOnly fin)
    {
        VerifierModifiable();
        var contact = ContactEnVigueur(contactId);
        contact.Cloturer(Cloturer(contact.Validite, fin, "Le contact"));
        IncrementerVersion();
    }

    /// <summary>Contacts valides à une date.</summary>
    public IEnumerable<Contact> ContactsAu(DateOnly date) => _contacts.Where(c => c.Validite.Contains(date));

    private Contact ContactEnVigueur(Guid contactId) =>
        _contacts.SingleOrDefault(c => c.Id == contactId)
        ?? throw new ElementIntrouvableException($"Contact {contactId} inconnu pour cet affilié.");
}

public sealed class Contact : Entity
{
    private Contact()
    {
    }

    internal Contact(Guid id, DonneesContact donnees, Validity validite) : base(id)
    {
        Nom = donnees.Nom;
        Fonction = donnees.Fonction;
        Role = donnees.Role;
        Email = donnees.Email;
        Telephone = donnees.Telephone;
        Validite = validite;
    }

    public string Nom { get; private set; } = string.Empty;

    public string? Fonction { get; private set; }

    public RoleContact Role { get; private set; }

    public string? Email { get; private set; }

    public string? Telephone { get; private set; }

    public Validity Validite { get; private set; }

    internal void Cloturer(Validity validite) => Validite = validite;
}
