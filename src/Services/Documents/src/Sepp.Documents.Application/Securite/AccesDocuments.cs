using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.Documents.Domain.Commun;
using Sepp.Documents.Domain.Documents;

namespace Sepp.Documents.Application.Securite;

/// <summary>
/// Règles d'accès aux documents (§3.3, ARC-41) : un document de la zone médicale n'est lisible qu'avec la permission
/// médicale (<c>dossier-sante:lire</c>), un document psychosocial qu'avec <c>dossier-psy:lire</c>. Seule exception : le
/// travailleur lit les documents publiés qui lui sont adressés (§3.3 « ses propres documents », POR-13). L'employeur ne lit
/// que les documents de la zone standard publiés pour son affilié (claim <c>affilie_id</c>).
/// </summary>
public sealed class AccesDocuments(ICurrentUser utilisateur, IPerimetreExterne perimetre)
{
    public bool EstTravailleur => utilisateur.Roles.Contains(Roles.Travailleur);

    public bool EstEmployeur => utilisateur.Roles.Contains(Roles.Employeur) || utilisateur.Roles.Contains(Roles.Sipp);

    public bool EstExterne => EstTravailleur || EstEmployeur;

    /// <summary>Permission de lecture d'un document d'une zone pour un utilisateur interne.</summary>
    public bool PeutLireZone(ZoneDocument zone) =>
        !EstExterne && utilisateur.HasPermission(Permissions.DocumentsLire) && zone switch
        {
            ZoneDocument.Standard => true,
            ZoneDocument.Medicale => utilisateur.HasPermission(Permissions.DossierSanteLire),
            ZoneDocument.Psychosociale => utilisateur.HasPermission(Permissions.DossierPsyLire),
            _ => false,
        };

    public Error? VerifierLecture(Document document)
    {
        if (!utilisateur.HasPermission(Permissions.DocumentsLire))
        {
            return Interdit();
        }

        if (EstExterne)
        {
            var autorise = document.Statut == StatutDocument.Publie && (
                (EstTravailleur && document.TypeDestinataire == TypeDestinataire.Personne && perimetre.PersonneId == document.DestinataireId) ||
                (EstEmployeur && document.Zone == ZoneDocument.Standard && document.TypeDestinataire == TypeDestinataire.Affilie &&
                 perimetre.Affilies.Contains(document.DestinataireId)));

            // Un externe ne doit pas apprendre l'existence d'un document hors de son périmètre.
            return autorise ? null : Error.NotFound("document.inconnu", "Document inconnu.");
        }

        return PeutLireZone(document.Zone)
            ? null
            : Error.Forbidden("documents.zone-interdite", $"La lecture d'un document de la zone {document.Zone.Code()} exige la permission de cette zone.");
    }

    /// <summary>Génération : permission de génération et droit d'écriture dans la zone du modèle.</summary>
    public Error? VerifierGeneration(ZoneDocument zone)
    {
        var autorise = !EstExterne && utilisateur.HasPermission(Permissions.DocumentsGenerer) && zone switch
        {
            ZoneDocument.Standard => true,
            ZoneDocument.Medicale => utilisateur.HasPermission(Permissions.DossierSanteEcrire),
            ZoneDocument.Psychosociale => utilisateur.HasPermission(Permissions.DossierPsyEcrire),
            _ => false,
        };
        return autorise ? null : Error.Forbidden("documents.generation-interdite", $"Génération d'un document de la zone {zone.Code()} non autorisée.");
    }

    /// <summary>DOC-01 : les modèles médicaux sont validés par le CPMT dirigeant, les psychosociaux par le CPAP dirigeant.</summary>
    public Error? VerifierValidationModele(ZoneDocument zone)
    {
        var permission = zone switch
        {
            ZoneDocument.Medicale => Permissions.DocumentsModeleValiderMedical,
            ZoneDocument.Psychosociale => Permissions.DocumentsModeleValiderPsychosocial,
            _ => Permissions.DocumentsModeleValider,
        };
        return utilisateur.HasPermission(permission)
            ? null
            : Error.Forbidden("documents.validation-interdite", zone switch
            {
                ZoneDocument.Medicale => "Un modèle de la zone médicale est validé par le CPMT dirigeant.",
                ZoneDocument.Psychosociale => "Un modèle de la zone psychosociale est validé par le CPAP dirigeant.",
                _ => "La validation des modèles est réservée à l'administrateur fonctionnel.",
            });
    }

    public Error? VerifierIntegrite(Document document) =>
        utilisateur.HasPermission(Permissions.DocumentsVerifierIntegrite) ? null : VerifierLecture(document);

    private static Error Interdit() => Error.Forbidden("documents.interdit", "Droits insuffisants sur les documents.");
}
