using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Auditing;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.SurveillanceMedicale.Application.Protocoles;
using Sepp.SurveillanceMedicale.Domain.Dossiers;

namespace Sepp.SurveillanceMedicale.Application.Dossiers;

public sealed record ReponseSaisie(string CodeQuestion, string Valeur);

/// <summary>SAN-22 : questionnaire complété pendant la consultation par le professionnel (ou relu avec le travailleur).</summary>
public sealed record EnregistrerQuestionnaire(Guid DossierId, string ModeleCode, IReadOnlyList<ReponseSaisie> Reponses, Guid? ExamenId)
{
    public override string ToString() => $"EnregistrerQuestionnaire {{ DossierId = {DossierId}, ModeleCode = {ModeleCode} }}";
}

public sealed class EnregistrerQuestionnaireHandler(GardeDossier garde, IProtocolesRepository protocoles, IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<EnregistrerQuestionnaire, Guid>
{
    public async Task<Result<Guid>> HandleAsync(EnregistrerQuestionnaire command, CancellationToken cancellationToken)
    {
        var dossier = await garde.EcrireAsync(command.DossierId, PartiesDossier.Questionnaires, ActionAudit.Creation, cancellationToken);
        if (!dossier.IsSuccess)
        {
            return dossier.Error!;
        }

        var reponses = await Questionnaires.ValiderAsync(protocoles, command.ModeleCode, command.Reponses, cancellationToken);
        if (!reponses.IsSuccess)
        {
            return reponses.Error!;
        }

        var (modele, valeurs) = reponses.Value;
        var resultat = Regles.Appliquer("questionnaire.invalide", () => dossier.Value.EnregistrerQuestionnaire(
            modele.Code, modele.Version, valeurs, SourceQuestionnaire.Consultation, horloge.GetUtcNow(), command.ExamenId).Id);
        if (resultat.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return resultat;
    }
}

/// <summary>
/// SAN-22 : questionnaire rempli à l'avance par le travailleur (portail : claim <c>personne_id</c> du jeton) ou sur la
/// tablette de la salle d'attente (assistant médical). Écriture seule : aucune donnée du dossier n'est renvoyée, et le
/// dossier est ouvert s'il n'existe pas encore (sans CPMT gestionnaire). L'écriture est journalisée.
/// </summary>
public sealed record PreRemplirQuestionnaire(Guid PersonneId, string ModeleCode, IReadOnlyList<ReponseSaisie> Reponses)
{
    public override string ToString() => $"PreRemplirQuestionnaire {{ PersonneId = {PersonneId}, ModeleCode = {ModeleCode} }}";
}

public sealed class PreRemplirQuestionnaireHandler(
    IDossierSanteRepository dossiers, IProtocolesRepository protocoles, GardeDossier garde, ICurrentUser user, IContexteAcces contexte,
    IUnitOfWork unitOfWork, TimeProvider horloge)
    : ICommandHandler<PreRemplirQuestionnaire, Guid>
{
    public async Task<Result<Guid>> HandleAsync(PreRemplirQuestionnaire command, CancellationToken cancellationToken)
    {
        if (!user.HasPermission(Permissions.QuestionnaireSanteRemplir))
        {
            return Error.Forbidden("questionnaire.interdit", "Droits insuffisants pour remplir un questionnaire de santé.");
        }

        // Un travailleur ne remplit que son propre questionnaire.
        if (user.Roles.Contains(Roles.Travailleur) && contexte.PersonneIdJeton != command.PersonneId)
        {
            return Error.Forbidden("questionnaire.hors-perimetre", "Un travailleur ne remplit que ses propres questionnaires.");
        }

        var reponses = await Questionnaires.ValiderAsync(protocoles, command.ModeleCode, command.Reponses, cancellationToken);
        if (!reponses.IsSuccess)
        {
            return reponses.Error!;
        }

        var aujourdhui = horloge.Aujourdhui();
        var dossier = await dossiers.GetParPersonneAsync(command.PersonneId, cancellationToken);
        if (dossier is null)
        {
            var ouvert = Regles.Appliquer("dossier-sante.invalide", () => DossierSante.Ouvrir(command.PersonneId, null, aujourdhui));
            if (!ouvert.IsSuccess)
            {
                return ouvert.Error!;
            }

            dossier = ouvert.Value;
            dossiers.Add(dossier);
            garde.Journaliser(ActionAudit.Creation, PartiesDossier.Dossier, dossier.Id);
        }

        var (modele, valeurs) = reponses.Value;
        var source = user.Roles.Contains(Roles.Travailleur) ? SourceQuestionnaire.Portail : SourceQuestionnaire.Tablette;
        var dossierCible = dossier;
        var resultat = Regles.Appliquer("questionnaire.invalide",
            () => dossierCible.EnregistrerQuestionnaire(modele.Code, modele.Version, valeurs, source, horloge.GetUtcNow(), null).Id);
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        garde.Journaliser(ActionAudit.Creation, PartiesDossier.Questionnaires, dossier.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return resultat.Value;
    }
}

/// <summary>Modèle de questionnaire et dernières réponses de la personne, pour pré-remplir une nouvelle saisie (SAN-22).</summary>
public sealed record QuestionnairePreRempliDto(ModeleQuestionnaireDto Modele, QuestionnaireDto? DernieresReponses);

public sealed record ObtenirQuestionnairePreRempli(Guid DossierId, string ModeleCode, BuildingBlocks.Domain.Language Langue);

public sealed class ObtenirQuestionnairePreRempliHandler(GardeDossier garde, IProtocolesRepository protocoles)
    : IQueryHandler<ObtenirQuestionnairePreRempli, QuestionnairePreRempliDto>
{
    public async Task<Result<QuestionnairePreRempliDto>> HandleAsync(ObtenirQuestionnairePreRempli query, CancellationToken cancellationToken)
    {
        var modele = await protocoles.GetModeleQuestionnaireAsync(query.ModeleCode.Trim().ToUpperInvariant(), cancellationToken);
        if (modele is null)
        {
            return Error.NotFound("questionnaire.modele-inconnu", "Modèle de questionnaire inconnu.");
        }

        var dossier = await garde.LireAsync(query.DossierId, PartiesDossier.Questionnaires, cancellationToken);
        return dossier.IsSuccess
            ? new QuestionnairePreRempliDto(modele.Dto(query.Langue), dossier.Value.DernierQuestionnaire(modele.Code)?.Dto())
            : dossier.Error!;
    }
}

internal static class Questionnaires
{
    public static async Task<Result<(Domain.Protocoles.ModeleQuestionnaire Modele, IReadOnlyList<ReponseQuestionnaire> Reponses)>> ValiderAsync(
        IProtocolesRepository protocoles, string modeleCode, IReadOnlyList<ReponseSaisie> saisies, CancellationToken cancellationToken)
    {
        var modele = await protocoles.GetModeleQuestionnaireAsync((modeleCode ?? string.Empty).Trim().ToUpperInvariant(), cancellationToken);
        if (modele is null)
        {
            return Error.NotFound("questionnaire.modele-inconnu", "Modèle de questionnaire inconnu.");
        }

        IReadOnlyList<ReponseQuestionnaire> reponses = [.. saisies.Select(r => new ReponseQuestionnaire((r.CodeQuestion ?? string.Empty).Trim().ToUpperInvariant(), r.Valeur))];
        var verification = Regles.Appliquer("questionnaire.invalide", () => modele.VerifierReponses(reponses));
        return verification.IsSuccess ? (modele, reponses) : verification.Error!;
    }
}
