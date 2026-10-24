using System.Globalization;

using Sepp.BuildingBlocks.Domain;
using Sepp.BuildingBlocks.Domain.Calendar;

namespace Sepp.Reintegration.Domain;

/// <summary>Unités des paramètres légaux reçus du service Référentiels (<c>ParametreLegalModifie.Unite</c>).</summary>
public enum UniteDelai
{
    JoursOuvrables,
    JoursCalendrier,
    Semaines,
    Mois,
}

/// <summary>Durée légale (ARC-21) : valeur et unité d'un paramètre, appliquée à une date avec le calendrier belge (DAT-08).</summary>
public readonly record struct DureeLegale(int Valeur, UniteDelai Unite)
{
    public DateOnly AjouterA(DateOnly depart, BusinessCalendar calendrier) => Unite switch
    {
        UniteDelai.JoursOuvrables => calendrier.AddBusinessDays(depart, Valeur),
        UniteDelai.JoursCalendrier => depart.AddDays(Valeur),
        UniteDelai.Semaines => depart.AddDays(7 * Valeur),
        UniteDelai.Mois => depart.AddMonths(Valeur),
        _ => throw new DomainException($"Unité de délai inconnue : {Unite}."),
    };

    public override string ToString() => $"{Valeur.ToString(CultureInfo.InvariantCulture)} {Unite}";
}

/// <summary>
/// Règles légales de la réintégration (§2.1, réforme au 1er janvier 2026) et réglages de suivi, en vigueur à une date. Les
/// valeurs légales viennent des paramètres du service Référentiels (<c>REINTEGRATION.*</c>, ARC-21) ou, à défaut, de la
/// configuration du service (mêmes valeurs que le catalogue de Référentiels, à valider par le département juridique).
/// </summary>
/// <param name="InformationTravailleur">REINTEGRATION.INFORMATION_TRAVAILLEUR : information du travailleur après N d'incapacité (SAN-60).</param>
/// <param name="EstimationPotentiel">REINTEGRATION.ESTIMATION_POTENTIEL : estimation du potentiel de travail après N d'incapacité (SAN-61).</param>
/// <param name="TrajetObligatoire">REINTEGRATION.TRAJET_OBLIGATOIRE : trajet au plus tard N après le début de l'incapacité.</param>
/// <param name="SeuilTravailleurs">REINTEGRATION.SEUIL_TRAVAILLEURS : effectif à partir duquel le trajet est obligatoire.</param>
/// <param name="AbsencesMedecinConseil">REINTEGRATION.ABSENCES_MEDECIN_CONSEIL : absences avant information du médecin-conseil (SAN-63).</param>
/// <param name="Invitation">REINTEGRATION.INVITATION.DELAI : invitation à l'évaluation de réintégration après la demande (SAN-62).</param>
/// <param name="ReponsePlan">REINTEGRATION.REPONSE_PLAN.DELAI : réponse du travailleur au plan (SAN-62).</param>
/// <param name="PlanEmployeur">Délai de l'employeur pour le plan ou le rapport motivé après le formulaire : non fixé par le cahier, <c>null</c> = non suivi (à valider).</param>
/// <param name="AlerteAvantEcheanceJoursOuvrables">Alerte « échéance proche » N jours ouvrables avant chaque échéance (SAN-65) : réglage d'organisation, à valider ; 0 = désactivée.</param>
/// <param name="Calendrier">Jours ouvrables belges, jours fériés supplémentaires de Référentiels compris (DAT-08).</param>
public sealed record PolitiqueReintegration(
    DureeLegale InformationTravailleur,
    DureeLegale EstimationPotentiel,
    DureeLegale TrajetObligatoire,
    int SeuilTravailleurs,
    int AbsencesMedecinConseil,
    DureeLegale Invitation,
    DureeLegale ReponsePlan,
    DureeLegale? PlanEmployeur,
    int AlerteAvantEcheanceJoursOuvrables,
    BusinessCalendar Calendrier);

/// <summary>Échéances suivies par le tableau de bord des délais (SAN-65).</summary>
public enum TypeEcheance
{
    InformationTravailleur,
    EstimationPotentiel,
    TrajetObligatoire,
    InvitationEvaluation,
    PlanEmployeur,
    ReponseTravailleur,
}

public enum StatutEcheance
{
    AVenir,

    /// <summary>Dans la fenêtre d'alerte avant l'échéance (SAN-65 : « alertes avant dépassement »).</summary>
    Proche,
    Depassee,
    Respectee,
}

/// <summary>Échéance légale d'un dossier de réintégration ; <c>RespecteeLe</c> est la date de l'acte qui la satisfait.</summary>
public sealed record Echeance(TypeEcheance Type, DateOnly Date, DateOnly? RespecteeLe, string BaseLegale)
{
    public DateOnly DebutAlerte(PolitiqueReintegration politique) =>
        politique.AlerteAvantEcheanceJoursOuvrables > 0 ? politique.Calendrier.SubtractBusinessDays(Date, politique.AlerteAvantEcheanceJoursOuvrables) : Date;

    public StatutEcheance StatutAu(DateOnly aujourdHui, PolitiqueReintegration politique) =>
        RespecteeLe is not null ? StatutEcheance.Respectee
        : aujourdHui > Date ? StatutEcheance.Depassee
        : politique.AlerteAvantEcheanceJoursOuvrables > 0 && aujourdHui >= DebutAlerte(politique) ? StatutEcheance.Proche
        : StatutEcheance.AVenir;
}

/// <summary>
/// ADR 0008 : processus persistant à minuteries stockées en base (<c>prochaine_echeance</c>, <c>prochaine_minuterie</c>),
/// traitées par un service d'arrière-plan (<c>FOR UPDATE SKIP LOCKED</c>). Chaque échéance non respectée arme deux
/// minuteries : l'alerte avant dépassement (<c>Type.Proche</c>) et le dépassement (<c>Type.Depassee</c>), déclenchées une
/// seule fois chacune ; une échéance déplacée (nouvelle date) les réarme.
/// </summary>
public abstract class ProcessusAEcheances : AggregateRoot
{
    protected ProcessusAEcheances(Guid id) : base(id)
    {
    }

    protected ProcessusAEcheances()
    {
    }

    /// <summary>Prochaine minuterie à traiter (jour belge) ; <c>null</c> si aucune.</summary>
    public DateOnly? ProchaineEcheance { get; private set; }

    public string? ProchaineMinuterie { get; private set; }

    /// <summary>Minuteries déjà déclenchées : <c>&lt;type&gt;.&lt;Proche|Depassee&gt;@&lt;date de l'échéance&gt;</c>.</summary>
    public IReadOnlyList<string> MinuteriesDeclenchees { get; private set; } = [];

    /// <summary>Échéances légales du processus (vides une fois le processus clos).</summary>
    public abstract IReadOnlyList<Echeance> Echeances(PolitiqueReintegration politique);

    public bool Reprogrammer(PolitiqueReintegration politique)
    {
        var suivante = Candidates(politique).OrderBy(c => c.Date).ThenBy(c => c.Code, StringComparer.Ordinal).FirstOrDefault();
        var date = suivante == default ? (DateOnly?)null : suivante.Date;
        var code = suivante == default ? null : suivante.Code;
        if (date == ProchaineEcheance && code == ProchaineMinuterie)
        {
            return false;
        }

        ProchaineEcheance = date;
        ProchaineMinuterie = code;
        return true;
    }

    /// <summary>Déclenche les minuteries échues (chacune une fois), puis programme la suivante.</summary>
    public IReadOnlyList<string> DeclencherMinuteries(DateOnly aujourdHui, PolitiqueReintegration politique)
    {
        var declenchees = Candidates(politique).Where(c => c.Date <= aujourdHui).OrderBy(c => c.Date).Select(c => c.Code).ToList();
        if (declenchees.Count > 0)
        {
            MinuteriesDeclenchees = [.. MinuteriesDeclenchees, .. declenchees];
        }

        Reprogrammer(politique);
        return declenchees;
    }

    public bool AlerteDeclenchee(Echeance echeance, StatutEcheance statut) => MinuteriesDeclenchees.Contains(Code(echeance, statut));

    private IEnumerable<(DateOnly Date, string Code)> Candidates(PolitiqueReintegration politique)
    {
        foreach (var e in Echeances(politique).Where(e => e.RespecteeLe is null))
        {
            if (politique.AlerteAvantEcheanceJoursOuvrables > 0 && !MinuteriesDeclenchees.Contains(Code(e, StatutEcheance.Proche)))
            {
                yield return (e.DebutAlerte(politique), Code(e, StatutEcheance.Proche));
            }

            if (!MinuteriesDeclenchees.Contains(Code(e, StatutEcheance.Depassee)))
            {
                yield return (e.Date.AddDays(1), Code(e, StatutEcheance.Depassee));
            }
        }
    }

    private static string Code(Echeance e, StatutEcheance statut) =>
        $"{e.Type}.{statut}@{e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
}

/// <summary>Contrôles de texte communs.</summary>
public static class Texte
{
    public static string Obligatoire(string? valeur, string nom, int longueurMaximale)
    {
        if (string.IsNullOrWhiteSpace(valeur))
        {
            throw new DomainException($"{nom} est obligatoire.");
        }

        var texte = valeur.Trim();
        return texte.Length <= longueurMaximale ? texte : throw new DomainException($"{nom} dépasse {longueurMaximale} caractères.");
    }

    public static string? Facultatif(string? valeur, string nom, int longueurMaximale) =>
        string.IsNullOrWhiteSpace(valeur) ? null : Obligatoire(valeur, nom, longueurMaximale);

    public static string Code(string? valeur, IReadOnlyList<string> connus, string nom) =>
        valeur is not null && connus.Contains(valeur) ? valeur : throw new DomainException($"{nom} inconnu : « {valeur} ».");
}
