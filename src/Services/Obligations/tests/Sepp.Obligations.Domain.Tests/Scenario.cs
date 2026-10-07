using Sepp.Obligations.Domain.Calcul;
using Sepp.Obligations.Domain.Demandes;
using Sepp.Obligations.Domain.Obligations;
using Sepp.Obligations.Domain.Projections;

namespace Sepp.Obligations.Domain.Tests;

/// <summary>Construit la situation d'un travailleur à partir de projections locales, comme le feraient les événements.</summary>
internal sealed class Scenario
{
    public static readonly DateTimeOffset T0 = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly Aujourdhui = new(2026, 6, 15);

    public Guid Personne { get; } = Guid.CreateVersion7();

    public Guid Affilie { get; } = Guid.CreateVersion7();

    public Guid Poste { get; } = Guid.CreateVersion7();

    public List<AffectationLocale> Affectations { get; } = [];

    public List<ProfilRisquePosteLocal> Profils { get; } = [];

    public List<RegleSurveillanceLocale> Regles { get; } = [];

    public List<SurchargeFrequenceLocale> Surcharges { get; } = [];

    public List<OccupationLocale> Occupations { get; } = [];

    public List<EtatParticulierLocal> Etats { get; } = [];

    public List<ExamenLocal> Examens { get; } = [];

    public List<RepriseLocale> Reprises { get; } = [];

    public List<IncapaciteLocale> Incapacites { get; } = [];

    public List<TrajetLocal> Trajets { get; } = [];

    public List<DemandeTravailleur> Demandes { get; } = [];

    public List<ParametreLegalLocal> Parametres { get; } = [];

    public List<CalendrierLocal> Calendriers { get; } = [];

    public Scenario Affecte(DateOnly debut, DateOnly? fin = null, Guid? poste = null)
    {
        Affectations.Add(new AffectationLocale(Guid.CreateVersion7(), Personne, poste ?? Poste, debut, fin, T0));
        return this;
    }

    public Scenario ProfilPoste(DateOnly valideDu, params string[] codes)
    {
        Profils.Add(new ProfilRisquePosteLocal(Poste, valideDu, Affilie, codes, T0));
        return this;
    }

    public Scenario Regle(string code, string type, int? frequenceMois, bool prolongee = false, int version = 1, DateOnly? valideDu = null)
    {
        Regles.Add(new RegleSurveillanceLocale(code, version, Guid.CreateVersion7(), "Physique", type, frequenceMois, prolongee, valideDu ?? new DateOnly(2026, 1, 1)));
        return this;
    }

    public Scenario Surcharge(string cible, Guid cibleId, string code, int frequenceMois, DateOnly valideDu, DateOnly? jusquAu = null)
    {
        Surcharges.Add(new SurchargeFrequenceLocale(Guid.CreateVersion7(), Affilie, cible, cibleId, code, frequenceMois, valideDu, jusquAu, T0));
        return this;
    }

    public Scenario Occupe(DateOnly debut, DateOnly? fin = null)
    {
        var occupation = new OccupationLocale(Guid.CreateVersion7(), Personne, Affilie);
        occupation.Debuter(debut);
        if (fin is { } f)
        {
            occupation.Terminer(f, T0);
        }

        Occupations.Add(occupation);
        return this;
    }

    public Guid Examen(string type, DateOnly date)
    {
        var examen = new ExamenLocal(Guid.CreateVersion7(), Personne, Affilie, type, date);
        Examens.Add(examen);
        return examen.ExamenId;
    }

    public Scenario Reprise(DateOnly reprise, DateOnly debutAbsence)
    {
        Reprises.Add(new RepriseLocale(Personne, Affilie, reprise, debutAbsence, T0));
        return this;
    }

    public Scenario Incapacite(DateOnly debut)
    {
        Incapacites.Add(new IncapaciteLocale(Guid.CreateVersion7(), Personne, Affilie, debut, "MUTUALITE"));
        return this;
    }

    public Scenario Trajet(DateOnly demande, DateOnly? fin = null)
    {
        var trajet = new TrajetLocal(Guid.CreateVersion7(), Personne, Affilie);
        trajet.Demarrer(demande);
        if (fin is { } f)
        {
            trajet.Terminer(f, "TERMINE");
        }

        Trajets.Add(trajet);
        return this;
    }

    public Scenario Demande(TypeObligation type, DateOnly date)
    {
        Demandes.Add(DemandeTravailleur.Enregistrer(Personne, Affilie, type, date, date.AddDays(1), "agent-1"));
        return this;
    }

    public Scenario Maternite(DateOnly debut, DateOnly? fin = null)
    {
        Etats.Add(new EtatParticulierLocal(Guid.CreateVersion7(), Personne, EtatParticulierLocal.ProtectionMaternite, debut, fin, T0));
        return this;
    }

    public Scenario Parametre(string code, decimal valeur, string unite, DateOnly valideDu, DateOnly? valideJusquAu = null)
    {
        Parametres.Add(new ParametreLegalLocal(code, valideDu, valideJusquAu, valeur, unite, T0));
        return this;
    }

    public Scenario JourFerie(DateOnly date)
    {
        Calendriers.Add(new CalendrierLocal(date.Year, [date], T0));
        return this;
    }

    public SituationTravailleur Situation() =>
        new(Personne, Affectations, Profils, Regles, Surcharges, Occupations, Etats, Examens, Reprises, Incapacites, Trajets, Demandes);

    public MoteurEcheances Moteur(OptionsCalcul? options = null) =>
        new(new PolitiquesLegales(Parametres), CalendrierOuvrable.Construire(Calendriers, 2020, 2035), options ?? new OptionsCalcul());

    public ResultatCalcul Calculer(DateOnly? date = null, OptionsCalcul? options = null) => Moteur(options).Calculer(Situation(), date ?? Aujourdhui);
}
