using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;
using Sepp.Contracts.Integrations;
using Sepp.Integrations.Application.Correspondances;
using Sepp.Integrations.Application.Externe;
using Sepp.Integrations.Domain;
using Sepp.Integrations.Domain.Bce;
using Sepp.Integrations.Domain.Correspondances;
using Sepp.Integrations.Domain.Flux;

namespace Sepp.Integrations.Application.Flux;

/// <summary>Nature des messages journalisés (colonne <c>type_message</c>).</summary>
public static class TypesMessage
{
    public const string EntrepriseBce = "bce.entreprise";
    public const string EntreeDimona = "dimona.entree";
    public const string SortieDimona = "dimona.sortie";
    public const string MutationIdentite = "registre-national.mutation";
}

/// <summary>Sérialisation des charges utiles au format canonique interne.</summary>
public static class ChargesUtiles
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static string Serialiser<T>(T message) => JsonSerializer.Serialize(message, Options);

    public static T Lire<T>(string chargeUtile) =>
        JsonSerializer.Deserialize<T>(chargeUtile, Options) ?? throw new DomainException("Charge utile vide.");

    /// <summary>Empreinte courte d'un contenu, pour une clé d'idempotence qui ne révèle rien du contenu.</summary>
    public static string Empreinte(string contenu) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contenu)))[..32];
}

/// <summary>
/// Traitement d'un échange journalisé : traduction du format canonique vers le service propriétaire (Personnes,
/// Affiliés via événement), puis mise à jour du statut dans le journal (INT-02). Utilisé pour le premier traitement
/// comme pour la relance manuelle : chaque effet est idempotent (référence DIMONA côté Personnes, comparaison des
/// données BCE, correspondances par clé), un rejeu ne duplique donc rien.
/// </summary>
public sealed class TraitementEchanges(
    ICorrespondanceRepository correspondances,
    RegistreCorrespondances registre,
    IEntrepriseBceRepository entreprises,
    IRegistreNational registreNational,
    IPersonnesClient personnes,
    IIntegrationEventOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider horloge)
{
    public async Task TraiterAsync(EchangeFlux echange, CancellationToken cancellationToken)
    {
        echange.DebuterTentative(horloge.GetUtcNow());
        Issue issue;
        try
        {
            issue = echange.TypeMessage switch
            {
                TypesMessage.EntrepriseBce => await TraiterEntrepriseAsync(echange.ChargeUtile!, cancellationToken),
                TypesMessage.EntreeDimona => await TraiterEntreeAsync(echange.ChargeUtile!, cancellationToken),
                TypesMessage.SortieDimona => await TraiterSortieAsync(echange.ChargeUtile!, cancellationToken),
                TypesMessage.MutationIdentite => await TraiterMutationAsync(echange.ChargeUtile!, cancellationToken),
                _ => Issue.Rejet("integrations.type-message-inconnu", $"Type de message inconnu : {echange.TypeMessage}."),
            };
        }
        catch (DomainException ex)
        {
            issue = Issue.Rejet("integrations.donnees-invalides", ex.Message);
        }
        catch (JsonException)
        {
            issue = Issue.Rejet("integrations.format-invalide", "Charge utile illisible au format canonique.");
        }
        catch (NotImplementedException ex)
        {
            issue = Issue.Erreur("integrations.adaptateur-non-implemente", ex.Message);
        }
#pragma warning disable CA1031 // Échec technique d'un adaptateur externe : consigné dans le journal, relançable (INT-02).
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            // Seul le type d'exception est conservé : son message pourrait reprendre des données reçues.
            issue = Issue.Erreur("integrations.erreur-technique", $"Échec technique ({ex.GetType().Name}).");
        }

        var maintenant = horloge.GetUtcNow();
        switch (issue.Statut)
        {
            case StatutEchange.Traite:
                echange.MarquerTraite(issue.Nombre, maintenant);
                break;
            case StatutEchange.Rejete:
                echange.Rejeter(issue.Code!, issue.Message!, maintenant);
                break;
            default:
                echange.MarquerEnErreur(issue.Code!, issue.Message!, maintenant);
                break;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>§12 BCE : mise à jour de la référence locale et publication des données changées vers Affiliés.</summary>
    private async Task<Issue> TraiterEntrepriseAsync(string chargeUtile, CancellationToken cancellationToken)
    {
        var donnees = ChargesUtiles.Lire<DonneesEntreprise>(chargeUtile);
        var numero = NumerosBce.Entreprise(donnees.NumeroBce);
        var maintenant = horloge.GetUtcNow();
        var entreprise = await entreprises.GetParNumeroAsync(numero, cancellationToken);
        bool change;
        if (entreprise is null)
        {
            entreprise = EntrepriseBce.Creer(donnees, maintenant);
            entreprises.Add(entreprise);
            change = true;
        }
        else
        {
            change = entreprise.Actualiser(donnees, maintenant);
        }

        if (change)
        {
            var affilie = await correspondances.GetAsync(TypeIdentifiantExterne.NumeroBce, numero, cancellationToken);
            outbox.Add(new DonneesBceRecues(numero, affilie?.IdentifiantInterne, entreprise.Denomination, entreprise.FormeJuridique,
                entreprise.CodeNace, entreprise.UnitesEtablissement.Select(u => u.Numero).ToList(), entreprise.DateExtraction));
        }

        return Issue.Traite(1 + entreprise.UnitesEtablissement.Count);
    }

    /// <summary>AFF-20 : entrée DIMONA → identité au registre national → occupation dans le service Personnes.</summary>
    private async Task<Issue> TraiterEntreeAsync(string chargeUtile, CancellationToken cancellationToken)
    {
        var declaration = ChargesUtiles.Lire<DeclarationDimona>(chargeUtile);
        var reference = CorrespondanceIdentifiant.Normaliser(TypeIdentifiantExterne.ReferenceDimona, declaration.ReferenceDimona);
        var employeur = await AffilieAsync(declaration.NumeroBceEmployeur, cancellationToken);
        if (employeur is null)
        {
            return Issue.Rejet("integrations.employeur-inconnu",
                $"L'employeur BCE {Formater(declaration.NumeroBceEmployeur)} n'est pas un affilié connu (correspondance absente).");
        }

        Guid? utilisateur = null;
        if (!string.IsNullOrWhiteSpace(declaration.NumeroBceUtilisateur))
        {
            utilisateur = await AffilieAsync(declaration.NumeroBceUtilisateur, cancellationToken);
            if (utilisateur is null)
            {
                return Issue.Rejet("integrations.utilisateur-inconnu",
                    $"L'entreprise utilisatrice BCE {Formater(declaration.NumeroBceUtilisateur)} n'est pas un affilié connu.");
            }
        }

        var identite = await registreNational.ConsulterIdentiteAsync(declaration.Niss, cancellationToken);
        if (identite is null)
        {
            return Issue.Rejet("integrations.identite-introuvable",
                $"Identité introuvable au registre national pour la déclaration {reference}.");
        }

        var resultat = await personnes.EnregistrerEntreeDimonaAsync(new EntreeDimonaPersonnes(
            reference, declaration.Niss, identite, employeur.Value, utilisateur, declaration.TypeTravailleur,
            declaration.TypeContrat, declaration.DateDebut, declaration.DateFin), cancellationToken);
        if (resultat.Issue != IssueAppel.Succes)
        {
            return Issue.Depuis(resultat);
        }

        await registre.DefinirAsync(TypeIdentifiantExterne.ReferenceDimona, reference, TypeObjetInterne.Occupation,
            resultat.Valeur!.OccupationId, cancellationToken);
        return Issue.Traite(1);
    }

    /// <summary>AFF-20 : sortie DIMONA ; une sortie reçue avant son entrée reste en erreur, relançable (Personnes répond 404).</summary>
    private async Task<Issue> TraiterSortieAsync(string chargeUtile, CancellationToken cancellationToken)
    {
        var declaration = ChargesUtiles.Lire<DeclarationDimona>(chargeUtile);
        var reference = CorrespondanceIdentifiant.Normaliser(TypeIdentifiantExterne.ReferenceDimona, declaration.ReferenceDimona);
        if (declaration.DateFin is not { } dateFin)
        {
            return Issue.Rejet("integrations.date-fin-absente", $"La sortie DIMONA {reference} n'a pas de date de fin.");
        }

        var resultat = await personnes.EnregistrerSortieDimonaAsync(reference, dateFin, cancellationToken);
        return resultat.Issue == IssueAppel.Succes ? Issue.Traite(1) : Issue.Depuis(resultat);
    }

    private async Task<Issue> TraiterMutationAsync(string chargeUtile, CancellationToken cancellationToken)
    {
        var mutation = ChargesUtiles.Lire<MutationRegistreNational>(chargeUtile);
        var resultat = await personnes.AppliquerMutationAsync(mutation, cancellationToken);
        return resultat.Issue == IssueAppel.Succes ? Issue.Traite(1) : Issue.Depuis(resultat);
    }

    private async Task<Guid?> AffilieAsync(string numeroBce, CancellationToken cancellationToken) =>
        (await correspondances.GetAsync(TypeIdentifiantExterne.NumeroBce, NumerosBce.Entreprise(numeroBce), cancellationToken))?.IdentifiantInterne;


    private static string Formater(string numeroBce) =>
        NumerosBce.EstEntrepriseValide(numeroBce, out var numero) ? NumerosBce.Formater(numero) : "invalide";

    private sealed record Issue(StatutEchange Statut, int Nombre, string? Code, string? Message)
    {
        public static Issue Traite(int nombre) => new(StatutEchange.Traite, nombre, null, null);

        public static Issue Rejet(string code, string message) => new(StatutEchange.Rejete, 0, code, message);

        public static Issue Erreur(string code, string message) => new(StatutEchange.EnErreur, 0, code, message);

        public static Issue Depuis<T>(ResultatAppel<T> resultat) => resultat.Issue == IssueAppel.Rejet
            ? Rejet(resultat.Code ?? "integrations.rejet", resultat.Message ?? "Refusé par le service destinataire.")
            : Erreur(resultat.Code ?? "integrations.erreur-technique", resultat.Message ?? "Échec technique de l'appel.");
    }
}
