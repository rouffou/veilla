using System.Collections.Concurrent;

using Sepp.Communications.Application;
using Sepp.Communications.Domain.Messages;

namespace Sepp.Communications.Adapters.Envois;

/// <summary>Envoi enregistré par un simulateur : ce qui serait parti, sans aucune coordonnée du destinataire.</summary>
public sealed record EnvoiSimule(Canal Canal, Guid MessageId, Guid DestinataireId, bool Recommande, string Sujet, string Corps, DateTimeOffset Date);

/// <summary>
/// Boîte d'envoi commune des simulateurs de canaux (développement, tests) : consultable pour vérifier ce qui a été
/// « envoyé », et programmable pour simuler des pannes (reprises, abandon). Aucun envoi réel, aucune valeur probante.
/// </summary>
public sealed class BoiteEnvoiSimulee
{
    private readonly ConcurrentQueue<EnvoiSimule> _envoyes = new();
    private readonly ConcurrentDictionary<Canal, ConcurrentQueue<ErreurEnvoiException>> _pannes = new();

    public IReadOnlyList<EnvoiSimule> Envoyes => [.. _envoyes];

    public void Enregistrer(EnvoiSimule envoi) => _envoyes.Enqueue(envoi);

    /// <summary>Les <paramref name="fois"/> prochains envois par ce canal échouent avec ce code.</summary>
    public void ProgrammerEchec(Canal canal, string code, bool definitif, int fois = 1)
    {
        var file = _pannes.GetOrAdd(canal, _ => new ConcurrentQueue<ErreurEnvoiException>());
        for (var i = 0; i < fois; i++)
        {
            file.Enqueue(new ErreurEnvoiException(code, definitif));
        }
    }

    internal ErreurEnvoiException? ProchaineErreur(Canal canal) =>
        _pannes.TryGetValue(canal, out var file) && file.TryDequeue(out var erreur) ? erreur : null;

    public void Vider()
    {
        _envoyes.Clear();
        _pannes.Clear();
    }
}

/// <summary>Base des simulateurs : mêmes préconditions que le canal réel (coordonnée présente), panne programmable, trace en mémoire.</summary>
public abstract class CanalSimule(Canal canal, string typePreuve, BoiteEnvoiSimulee boite, TimeProvider horloge) : ICanalEnvoi
{
    public Canal Canal { get; } = canal;

    public Task<ResultatEnvoi> EnvoyerAsync(Envoi envoi, CancellationToken cancellationToken)
    {
        if (boite.ProchaineErreur(Canal) is { } panne)
        {
            throw panne;
        }

        VerifierCoordonnees(envoi.Destinataire);
        var date = horloge.GetUtcNow();
        boite.Enregistrer(new EnvoiSimule(Canal, envoi.MessageId, envoi.Destinataire.Id, envoi.Recommande, envoi.Sujet, envoi.Corps, date));
        var type = envoi.Recommande && Canal == Canal.Courrier ? "recommande-papier-simule" : typePreuve;
        return Task.FromResult(new ResultatEnvoi(type, $"SIM-{Canal}-{envoi.MessageId:N}", date));
    }

    protected virtual void VerifierCoordonnees(Destinataire destinataire)
    {
    }

    protected static void Exiger(bool present, string code)
    {
        if (!present)
        {
            throw new ErreurEnvoiException(code, definitive: true);
        }
    }
}

public sealed class PortailNotificationsSimule(BoiteEnvoiSimulee boite, TimeProvider horloge)
    : CanalSimule(Canal.Portail, "depot-portail-simule", boite, horloge), IPortailNotifications;

public sealed class EnvoiEmailSimule(BoiteEnvoiSimulee boite, TimeProvider horloge)
    : CanalSimule(Canal.Email, "accuse-depot-smtp-simule", boite, horloge), IEnvoiEmail
{
    protected override void VerifierCoordonnees(Destinataire destinataire) => Exiger(!string.IsNullOrWhiteSpace(destinataire.Email), "adresse-email-absente");
}

public sealed class EnvoiSmsSimule(BoiteEnvoiSimulee boite, TimeProvider horloge)
    : CanalSimule(Canal.Sms, "accuse-sms-simule", boite, horloge), IEnvoiSms
{
    protected override void VerifierCoordonnees(Destinataire destinataire) => Exiger(!string.IsNullOrWhiteSpace(destinataire.Telephone), "telephone-absent");
}

public sealed class EnvoiCourrierSimule(BoiteEnvoiSimulee boite, TimeProvider horloge)
    : CanalSimule(Canal.Courrier, "bordereau-depot-postal-simule", boite, horloge), IEnvoiCourrier
{
    protected override void VerifierCoordonnees(Destinataire destinataire) => Exiger(destinataire.Adresse is not null, "adresse-postale-absente");
}

public sealed class EnvoiRecommandeElectroniqueSimule(BoiteEnvoiSimulee boite, TimeProvider horloge)
    : CanalSimule(Canal.RecommandeElectronique, "accuse-recommande-electronique-simule", boite, horloge), IEnvoiRecommandeElectronique
{
    protected override void VerifierCoordonnees(Destinataire destinataire) =>
        Exiger(!string.IsNullOrWhiteSpace(destinataire.Email) || !string.IsNullOrWhiteSpace(destinataire.EBoxEntreprise) || !string.IsNullOrWhiteSpace(destinataire.EBoxCitoyen),
            "adresse-recommande-absente");
}

public sealed class EBoxEntrepriseSimulee(BoiteEnvoiSimulee boite, TimeProvider horloge)
    : CanalSimule(Canal.EBoxEntreprise, "reference-ebox-entreprise-simulee", boite, horloge), IEBoxEntreprise
{
    protected override void VerifierCoordonnees(Destinataire destinataire) => Exiger(!string.IsNullOrWhiteSpace(destinataire.EBoxEntreprise), "ebox-entreprise-absente");
}

public sealed class EBoxCitoyenSimulee(BoiteEnvoiSimulee boite, TimeProvider horloge)
    : CanalSimule(Canal.EBoxCitoyen, "reference-ebox-citoyen-simulee", boite, horloge), IEBoxCitoyen
{
    protected override void VerifierCoordonnees(Destinataire destinataire) => Exiger(!string.IsNullOrWhiteSpace(destinataire.EBoxCitoyen), "ebox-citoyen-absente");
}
