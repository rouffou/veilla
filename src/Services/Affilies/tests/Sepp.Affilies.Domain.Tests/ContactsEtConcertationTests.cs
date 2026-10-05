using Sepp.Affilies.Domain.Affilies;
using Sepp.BuildingBlocks.Domain;

using Shouldly;

namespace Sepp.Affilies.Domain.Tests;

public class ContactsEtConcertationTests
{
    private static readonly DateOnly Debut = new(2024, 1, 1);
    private readonly Affilie _affilie = Exemples.Affilie();

    private static DonneesContact Contact(RoleContact role = RoleContact.PersonneDeContact, string email = "marie@dupont.be") =>
        new("Marie Dupont", "Directrice RH", role, email, "+32 2 123 45 67");

    [Fact]
    public void Un_contact_est_ajoute_avec_role_et_periode()
    {
        var contact = _affilie.AjouterContact(Contact(RoleContact.PersonneDeConfiance), Debut);

        contact.Role.ShouldBe(RoleContact.PersonneDeConfiance);
        contact.Validite.IsOpen.ShouldBeTrue();
        _affilie.ContactsAu(Debut).ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("pas-un-email")]
    [InlineData("a@b")]
    public void Un_email_invalide_est_refuse(string email) =>
        Should.Throw<DomainException>(() => Contact(email: email));

    [Fact]
    public void Modifier_un_contact_cloture_l_ancienne_ligne_et_en_cree_une_nouvelle()
    {
        var ancien = _affilie.AjouterContact(Contact(), Debut);

        var nouveau = _affilie.ModifierContact(ancien.Id, Contact(email: "m.dupont@dupont.be"), new DateOnly(2025, 6, 1));

        ancien.Validite.ValidTo.ShouldBe(new DateOnly(2025, 6, 1));
        nouveau.Email.ShouldBe("m.dupont@dupont.be");
        _affilie.Contacts.Count.ShouldBe(2);
        _affilie.ContactsAu(new DateOnly(2025, 5, 31)).ShouldHaveSingleItem().Id.ShouldBe(ancien.Id);
        _affilie.ContactsAu(new DateOnly(2025, 6, 1)).ShouldHaveSingleItem().Id.ShouldBe(nouveau.Id);
    }

    [Fact]
    public void Un_contact_cloture_ne_peut_plus_etre_modifie()
    {
        var contact = _affilie.AjouterContact(Contact(), Debut);
        _affilie.TerminerContact(contact.Id, new DateOnly(2025, 1, 1));

        Should.Throw<DomainException>(() => _affilie.TerminerContact(contact.Id, new DateOnly(2025, 2, 1)));
        Should.Throw<DomainException>(() => _affilie.ModifierContact(contact.Id, Contact(), new DateOnly(2025, 2, 1)));
    }

    [Fact]
    public void La_fin_d_un_contact_suit_son_debut() =>
        Should.Throw<DomainException>(() => _affilie.TerminerContact(_affilie.AjouterContact(Contact(), Debut).Id, Debut));

    [Fact]
    public void Un_comite_ppt_est_installe_et_ses_reunions_planifiees()
    {
        var comite = _affilie.InstallerOrgane(TypeOrgane.ComitePpt, Debut);
        var ordreDuJour = Guid.CreateVersion7();

        var reunion = _affilie.PlanifierReunion(comite.Id, new DateOnly(2025, 3, 12), ordreDuJour, participationSepp: true);

        _affilie.DisposeDUnComitePptAu(new DateOnly(2025, 1, 1)).ShouldBeTrue();
        _affilie.DisposeDUnComitePptAu(new DateOnly(2023, 1, 1)).ShouldBeFalse();
        reunion.OrdreDuJourDocumentId.ShouldBe(ordreDuJour);
        reunion.ParticipationSepp.ShouldBeTrue();
    }

    [Fact]
    public void Deux_organes_du_meme_type_ne_coexistent_pas()
    {
        _affilie.InstallerOrgane(TypeOrgane.ComitePpt, Debut);

        Should.Throw<DomainException>(() => _affilie.InstallerOrgane(TypeOrgane.ComitePpt, new DateOnly(2025, 1, 1)));
        _affilie.InstallerOrgane(TypeOrgane.DelegationSyndicale, Debut).Type.ShouldBe(TypeOrgane.DelegationSyndicale);
    }

    [Fact]
    public void Une_reunion_a_lieu_pendant_l_existence_de_l_organe_et_a_une_date_unique()
    {
        var comite = _affilie.InstallerOrgane(TypeOrgane.ComitePpt, Debut);
        var reunion = _affilie.PlanifierReunion(comite.Id, new DateOnly(2025, 3, 12), null, false);

        Should.Throw<DomainException>(() => _affilie.PlanifierReunion(comite.Id, new DateOnly(2023, 3, 12), null, false));
        Should.Throw<DomainException>(() => _affilie.PlanifierReunion(comite.Id, new DateOnly(2025, 3, 12), null, false));

        _affilie.ModifierReunion(comite.Id, reunion.Id, new DateOnly(2025, 3, 19), Guid.CreateVersion7(), true);
        reunion.DateReunion.ShouldBe(new DateOnly(2025, 3, 19));
        reunion.ParticipationSepp.ShouldBeTrue();
    }

    [Fact]
    public void Un_organe_ne_peut_pas_etre_dissous_avant_ses_reunions()
    {
        var comite = _affilie.InstallerOrgane(TypeOrgane.ComitePpt, Debut);
        _affilie.PlanifierReunion(comite.Id, new DateOnly(2025, 3, 12), null, false);

        Should.Throw<DomainException>(() => _affilie.DissoudreOrgane(comite.Id, new DateOnly(2025, 1, 1)));
        _affilie.DissoudreOrgane(comite.Id, new DateOnly(2025, 6, 1));
        _affilie.DisposeDUnComitePptAu(new DateOnly(2025, 6, 1)).ShouldBeFalse();
    }
}
