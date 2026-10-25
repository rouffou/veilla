using System.Globalization;

using Microsoft.EntityFrameworkCore;

using Sepp.BuildingBlocks.Application.Republication;

namespace Sepp.BuildingBlocks.Infrastructure.Messaging;

public enum StatutRepublication
{
    EnCours,
    Terminee,
    Echouee,
}

/// <summary>
/// Republication de l'état courant d'un type d'objet (ADR 0011, ARC-31) : table <c>republication</c> du producteur. Le curseur
/// et le compteur avancent dans la même transaction que l'écriture des instantanés dans l'outbox : une reprise après panne
/// repart du dernier lot validé, sans perte ni doublon de lot.
/// </summary>
public sealed class DemandeRepublication
{
    public const int LongueurErreurMax = 500;

    private DemandeRepublication()
    {
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public StatutRepublication Statut { get; private set; }

    /// <summary>Position après le dernier lot écrit dans l'outbox ; <c>null</c> avant le premier lot.</summary>
    public string? Curseur { get; private set; }

    public int Publies { get; private set; }

    public int Lots { get; private set; }

    /// <summary>Identifiant (claim <c>sub</c>) de l'exploitant qui a demandé la republication.</summary>
    public string DemandeePar { get; private set; } = string.Empty;

    public DateTimeOffset DemandeeLe { get; private set; }

    public DateTimeOffset? TermineeLe { get; private set; }

    /// <summary>Motif technique d'échec, sans donnée métier.</summary>
    public string? Erreur { get; private set; }

    public static DemandeRepublication Demander(string type, string demandeePar, DateTimeOffset maintenant) => new()
    {
        Id = Guid.CreateVersion7(),
        Type = type,
        Statut = StatutRepublication.EnCours,
        DemandeePar = demandeePar,
        DemandeeLe = maintenant,
    };

    /// <summary>Enregistre un lot écrit dans l'outbox ; la republication se termine quand la source est épuisée.</summary>
    public void Avancer(int publies, string? curseurSuivant, DateTimeOffset maintenant)
    {
        if (Statut != StatutRepublication.EnCours)
        {
            throw new InvalidOperationException($"Republication {Id} déjà {Statut}.");
        }

        Publies += publies;
        Lots++;
        Curseur = curseurSuivant ?? Curseur;
        if (curseurSuivant is null)
        {
            Statut = StatutRepublication.Terminee;
            TermineeLe = maintenant;
        }
    }

    public void Echouer(string erreur, DateTimeOffset maintenant)
    {
        Statut = StatutRepublication.Echouee;
        Erreur = erreur.Length > LongueurErreurMax ? erreur[..LongueurErreurMax] : erreur;
        TermineeLe = maintenant;
    }

    public RepublicationDto Dto() => new(Id, Type, Statut.ToString(), Publies, Lots, DemandeePar, DemandeeLe, TermineeLe, Erreur);
}

/// <summary>Débit de la republication (ADR 0011). Valeurs par défaut prudentes, à valider en recette de charge.</summary>
public sealed class RepublicationOptions
{
    /// <summary>Instantanés lus et écrits dans l'outbox par lot (une transaction).</summary>
    public int TailleLot { get; set; } = 200;

    /// <summary>Intervalle entre deux lots : avec <see cref="TailleLot"/>, borne le débit (200 par seconde par défaut).</summary>
    public TimeSpan Intervalle { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Contre-pression : aucun lot n'est écrit tant que l'outbox compte au moins ce nombre de messages en attente, pour que
    /// les événements métier courants ne soient retardés que de quelques lots.
    /// </summary>
    public int ArriereOutboxMax { get; set; } = 1000;
}

/// <summary>Configuration EF Core de la table <c>republication</c>, à appeler par le <c>ConfigureModel</c> d'un producteur.</summary>
public static class RepublicationModel
{
    public static ModelBuilder AjouterRepublications(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DemandeRepublication>(b =>
        {
            b.ToTable("republication");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.Type).HasMaxLength(100);
            b.Property(r => r.Statut).HasConversion<string>().HasMaxLength(20);
            b.Property(r => r.Curseur).HasMaxLength(200);
            b.Property(r => r.DemandeePar).HasMaxLength(100);
            b.Property(r => r.Erreur).HasMaxLength(DemandeRepublication.LongueurErreurMax);

            // Une seule republication en cours par type, même si deux exploitants la demandent en même temps.
            b.HasIndex(r => r.Type).IsUnique().HasFilter("statut = 'EnCours'");
            b.HasIndex(r => r.DemandeeLe);
        });
        return modelBuilder;
    }
}

/// <summary>Curseur de parcours par identifiant (ordre de la clé primaire) pour les sources de republication.</summary>
public static class CurseurRepublication
{
    public static Guid? Lire(string? curseur) =>
        curseur is null ? null
        : Guid.TryParseExact(curseur, "D", out var id) ? id
        : throw new InvalidOperationException("Curseur de republication invalide.");

    public static string? Suivant<T>(IReadOnlyList<T> lot, int taille, Func<T, Guid> cle) =>
        lot.Count < taille ? null : cle(lot[^1]).ToString("D", CultureInfo.InvariantCulture);
}
