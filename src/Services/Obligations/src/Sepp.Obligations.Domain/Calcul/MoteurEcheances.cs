using System.Globalization;

using Sepp.BuildingBlocks.Domain.Calendar;

using Sepp.Obligations.Domain.Demandes;
using Sepp.Obligations.Domain.Obligations;
using Sepp.Obligations.Domain.Projections;

namespace Sepp.Obligations.Domain.Calcul;

/// <summary>Tout ce que le service sait d'un travailleur, par ses projections locales (SAN-01, aucun appel synchrone).</summary>
public sealed record SituationTravailleur(
    Guid PersonneId,
    IReadOnlyList<AffectationLocale> Affectations,
    IReadOnlyList<ProfilRisquePosteLocal> Profils,
    IReadOnlyList<RegleSurveillanceLocale> Regles,
    IReadOnlyList<SurchargeFrequenceLocale> Surcharges,
    IReadOnlyList<OccupationLocale> Occupations,
    IReadOnlyList<EtatParticulierLocal> EtatsParticuliers,
    IReadOnlyList<ExamenLocal> Examens,
    IReadOnlyList<RepriseLocale> Reprises,
    IReadOnlyList<IncapaciteLocale> Incapacites,
    IReadOnlyList<TrajetLocal> Trajets,
    IReadOnlyList<DemandeTravailleur> Demandes)
{
    public static SituationTravailleur Vide(Guid personneId) => new(personneId, [], [], [], [], [], [], [], [], [], [], []);
}

/// <summary>Réglages organisationnels du calcul (pas des délais légaux, qui viennent des politiques légales).</summary>
public sealed record OptionsCalcul
{
    /// <summary>
    /// Une évaluation de santé (ou des actes) réalisée au plus tant de mois avant le début d'une exposition vaut
    /// évaluation préalable : l'évaluation préalable a lieu avant l'affectation effective (§5.1).
    /// </summary>
    public int AnterioritePrealableMois { get; init; } = 3;
}

/// <summary>Résultat d'un calcul : échéances attendues (ouvertes ou déjà réalisées), expositions et situation d'emploi.</summary>
public sealed class ResultatCalcul(
    IReadOnlyList<EcheanceCalculee> echeances,
    IReadOnlyList<Exposition> expositions,
    IReadOnlyList<Exposition> expositionsSansRegle,
    bool sortiDeToutes,
    IReadOnlySet<Guid> affiliesQuittes)
{
    public IReadOnlyList<EcheanceCalculee> Echeances { get; } = echeances;

    public IReadOnlyList<Exposition> Expositions { get; } = expositions;

    /// <summary>Expositions à un risque dont aucune règle de surveillance n'a été reçue (AFF-32).</summary>
    public IReadOnlyList<Exposition> ExpositionsSansRegle { get; } = expositionsSansRegle;

    /// <summary>Le travailleur n'a plus d'occupation en cours chez aucun employeur.</summary>
    public bool SortiDeToutes { get; } = sortiDeToutes;

    public IReadOnlySet<Guid> AffiliesQuittes { get; } = affiliesQuittes;

    /// <summary>Le travailleur a quitté l'affilié : ses obligations ouvertes y passent au statut « sorti de l'entreprise ».</summary>
    public bool EstSortiDe(Guid affilieId) => SortiDeToutes || AffiliesQuittes.Contains(affilieId);
}

/// <summary>
/// SAN-01 : moteur d'échéances. Fonction pure de la situation du travailleur, des politiques légales, du calendrier
/// et de la date de calcul : le même état des projections donne toujours le même résultat, quel que soit l'ordre
/// d'arrivée des événements qui l'ont construit (SAN-04).
/// </summary>
/// <remarks>
/// <para>Risques (§5.1) : pour chaque exposition (affilié, risque) dont la règle de surveillance est connue :
/// une évaluation préalable (ou des actes initiaux) au début de l'exposition, puis une chaîne de cycles périodiques :
/// chaque évaluation fonde le cycle suivant à « date + fréquence » (règle ou surcharge du CPMT), réalisé par
/// l'évaluation suivante. Après la fin de l'exposition, la chaîne se poursuit en surveillance prolongée si la règle
/// le prévoit.</para>
/// <para>Événements : examen de reprise (absence ≥ SANTE.REPRISE.ABSENCE_MINIMUM, du jour de la reprise à
/// SANTE.REPRISE.DELAI), visite de pré-reprise et consultation spontanée (délais en jours ouvrables), protection de la
/// maternité (dès la déclaration, si la travailleuse est exposée à des risques), estimation du potentiel de travail
/// (REINTEGRATION.ESTIMATION_POTENTIEL après le début de l'incapacité, sauf reprise avant), évaluation de réintégration
/// (invitation dans REINTEGRATION.INVITATION.DELAI).</para>
/// </remarks>
public sealed class MoteurEcheances(IPolitiquesLegales politiques, BusinessCalendar calendrier, OptionsCalcul options)
{
    private const string TypeEvaluationPeriodique = "EvaluationSantePeriodique";
    private const string TypeActes = "ActesMedicauxSupplementaires";
    private const string TypePrealableUniquement = "EvaluationPrealableUniquement";

    public ResultatCalcul Calculer(SituationTravailleur situation, DateOnly dateCalcul)
    {
        var occupations = situation.Occupations.Where(o => o.PersonneId == situation.PersonneId).ToList();
        var sortiDeToutes = occupations.Count > 0 && !occupations.Any(o => o.EstEnCoursOuAVenirAu(dateCalcul));
        var affiliesQuittes = occupations
            .GroupBy(o => o.AffilieId)
            .Where(g => !g.Any(o => o.EstEnCoursOuAVenirAu(dateCalcul)))
            .Select(g => g.Key)
            .ToHashSet();
        bool Sorti(Guid affilieId) => sortiDeToutes || affiliesQuittes.Contains(affilieId);

        var examens = situation.Examens
            .Where(e => e.PersonneId == situation.PersonneId)
            .Select(e => TypesObligation.TryParse(e.TypeExamen, out var type) ? new Examen(e.ExamenId, type, e.Date) : null)
            .OfType<Examen>()
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Id)
            .ToList();

        var expositions = CalculExpositions.Calculer(situation.Affectations.Where(a => a.PersonneId == situation.PersonneId), situation.Profils);
        var echeances = new List<EcheanceCalculee>();
        var sansRegle = new List<Exposition>();
        var initiales = new List<Initiale>();

        foreach (var exposition in expositions)
        {
            CalculerRisque(situation, exposition, examens, dateCalcul, Sorti(exposition.AffilieId), echeances, initiales, sansRegle);
        }

        echeances.AddRange(Initiales(initiales, dateCalcul, Sorti));
        echeances.AddRange(Reprises(situation, examens, Sorti));
        echeances.AddRange(Demandes(situation, examens, Sorti));
        echeances.AddRange(ProtectionMaternite(situation, expositions, examens, dateCalcul, Sorti));
        echeances.AddRange(EstimationsPotentiel(situation, examens, Sorti));
        echeances.AddRange(EvaluationsReintegration(situation, examens, Sorti));

        return new ResultatCalcul(
            echeances.OrderBy(e => e.DateDue).ThenBy(e => e.Cle, StringComparer.Ordinal).ToList(),
            expositions,
            sansRegle,
            sortiDeToutes,
            affiliesQuittes);
    }

    private void CalculerRisque(
        SituationTravailleur situation, Exposition exposition, IReadOnlyList<Examen> examens, DateOnly dateCalcul, bool sorti,
        List<EcheanceCalculee> echeances, List<Initiale> initiales, List<Exposition> sansRegle)
    {
        var dateRegle = Max(dateCalcul, exposition.Debut);
        var regle = RegleAu(situation.Regles, exposition.CodeRisque, dateRegle);
        if (regle is null || regle.TypeSurveillance is not (TypeEvaluationPeriodique or TypeActes or TypePrealableUniquement))
        {
            sansRegle.Add(exposition);
            return;
        }

        var actes = regle.TypeSurveillance == TypeActes;
        var frequence = Frequence(situation, exposition, regle, dateRegle);
        var seuil = exposition.Debut.AddMonths(-options.AnterioritePrealableMois);
        var famille = examens
            .Where(e => (actes ? e.Type == TypeObligation.ActesMedicauxSupplementaires : e.Type.EstEvaluationDeSante()) && e.Date >= seuil)
            .ToList();

        initiales.Add(new Initiale(
            exposition, regle, actes ? TypeObligation.ActesMedicauxSupplementaires : TypeObligation.EvaluationPrealable, famille.FirstOrDefault()));

        if (frequence.Mois is not { } mois)
        {
            return;
        }

        var prolongee = regle.SurveillanceProlongee && !actes;
        var typePeriodique = actes ? TypeObligation.ActesMedicauxSupplementaires : TypeObligation.EvaluationPeriodique;
        for (var i = 0; i < famille.Count; i++)
        {
            var fondement = famille[i];
            if (exposition.Fin is { } fin && fondement.Date >= fin && !prolongee)
            {
                break;
            }

            var due = fondement.Date.AddMonths(mois);
            TypeObligation? type = exposition.Fin is null || due < exposition.Fin
                ? typePeriodique
                : prolongee ? TypeObligation.SurveillanceProlongee : null;
            if (type is null)
            {
                continue;
            }

            var suivant = i + 1 < famille.Count ? famille[i + 1] : null;
            if (suivant is null && sorti)
            {
                continue;
            }

            var explication = type == TypeObligation.SurveillanceProlongee
                ? $"Surveillance prolongée après la fin de l'exposition au risque {exposition.CodeRisque} : examen tous les {mois} mois depuis le dernier."
                : $"{typePeriodique.Libelle().Fr} pour le risque {exposition.CodeRisque} : {mois} mois après l'examen du {Jour(fondement.Date)}.";
            echeances.Add(new EcheanceCalculee(
                Cle(actes ? "ACTES" : "PERIODIQUE", exposition.AffilieId, exposition.CodeRisque, Jour(fondement.Date)),
                exposition.AffilieId,
                type.Value,
                frequence.Origine,
                [exposition.CodeRisque],
                due,
                due,
                suivant is null ? null : new Realisation(suivant.Date, suivant.Id),
                new Justification(
                    $"regle-surveillance:{exposition.CodeRisque}",
                    regle.Version,
                    explication,
                    Entrees(
                        ("risque", exposition.CodeRisque),
                        ("type_surveillance", regle.TypeSurveillance),
                        ("regle_version", Nombre(regle.Version)),
                        ("regle_valide_du", Jour(regle.ValideDu)),
                        ("frequence_mois", Nombre(mois)),
                        ("source_frequence", frequence.Source),
                        ("surveillance_prolongee", regle.SurveillanceProlongee ? "oui" : "non"),
                        ("exposition_debut", Jour(exposition.Debut)),
                        ("exposition_fin", exposition.Fin is { } f ? Jour(f) : "en cours"),
                        ("postes", string.Join(',', exposition.PosteIds)),
                        ("derniere_evaluation", $"{Jour(fondement.Date)} ({fondement.Type.Code()}, examen {fondement.Id})"),
                        ("realise_par", suivant is null ? "-" : $"{Jour(suivant.Date)} (examen {suivant.Id})")))));
        }
    }

    /// <summary>Évaluation préalable (ou actes initiaux) : une par affilié et par date de début d'exposition, couvrant tous les risques qui commencent ce jour-là.</summary>
    private static IEnumerable<EcheanceCalculee> Initiales(List<Initiale> initiales, DateOnly dateCalcul, Func<Guid, bool> sorti)
    {
        foreach (var groupe in initiales.GroupBy(i => (i.Exposition.AffilieId, i.Type, i.Exposition.Debut)))
        {
            var (affilieId, type, debut) = groupe.Key;
            var membres = groupe.OrderBy(i => i.Exposition.CodeRisque, StringComparer.Ordinal).ToList();
            var realisation = membres.Select(i => i.PremierExamen).OfType<Examen>().OrderBy(e => e.Date).ThenBy(e => e.Id).FirstOrDefault();
            var enCours = membres.Any(i => i.Exposition.EstEnCoursOuAVenirAu(dateCalcul));
            if (realisation is null && (!enCours || sorti(affilieId)))
            {
                continue;
            }

            var codes = membres.Select(i => i.Exposition.CodeRisque).ToList();
            var entrees = new List<(string, string)>
            {
                ("exposition_debut", Jour(debut)),
                ("risques", string.Join(',', codes)),
                ("postes", string.Join(',', membres.SelectMany(i => i.Exposition.PosteIds).Distinct())),
            };
            entrees.AddRange(membres.Select(i => ($"regle.{i.Exposition.CodeRisque}", $"v{Nombre(i.Regle.Version)} {i.Regle.TypeSurveillance} du {Jour(i.Regle.ValideDu)}")));
            entrees.Add(("realise_par", realisation is null ? "-" : $"{Jour(realisation.Date)} ({realisation.Type.Code()}, examen {realisation.Id})"));

            yield return new EcheanceCalculee(
                Cle(type == TypeObligation.EvaluationPrealable ? "PREALABLE" : "ACTES-INITIAUX", affilieId, Jour(debut)),
                affilieId,
                type,
                OrigineObligation.Regle,
                codes,
                debut,
                debut,
                realisation is null ? null : new Realisation(realisation.Date, realisation.Id),
                new Justification(
                    $"regle-surveillance:{string.Join(',', codes)}",
                    membres.Count == 1 ? membres[0].Regle.Version : null,
                    $"{type.Libelle().Fr} : nouvelle exposition à {string.Join(", ", codes)} à partir du {Jour(debut)}, avant l'affectation effective.",
                    Entrees([.. entrees])));
        }
    }

    private IEnumerable<EcheanceCalculee> Reprises(SituationTravailleur situation, IReadOnlyList<Examen> examens, Func<Guid, bool> sorti)
    {
        // Une reprise annulée (ARC-33) n'attend plus d'examen : l'obligation ouverte est annulée par le recalcul.
        foreach (var reprise in situation.Reprises.Where(r => r.PersonneId == situation.PersonneId && !r.Annulee))
        {
            var minimum = politiques.Duree(CodesParametres.RepriseAbsenceMinimum, reprise.DateReprise);
            if (!ExamenRepriseRequis(reprise.DebutAbsence, reprise.DateReprise))
            {
                continue;
            }

            var delai = politiques.Duree(CodesParametres.RepriseDelai, reprise.DateReprise);
            var realisation = PremierExamen(examens, TypeObligation.ExamenReprise, reprise.DateReprise);
            if (realisation is null && sorti(reprise.AffilieId))
            {
                continue;
            }

            yield return Evenement(
                Cle("REPRISE", reprise.AffilieId, Jour(reprise.DateReprise)),
                reprise.AffilieId,
                TypeObligation.ExamenReprise,
                reprise.DateReprise,
                delai.AjouterA(reprise.DateReprise, calendrier),
                realisation,
                delai,
                $"Absence du {Jour(reprise.DebutAbsence)} au {Jour(reprise.DateReprise)} d'au moins {minimum.Valeur} {minimum.Unite} : examen de reprise du jour de la reprise à {delai.Valeur} {delai.Unite}.",
                ("debut_absence", Jour(reprise.DebutAbsence)),
                ("date_reprise", Jour(reprise.DateReprise)),
                ("absence_minimum", minimum.Description),
                ("delai", delai.Description));
        }
    }

    /// <summary>
    /// §5.1 : une absence d'au moins SANTE.REPRISE.ABSENCE_MINIMUM (4 semaines) avant la date de reprise donne lieu à
    /// l'examen de reprise ; sinon l'examen n'est pas requis (branche « examen non requis » du processus de reprise, ARC-33).
    /// </summary>
    public bool ExamenRepriseRequis(DateOnly debutAbsence, DateOnly dateReprise) =>
        politiques.Duree(CodesParametres.RepriseAbsenceMinimum, dateReprise).AjouterA(debutAbsence, calendrier) <= dateReprise;

    private IEnumerable<EcheanceCalculee> Demandes(SituationTravailleur situation, IReadOnlyList<Examen> examens, Func<Guid, bool> sorti)
    {
        foreach (var demande in situation.Demandes.Where(d => d.PersonneId == situation.PersonneId))
        {
            var code = demande.Type == TypeObligation.VisitePreReprise ? CodesParametres.PreRepriseDelai : CodesParametres.ConsultationSpontaneeDelai;
            var delai = politiques.Duree(code, demande.DateDemande);
            var realisation = PremierExamen(examens, demande.Type, demande.DateDemande);
            if (realisation is null && sorti(demande.AffilieId))
            {
                continue;
            }

            yield return Evenement(
                CleDemande(demande.Id),
                demande.AffilieId,
                demande.Type,
                demande.DateDemande,
                delai.AjouterA(demande.DateDemande, calendrier),
                realisation,
                delai,
                $"{demande.Type.Libelle().Fr} demandée le {Jour(demande.DateDemande)} : évaluation dans les {delai.Valeur} {delai.Unite}.",
                ("demande", demande.Id.ToString()),
                ("date_demande", Jour(demande.DateDemande)),
                ("delai", delai.Description));
        }
    }

    /// <summary>AFF-24 : examen dès la déclaration, pour chaque affilié où la travailleuse est exposée à des risques pendant la protection.</summary>
    private static IEnumerable<EcheanceCalculee> ProtectionMaternite(
        SituationTravailleur situation, IReadOnlyList<Exposition> expositions, IReadOnlyList<Examen> examens, DateOnly dateCalcul, Func<Guid, bool> sorti)
    {
        foreach (var etat in situation.EtatsParticuliers.Where(e => e.PersonneId == situation.PersonneId && e.EstProtectionMaternite))
        {
            var realisation = PremierExamen(examens, TypeObligation.ProtectionMaternite, etat.DateDebut);
            var enCours = etat.DateFin is null || etat.DateFin >= dateCalcul;
            foreach (var parAffilie in expositions.Where(x => x.Chevauche(etat.DateDebut, etat.DateFin)).GroupBy(x => x.AffilieId))
            {
                if (realisation is null && (!enCours || sorti(parAffilie.Key)))
                {
                    continue;
                }

                var codes = parAffilie.Select(x => x.CodeRisque).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                yield return new EcheanceCalculee(
                    Cle("MATERNITE", parAffilie.Key, etat.EtatParticulierId.ToString("N")),
                    parAffilie.Key,
                    TypeObligation.ProtectionMaternite,
                    OrigineObligation.Evenement,
                    codes,
                    etat.DateDebut,
                    null,
                    realisation is null ? null : new Realisation(realisation.Date, realisation.Id),
                    new Justification(
                        "etat-particulier:PROTECTION_MATERNITE",
                        null,
                        $"Protection de la maternité déclarée à partir du {Jour(etat.DateDebut)} : examen et mesures liés aux risques du poste ({string.Join(", ", codes)}), dès la déclaration.",
                        Entrees(
                            ("etat_particulier", etat.EtatParticulierId.ToString()),
                            ("periode_debut", Jour(etat.DateDebut)),
                            ("periode_fin", etat.DateFin is { } f ? Jour(f) : "en cours"),
                            ("risques", string.Join(',', codes)),
                            ("realise_par", realisation is null ? "-" : $"{Jour(realisation.Date)} (examen {realisation.Id})"))));
            }
        }
    }

    private IEnumerable<EcheanceCalculee> EstimationsPotentiel(SituationTravailleur situation, IReadOnlyList<Examen> examens, Func<Guid, bool> sorti)
    {
        foreach (var incapacite in situation.Incapacites.Where(i => i.PersonneId == situation.PersonneId))
        {
            var duree = politiques.Duree(CodesParametres.EstimationPotentiel, incapacite.DateDebut);
            var due = duree.AjouterA(incapacite.DateDebut, calendrier);
            var realisation = PremierExamen(examens, TypeObligation.EstimationPotentielTravail, incapacite.DateDebut);
            var repriseAvant = situation.Reprises.FirstOrDefault(r => r.PersonneId == situation.PersonneId && !r.Annulee && r.DateReprise > incapacite.DateDebut && r.DateReprise <= due);
            if (realisation is null && (repriseAvant is not null || sorti(incapacite.AffilieId)))
            {
                continue;
            }

            yield return Evenement(
                Cle("ESTIMATION", incapacite.AffilieId, incapacite.IncapaciteId.ToString("N")),
                incapacite.AffilieId,
                TypeObligation.EstimationPotentielTravail,
                due,
                null,
                realisation,
                duree,
                $"Incapacité depuis le {Jour(incapacite.DateDebut)} : estimation du potentiel de travail après {duree.Valeur} {duree.Unite} (réglementation 2026).",
                ("incapacite", incapacite.IncapaciteId.ToString()),
                ("debut_incapacite", Jour(incapacite.DateDebut)),
                ("source", incapacite.Source),
                ("duree", duree.Description));
        }
    }

    private IEnumerable<EcheanceCalculee> EvaluationsReintegration(SituationTravailleur situation, IReadOnlyList<Examen> examens, Func<Guid, bool> sorti)
    {
        foreach (var trajet in situation.Trajets.Where(t => t.PersonneId == situation.PersonneId))
        {
            if (trajet.DateDemande is not { } dateDemande)
            {
                continue;
            }

            var delai = politiques.Duree(CodesParametres.InvitationReintegrationDelai, dateDemande);
            var realisation = PremierExamen(examens, TypeObligation.EvaluationReintegration, dateDemande);
            if (realisation is null && (trajet.DateFin is not null || sorti(trajet.AffilieId)))
            {
                continue;
            }

            yield return Evenement(
                Cle("REINTEGRATION", trajet.AffilieId, trajet.TrajetId.ToString("N")),
                trajet.AffilieId,
                TypeObligation.EvaluationReintegration,
                dateDemande,
                delai.AjouterA(dateDemande, calendrier),
                realisation,
                delai,
                $"Trajet de réintégration demandé le {Jour(dateDemande)} : invitation à l'évaluation de réintégration dans les {delai.Valeur} {delai.Unite}.",
                ("trajet", trajet.TrajetId.ToString()),
                ("date_demande", Jour(dateDemande)),
                ("delai", delai.Description));
        }
    }

    private static EcheanceCalculee Evenement(
        string cle, Guid affilieId, TypeObligation type, DateOnly dateDue, DateOnly? dateLimite, Examen? realisation, DureeLegale parametre,
        string explication, params (string Cle, string Valeur)[] entrees) =>
        new(
            cle,
            affilieId,
            type,
            OrigineObligation.Evenement,
            [],
            dateDue,
            dateLimite,
            realisation is null ? null : new Realisation(realisation.Date, realisation.Id),
            new Justification(
                $"parametre:{parametre.Code}",
                null,
                explication,
                Entrees([.. entrees, ("realise_par", realisation is null ? "-" : $"{Jour(realisation.Date)} (examen {realisation.Id})")])));

    private static RegleSurveillanceLocale? RegleAu(IReadOnlyList<RegleSurveillanceLocale> regles, string codeRisque, DateOnly date)
    {
        var duRisque = regles.Where(r => r.CodeRisque == codeRisque).OrderBy(r => r.ValideDu).ThenBy(r => r.Version).ToList();
        return duRisque.LastOrDefault(r => r.ValideDu <= date) ?? duRisque.FirstOrDefault();
    }

    /// <summary>AFF-13 : la surcharge du CPMT pour le travailleur prime sur celle du poste, qui prime sur la règle du risque.</summary>
    private static FrequenceEffective Frequence(SituationTravailleur situation, Exposition exposition, RegleSurveillanceLocale regle, DateOnly date)
    {
        var applicables = situation.Surcharges
            .Where(s => s.CodeRisque == exposition.CodeRisque && s.AffilieId == exposition.AffilieId && s.EstApplicableAu(date))
            .OrderBy(s => s.FrequenceMois)
            .ThenBy(s => s.SurchargeId)
            .ToList();

        var personne = applicables.FirstOrDefault(s => s.Cible(SurchargeFrequenceLocale.CiblePersonne) && s.CibleId == situation.PersonneId);
        if (personne is not null)
        {
            return new FrequenceEffective(personne.FrequenceMois, OrigineObligation.Surcharge, $"surcharge du CPMT pour le travailleur ({personne.SurchargeId}, du {Jour(personne.ValideDu)})");
        }

        var poste = applicables.FirstOrDefault(s => s.Cible(SurchargeFrequenceLocale.CiblePoste) && exposition.PosteIds.Contains(s.CibleId));
        if (poste is not null)
        {
            return new FrequenceEffective(poste.FrequenceMois, OrigineObligation.Surcharge, $"surcharge du CPMT pour le poste {poste.CibleId} ({poste.SurchargeId}, du {Jour(poste.ValideDu)})");
        }

        return new FrequenceEffective(regle.FrequenceMois, OrigineObligation.Regle, $"règle du risque, version {Nombre(regle.Version)}");
    }

    private static Examen? PremierExamen(IReadOnlyList<Examen> examens, TypeObligation type, DateOnly depuis) =>
        examens.FirstOrDefault(e => e.Type == type && e.Date >= depuis);

    /// <summary>Clé de l'obligation d'examen de reprise : lie le processus de reprise à son obligation.</summary>
    public static string CleReprise(Guid affilieId, DateOnly dateReprise) => Cle("REPRISE", affilieId, Jour(dateReprise));

    private static string Cle(string prefixe, Guid affilieId, params string[] parties) =>
        string.Join(':', [prefixe, affilieId.ToString("N"), .. parties]);

    private static string CleDemande(Guid demandeId) => $"DEMANDE:{demandeId:N}";

    private static List<KeyValuePair<string, string>> Entrees(params (string Cle, string Valeur)[] entrees) =>
        entrees.Select(e => new KeyValuePair<string, string>(e.Cle, e.Valeur)).ToList();

    private static string Jour(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Nombre(int valeur) => valeur.ToString(CultureInfo.InvariantCulture);

    private static DateOnly Max(DateOnly a, DateOnly b) => a >= b ? a : b;

    private sealed record Examen(Guid Id, TypeObligation Type, DateOnly Date);

    private sealed record Initiale(Exposition Exposition, RegleSurveillanceLocale Regle, TypeObligation Type, Examen? PremierExamen);

    private sealed record FrequenceEffective(int? Mois, OrigineObligation Origine, string Source);
}
