using System.Net.Sockets;

using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;

using Microsoft.Extensions.Options;

using MimeKit;

using Sepp.Communications.Application;
using Sepp.Communications.Domain.Messages;

namespace Sepp.Communications.Adapters.Envois;

/// <summary>Configuration <c>Communications:Smtp</c> : serveur SMTP de l'envoi réel des e-mails.</summary>
public sealed class OptionsSmtp
{
    public string? Hote { get; set; }

    public int Port { get; set; } = 587;

    /// <summary><c>Auto</c>, <c>None</c>, <c>SslOnConnect</c>, <c>StartTls</c> ou <c>StartTlsWhenAvailable</c> ; <c>StartTls</c> par défaut.</summary>
    public string Securite { get; set; } = "StartTls";

    public string? Utilisateur { get; set; }

    /// <summary>Mot de passe SMTP : Key Vault en Azure, secrets utilisateur en local, jamais dans le dépôt.</summary>
    public string? MotDePasse { get; set; }

    public string? Expediteur { get; set; }

    public string NomExpediteur { get; set; } = "SEPP";

    /// <summary>Domaine de l'en-tête Message-ID : l'identifiant du message permet au prestataire de dédupliquer.</summary>
    public string DomaineMessageId { get; set; } = "sepp.invalid";
}

/// <summary>
/// Envoi réel des e-mails par SMTP (MailKit, licence MIT). Le message ne contient que la notification générique et le
/// lien (DOC-03), construite par <see cref="Gabarits"/>. La preuve est la réponse du serveur au dépôt.
/// </summary>
public sealed class EnvoiEmailSmtp(IOptions<OptionsSmtp> options, TimeProvider horloge) : IEnvoiEmail
{
    public Canal Canal => Canal.Email;

    public async Task<ResultatEnvoi> EnvoyerAsync(Envoi envoi, CancellationToken cancellationToken)
    {
        var smtp = options.Value;
        if (string.IsNullOrWhiteSpace(smtp.Hote) || string.IsNullOrWhiteSpace(smtp.Expediteur))
        {
            throw new ErreurEnvoiException("smtp-non-configure", definitive: false);
        }

        if (!MailboxAddress.TryParse(envoi.Destinataire.Email ?? string.Empty, out var destinataire))
        {
            throw new ErreurEnvoiException("adresse-email-invalide", definitive: true);
        }

        var message = new MimeMessage
        {
            MessageId = $"{envoi.MessageId:N}@{smtp.DomaineMessageId}",
            Subject = envoi.Sujet,
            Body = new TextPart("plain") { Text = envoi.Corps },
        };
        message.From.Add(new MailboxAddress(smtp.NomExpediteur, smtp.Expediteur));
        message.To.Add(destinataire);
        message.Headers.Add("Auto-Submitted", "auto-generated");

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(smtp.Hote, smtp.Port, Securite(smtp.Securite), cancellationToken);
            if (!string.IsNullOrWhiteSpace(smtp.Utilisateur))
            {
                await client.AuthenticateAsync(smtp.Utilisateur, smtp.MotDePasse ?? string.Empty, cancellationToken);
            }

            var reponse = await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
            return new ResultatEnvoi("accuse-depot-smtp", $"{message.MessageId} ; {reponse}", horloge.GetUtcNow());
        }
        catch (SmtpCommandException ex) when ((int)ex.StatusCode >= 500 && ex.ErrorCode is SmtpErrorCode.RecipientNotAccepted or SmtpErrorCode.SenderNotAccepted or SmtpErrorCode.MessageNotAccepted)
        {
            throw new ErreurEnvoiException($"smtp-refus-{(int)ex.StatusCode}", definitive: true, ex);
        }
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException or ProtocolException or AuthenticationException or IOException or SocketException or TimeoutException)
        {
            throw new ErreurEnvoiException($"smtp-indisponible:{ex.GetType().Name}", definitive: false, ex);
        }
    }

    private static SecureSocketOptions Securite(string valeur) =>
        Enum.TryParse<SecureSocketOptions>(valeur, ignoreCase: true, out var option)
            ? option
            : throw new InvalidOperationException($"Communications:Smtp:Securite « {valeur} » inconnu (Auto, None, SslOnConnect, StartTls, StartTlsWhenAvailable).");
}

/// <summary>
/// Notification dans l'espace sécurisé : le message du journal est la notification, consultée par le portail
/// (<c>GET /api/v1/messages</c> du service). Aucun appel sortant : l'envoi consiste à rendre le message disponible.
/// </summary>
public sealed class PortailNotifications(TimeProvider horloge) : IPortailNotifications
{
    public Canal Canal => Canal.Portail;

    public Task<ResultatEnvoi> EnvoyerAsync(Envoi envoi, CancellationToken cancellationToken) =>
        Task.FromResult(new ResultatEnvoi("depot-portail", $"portail:{envoi.MessageId:D}", horloge.GetUtcNow()));
}

/// <summary>
/// Canal pas encore raccordé à un prestataire : l'envoi échoue définitivement avec un code explicite, le message reste
/// visible dans le journal et peut être relancé une fois le canal raccordé (voir README du service).
/// </summary>
public abstract class CanalNonRaccorde(Canal canal) : ICanalEnvoi
{
    public Canal Canal { get; } = canal;

    public Task<ResultatEnvoi> EnvoyerAsync(Envoi envoi, CancellationToken cancellationToken) =>
        throw new ErreurEnvoiException("canal-non-raccorde", definitive: true);
}

/// <summary>SMS réel : opérateur ou passerelle SMS à désigner.</summary>
public sealed class EnvoiSmsNonRaccorde() : CanalNonRaccorde(Canal.Sms), IEnvoiSms;

/// <summary>Courrier réel : prestataire d'impression et de dépôt postal à désigner.</summary>
public sealed class EnvoiCourrierNonRaccorde() : CanalNonRaccorde(Canal.Courrier), IEnvoiCourrier;

/// <summary>Recommandé électronique réel : prestataire qualifié eIDAS à désigner.</summary>
public sealed class EnvoiRecommandeElectroniqueNonRaccorde() : CanalNonRaccorde(Canal.RecommandeElectronique), IEnvoiRecommandeElectronique;

/// <summary>eBox Entreprise réelle : raccordement à l'API eBox Enterprise (certificat d'organisation) à réaliser.</summary>
public sealed class EBoxEntrepriseNonRaccordee() : CanalNonRaccorde(Canal.EBoxEntreprise), IEBoxEntreprise;

/// <summary>eBox Citoyen réelle : raccordement à l'API eBox Citoyen à réaliser.</summary>
public sealed class EBoxCitoyenNonRaccordee() : CanalNonRaccorde(Canal.EBoxCitoyen), IEBoxCitoyen;
