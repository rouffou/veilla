using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Application.Security;
using Sepp.BuildingBlocks.Domain;
using Sepp.SurveillanceMedicale.Domain.Conservation;
using Sepp.SurveillanceMedicale.Domain.Examens;
using Sepp.SurveillanceMedicale.Domain.Protocoles;
using Sepp.SurveillanceMedicale.Domain.Vaccinations;

namespace Sepp.SurveillanceMedicale.Application.Protocoles;

// Protocoles médicaux (SAN-22, SAN-23, SAN-24, SAN-44, SAN-50) : paramétrage sans donnée personnelle.
// Lecture : personnel médical (dossier-sante:lire) ; écriture : CPMT dirigeant (protocoles-medicaux:administrer),
// sauf les modèles de texte, propres à chaque CPMT (dossier-sante:ecrire).

public sealed record LibelleDto(string Fr, string Nl, string De, string? En = null)
{
    public LocalizedLabel ToLabel() => new(Fr, Nl, De, En);
}

public sealed record ValeurReferenceDto(Guid Id, TypeActe TypeActe, string CodeMesure, string Libelle, string Unite, decimal? Minimum, decimal? Maximum, DateOnly ValideDu, DateOnly? ValideJusquAu);

public sealed record QuestionModeleDto(string Code, string Libelle, TypeReponse TypeReponse, bool Obligatoire, IReadOnlyList<string> Choix);

public sealed record ModeleQuestionnaireDto(Guid Id, string Code, int Version, string Titre, IReadOnlyList<QuestionModeleDto> Questions);

public sealed record SchemaVaccinalDto(Guid Id, string VaccinCode, string Libelle, IReadOnlyList<string> CodesRisques, int NombreDoses, IReadOnlyList<int> IntervallesMois, int? RappelMois);

public sealed record ModeleTexteDto(Guid Id, string Code, RubriqueModeleTexte Rubrique, string Titre, string Texte);

public sealed record DureeConservationDto(Guid Id, string CodeAgent, int Annees, string BaseLegale);

public static class ProtocolesProjections
{
    public static ValeurReferenceDto Dto(this ValeurReference v, Language langue) =>
        new(v.Id, v.TypeActe, v.CodeMesure, v.Libelle.In(langue), v.Unite, v.Minimum, v.Maximum, v.Validite.ValidFrom, v.Validite.ValidTo);

    public static ModeleQuestionnaireDto Dto(this ModeleQuestionnaire m, Language langue) =>
        new(m.Id, m.Code, m.Version, m.Titre.In(langue),
            [.. m.Questions.Select(q => new QuestionModeleDto(q.Code, q.Libelle.In(langue), q.TypeReponse, q.Obligatoire, q.Choix))]);

    public static SchemaVaccinalDto Dto(this SchemaVaccinal s, Language langue) =>
        new(s.Id, s.VaccinCode, s.Libelle.In(langue), s.CodesRisques, s.NombreDoses, s.IntervallesMois, s.RappelMois);
}

internal static class AccesProtocoles
{
    public static Error? Lire(ICurrentUser user) =>
        Regles.Permission(user, Permissions.DossierSanteLire, "Les protocoles médicaux sont réservés au personnel médical.");

    public static Error? Administrer(ICurrentUser user) =>
        Regles.Permission(user, Permissions.ProtocolesMedicauxAdministrer, "Les protocoles médicaux sont validés par le CPMT dirigeant.");
}

public sealed record ListerValeursReference(Language Langue);

public sealed class ListerValeursReferenceHandler(IProtocolesRepository protocoles, ICurrentUser user)
    : IQueryHandler<ListerValeursReference, IReadOnlyList<ValeurReferenceDto>>
{
    public async Task<Result<IReadOnlyList<ValeurReferenceDto>>> HandleAsync(ListerValeursReference query, CancellationToken cancellationToken)
    {
        if (AccesProtocoles.Lire(user) is { } interdit)
        {
            return interdit;
        }

        var valeurs = await protocoles.ListerValeursReferenceAsync(cancellationToken);
        return valeurs.OrderBy(v => v.TypeActe).ThenBy(v => v.CodeMesure).ThenBy(v => v.Validite.ValidFrom).Select(v => v.Dto(query.Langue)).ToList();
    }
}

/// <summary>SAN-23 : nouvelle valeur de référence ; la valeur précédente de la même mesure est clôturée (DAT-04).</summary>
public sealed record DefinirValeurReference(TypeActe TypeActe, string CodeMesure, LibelleDto Libelle, string Unite, decimal? Minimum, decimal? Maximum, DateOnly ValideDu);

public sealed class DefinirValeurReferenceHandler(IProtocolesRepository protocoles, ICurrentUser user, IUnitOfWork unitOfWork)
    : ICommandHandler<DefinirValeurReference, Guid>
{
    public async Task<Result<Guid>> HandleAsync(DefinirValeurReference command, CancellationToken cancellationToken)
    {
        if (AccesProtocoles.Administrer(user) is { } interdit)
        {
            return interdit;
        }

        var existantes = await protocoles.ListerValeursReferenceAsync(cancellationToken);
        var resultat = Regles.Appliquer("valeur-reference.invalide", () =>
        {
            var nouvelle = ValeurReference.Definir(command.TypeActe, command.CodeMesure, command.Libelle.ToLabel(), command.Unite, command.Minimum, command.Maximum, command.ValideDu);
            foreach (var precedente in existantes.Where(v => v.TypeActe == nouvelle.TypeActe && v.CodeMesure == nouvelle.CodeMesure && v.Validite.IsOpen))
            {
                precedente.Cloturer(command.ValideDu);
            }

            return nouvelle;
        });
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        protocoles.Add(resultat.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return resultat.Value.Id;
    }
}

public sealed record ListerModelesQuestionnaire(Language Langue);

public sealed class ListerModelesQuestionnaireHandler(IProtocolesRepository protocoles, ICurrentUser user)
    : IQueryHandler<ListerModelesQuestionnaire, IReadOnlyList<ModeleQuestionnaireDto>>
{
    public async Task<Result<IReadOnlyList<ModeleQuestionnaireDto>>> HandleAsync(ListerModelesQuestionnaire query, CancellationToken cancellationToken)
    {
        // Le travailleur et l'assistant médical voient les modèles pour remplir le questionnaire (SAN-22).
        if (!user.HasPermission(Permissions.DossierSanteLire) && !user.HasPermission(Permissions.QuestionnaireSanteRemplir))
        {
            return Error.Forbidden("surveillance-medicale.interdit", "Droits insuffisants.");
        }

        var modeles = await protocoles.ListerModelesQuestionnaireAsync(cancellationToken);
        return modeles.GroupBy(m => m.Code).Select(g => g.MaxBy(m => m.Version)!).OrderBy(m => m.Code).Select(m => m.Dto(query.Langue)).ToList();
    }
}

public sealed record NouvelleQuestion(string Code, LibelleDto Libelle, TypeReponse TypeReponse, bool Obligatoire, IReadOnlyList<string>? Choix);

/// <summary>SAN-22 : nouveau modèle, ou nouvelle version d'un modèle existant (les questionnaires remplis gardent leur version).</summary>
public sealed record PublierModeleQuestionnaire(string Code, LibelleDto Titre, IReadOnlyList<NouvelleQuestion> Questions);

public sealed class PublierModeleQuestionnaireHandler(IProtocolesRepository protocoles, ICurrentUser user, IUnitOfWork unitOfWork)
    : ICommandHandler<PublierModeleQuestionnaire, Guid>
{
    public async Task<Result<Guid>> HandleAsync(PublierModeleQuestionnaire command, CancellationToken cancellationToken)
    {
        if (AccesProtocoles.Administrer(user) is { } interdit)
        {
            return interdit;
        }

        var precedent = await protocoles.GetModeleQuestionnaireAsync((command.Code ?? string.Empty).Trim().ToUpperInvariant(), cancellationToken);
        var resultat = Regles.Appliquer("questionnaire.modele-invalide", () => ModeleQuestionnaire.Creer(
            command.Code!, (precedent?.Version ?? 0) + 1, command.Titre.ToLabel(),
            [.. command.Questions.Select(q => new QuestionModele(q.Code, q.Libelle.ToLabel(), q.TypeReponse, q.Obligatoire, q.Choix ?? []))]));
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        protocoles.Add(resultat.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return resultat.Value.Id;
    }
}

public sealed record ListerSchemasVaccinaux(Language Langue);

public sealed class ListerSchemasVaccinauxHandler(IProtocolesRepository protocoles, ICurrentUser user)
    : IQueryHandler<ListerSchemasVaccinaux, IReadOnlyList<SchemaVaccinalDto>>
{
    public async Task<Result<IReadOnlyList<SchemaVaccinalDto>>> HandleAsync(ListerSchemasVaccinaux query, CancellationToken cancellationToken)
    {
        if (AccesProtocoles.Lire(user) is { } interdit)
        {
            return interdit;
        }

        return (await protocoles.ListerSchemasVaccinauxAsync(cancellationToken)).OrderBy(s => s.VaccinCode).Select(s => s.Dto(query.Langue)).ToList();
    }
}

/// <summary>SAN-50 : schéma vaccinal par risque (ou périodicité des tests tuberculiniques, code <c>TEST_TUBERCULINIQUE</c>).</summary>
public sealed record DefinirSchemaVaccinal(string VaccinCode, LibelleDto Libelle, IReadOnlyList<string> CodesRisques, int NombreDoses, IReadOnlyList<int>? IntervallesMois, int? RappelMois);

public sealed class DefinirSchemaVaccinalHandler(IProtocolesRepository protocoles, ICurrentUser user, IUnitOfWork unitOfWork)
    : ICommandHandler<DefinirSchemaVaccinal, Guid>
{
    public async Task<Result<Guid>> HandleAsync(DefinirSchemaVaccinal command, CancellationToken cancellationToken)
    {
        if (AccesProtocoles.Administrer(user) is { } interdit)
        {
            return interdit;
        }

        var resultat = Regles.Appliquer("schema-vaccinal.invalide", () => SchemaVaccinal.Definir(
            command.VaccinCode, command.Libelle.ToLabel(), command.CodesRisques, command.NombreDoses, command.IntervallesMois ?? [], command.RappelMois));
        if (!resultat.IsSuccess)
        {
            return resultat.Error!;
        }

        protocoles.Add(resultat.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return resultat.Value.Id;
    }
}

public sealed record ListerDureesConservation;

public sealed class ListerDureesConservationHandler(IProtocolesRepository protocoles, ICurrentUser user)
    : IQueryHandler<ListerDureesConservation, IReadOnlyList<DureeConservationDto>>
{
    public async Task<Result<IReadOnlyList<DureeConservationDto>>> HandleAsync(ListerDureesConservation query, CancellationToken cancellationToken)
    {
        if (AccesProtocoles.Lire(user) is { } interdit && !user.HasPermission(Permissions.DossierSantePurger))
        {
            return interdit;
        }

        return (await protocoles.ListerDureesConservationAsync(cancellationToken))
            .OrderBy(d => d.CodeAgent).Select(d => new DureeConservationDto(d.Id, d.CodeAgent, d.Annees, d.BaseLegale)).ToList();
    }
}

/// <summary>SAN-44 : durée de conservation d'un type d'exposition (création ou modification).</summary>
public sealed record DefinirDureeConservation(string CodeAgent, int Annees, string BaseLegale);

public sealed class DefinirDureeConservationHandler(IProtocolesRepository protocoles, ICurrentUser user, IUnitOfWork unitOfWork)
    : ICommandHandler<DefinirDureeConservation, Guid>
{
    public async Task<Result<Guid>> HandleAsync(DefinirDureeConservation command, CancellationToken cancellationToken)
    {
        if (AccesProtocoles.Administrer(user) is { } interdit)
        {
            return interdit;
        }

        var code = (command.CodeAgent ?? string.Empty).Trim().ToUpperInvariant();
        var existante = (await protocoles.ListerDureesConservationAsync(cancellationToken)).FirstOrDefault(d => d.CodeAgent == code);
        var resultat = Regles.Appliquer("duree-conservation.invalide", () =>
        {
            if (existante is null)
            {
                var nouvelle = DureeConservationExposition.Definir(code, command.Annees, command.BaseLegale);
                protocoles.Add(nouvelle);
                return nouvelle.Id;
            }

            existante.Modifier(command.Annees, command.BaseLegale);
            return existante.Id;
        });
        if (resultat.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return resultat;
    }
}

/// <summary>SAN-24 : modèles de texte du CPMT connecté.</summary>
public sealed record ListerModelesTexte;

public sealed class ListerModelesTexteHandler(IProtocolesRepository protocoles, ICurrentUser user) : IQueryHandler<ListerModelesTexte, IReadOnlyList<ModeleTexteDto>>
{
    public async Task<Result<IReadOnlyList<ModeleTexteDto>>> HandleAsync(ListerModelesTexte query, CancellationToken cancellationToken)
    {
        if (Regles.Permission(user, Permissions.DossierSanteEcrire, "Les modèles de texte sont réservés au personnel médical.") is { } interdit)
        {
            return interdit;
        }

        return (await protocoles.ListerModelesTexteAsync(user.UserId, cancellationToken))
            .OrderBy(m => m.Rubrique).ThenBy(m => m.Code).Select(m => new ModeleTexteDto(m.Id, m.Code, m.Rubrique, m.Titre, m.Texte)).ToList();
    }
}

public sealed record EnregistrerModeleTexte(Guid? Id, string Code, RubriqueModeleTexte Rubrique, string Titre, string Texte);

public sealed class EnregistrerModeleTexteHandler(IProtocolesRepository protocoles, ICurrentUser user, IUnitOfWork unitOfWork)
    : ICommandHandler<EnregistrerModeleTexte, Guid>
{
    public async Task<Result<Guid>> HandleAsync(EnregistrerModeleTexte command, CancellationToken cancellationToken)
    {
        if (Regles.Permission(user, Permissions.DossierSanteEcrire, "Les modèles de texte sont réservés au personnel médical.") is { } interdit)
        {
            return interdit;
        }

        var existants = await protocoles.ListerModelesTexteAsync(user.UserId, cancellationToken);
        Result<Guid> resultat;
        if (command.Id is { } id)
        {
            var modele = existants.FirstOrDefault(m => m.Id == id);
            if (modele is null)
            {
                // Le modèle d'un autre CPMT est invisible.
                return Error.NotFound("modele-texte.inconnu", "Modèle de texte inconnu.");
            }

            resultat = Regles.Appliquer("modele-texte.invalide", () =>
            {
                modele.Modifier(command.Rubrique, command.Titre, command.Texte);
                return modele.Id;
            });
        }
        else
        {
            if (existants.Any(m => m.Code == command.Code?.Trim().ToUpperInvariant()))
            {
                return Error.Conflict("modele-texte.existe", "Vous avez déjà un modèle de texte avec ce code.");
            }

            resultat = Regles.Appliquer("modele-texte.invalide", () =>
            {
                var modele = ModeleTexte.Creer(user.UserId, command.Code!, command.Rubrique, command.Titre, command.Texte);
                protocoles.Add(modele);
                return modele.Id;
            });
        }

        if (resultat.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return resultat;
    }
}

public sealed record SupprimerModeleTexte(Guid Id);

public sealed class SupprimerModeleTexteHandler(IProtocolesRepository protocoles, ICurrentUser user, IUnitOfWork unitOfWork)
    : ICommandHandler<SupprimerModeleTexte, Unit>
{
    public async Task<Result<Unit>> HandleAsync(SupprimerModeleTexte command, CancellationToken cancellationToken)
    {
        if (Regles.Permission(user, Permissions.DossierSanteEcrire, "Les modèles de texte sont réservés au personnel médical.") is { } interdit)
        {
            return interdit;
        }

        var modele = await protocoles.GetModeleTexteAsync(command.Id, cancellationToken);
        if (modele is null || !string.Equals(modele.CpmtId, user.UserId, StringComparison.Ordinal))
        {
            return Error.NotFound("modele-texte.inconnu", "Modèle de texte inconnu.");
        }

        protocoles.Remove(modele);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
