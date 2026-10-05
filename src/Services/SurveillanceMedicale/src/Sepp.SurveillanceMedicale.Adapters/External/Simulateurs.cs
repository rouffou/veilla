using System.Security.Cryptography;
using System.Text;

using Sepp.SurveillanceMedicale.Application;
using Sepp.SurveillanceMedicale.Domain.MaladiesProfessionnelles;

namespace Sepp.SurveillanceMedicale.Adapters.External;

// Simulateurs des prestataires et organismes externes (NF-14 : aucune donnée réelle). Déterministes, sans état réseau.
// Les adaptateurs réels sont hors périmètre et à intégrer :
//  - SAN-32 signature qualifiée : prestataire eID / itsme (signature à distance du CPMT, horodatage qualifié) ;
//  - SAN-70/71 Fedris : format et canal de la déclaration électronique à confirmer avec Fedris ;
//  - SAN-42 canal de transfert entre SEPP / SIPP : eHealthBox ou canal sécurisé à confirmer.
// Sélection par configuration : SurveillanceMedicale:Adaptateurs:<Port> = Simulateur.

/// <summary>SAN-32 : signature simulée ; la référence dérive de l'empreinte du formulaire et du signataire.</summary>
public sealed class SignatureSimulee(TimeProvider horloge) : ISignatureQualifiee
{
    public Task<SignatureObtenue> SignerAsync(DemandeSignature demande, CancellationToken cancellationToken)
    {
        var jeton = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{demande.DecisionId:N}|{demande.SignataireId}|{demande.EmpreinteFormulaire}")))[..24];
        return Task.FromResult(new SignatureObtenue($"SIMULATEUR-SIGNATURE-{jeton}", horloge.GetUtcNow()));
    }
}

/// <summary>
/// SAN-70, SAN-71 : Fedris simulé. Référence déterministe ; le statut évolue selon le dernier chiffre de la
/// référence (pair : reconnue, impair : information demandée) pour exercer le suivi.
/// </summary>
public sealed class FedrisSimule(TimeProvider horloge) : IFedris
{
    public Task<string> DeclarerAsync(DeclarationFedris declaration, CancellationToken cancellationToken) =>
        Task.FromResult($"SIM-FEDRIS-{declaration.DeclarationId.ToString("N")[..12].ToUpperInvariant()}");

    public Task<StatutFedris> ConsulterStatutAsync(string referenceFedris, CancellationToken cancellationToken)
    {
        var dernier = referenceFedris[^1];
        var statut = char.IsAsciiDigit(dernier) && (dernier - '0') % 2 == 0 ? StatutDeclarationMp.Reconnue : StatutDeclarationMp.InformationDemandee;
        return Task.FromResult(new StatutFedris(statut, DateOnly.FromDateTime(horloge.GetUtcNow().UtcDateTime)));
    }
}

/// <summary>SAN-42 : canal de transfert simulé ; renvoie une référence d'envoi dérivée de l'empreinte du paquet.</summary>
public sealed class CanalTransfertSimule : ICanalTransfertDossier
{
    public Task<string> TransmettreAsync(string contrepartie, string paquet, string empreinte, CancellationToken cancellationToken) =>
        Task.FromResult($"SIM-CANAL-{contrepartie}-{empreinte[..16]}");
}
