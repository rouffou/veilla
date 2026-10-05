using System.Globalization;

using Sepp.BuildingBlocks.Application;
using Sepp.BuildingBlocks.Domain;
using Sepp.Integrations.Application.Externe;
using Sepp.Integrations.Domain;
using Sepp.Integrations.Domain.Correspondances;
using Sepp.Integrations.Domain.Flux;

namespace Sepp.Integrations.Application.Flux;

/// <summary>Bilan d'une exécution de flux (traitement planifié ou lancement à la demande).</summary>
public sealed record RapportExecutionDto(
    TypeFlux Flux,
    int Recus,
    int DejaRecus,
    int Traites,
    int Rejetes,
    int EnErreur,
    int Inconnus,
    string? ErreurRecuperation);

/// <summary>
/// Exécution d'un flux entrant (§12) : récupération auprès de l'organisme par son port, journalisation idempotente
/// de chaque message reçu (clé unique par flux), puis traitement des messages en attente. Sans contrôle de
/// permission : appelée par le planificateur (compte « system ») et par les cas d'usage d'administration, qui contrôlent.
/// </summary>
public sealed class ExecutionFlux(
    IJournalFluxRepository journal,
    IPositionFluxRepository positions,
    ICorrespondanceRepository correspondances,
    IRegistreBce registreBce,
    IFluxDimona fluxDimona,
    IRegistreNational registreNational,
    TraitementEchanges traitement,
    IUnitOfWork unitOfWork,
    TimeProvider horloge)
{
    /// <summary>Nombre maximal de messages traités par exécution (les suivants le seront à l'exécution suivante).</summary>
    public const int TailleLot = 500;

    public async Task<RapportExecutionDto> ExecuterAsync(TypeFlux flux, CancellationToken cancellationToken)
    {
        var compteur = new Compteur(flux);
        try
        {
            switch (flux)
            {
                case TypeFlux.Bce:
                    foreach (var affilie in await correspondances.ListerAsync(TypeIdentifiantExterne.NumeroBce, null, int.MaxValue, cancellationToken))
                    {
                        await ConsulterBceAsync(affilie.ValeurExterne, compteur, cancellationToken);
                    }

                    break;
                case TypeFlux.Dimona:
                    await RecevoirLotAsync(flux, fluxDimona.RecupererDeclarationsAsync, Journaliser, compteur, cancellationToken);
                    break;
                case TypeFlux.RegistreNational:
                    await RecevoirLotAsync(flux, registreNational.RecupererMutationsAsync, Journaliser, compteur, cancellationToken);
                    break;
                default:
                    throw new DomainException($"Flux inconnu : {flux}.");
            }
        }
        catch (NotImplementedException ex)
        {
            compteur.ErreurRecuperation = ex.Message;
        }
#pragma warning disable CA1031 // Indisponibilité de l'organisme : rapportée, l'exécution suivante reprend à la même position.
        catch (Exception ex) when (ex is not (OperationCanceledException or DomainException))
#pragma warning restore CA1031
        {
            compteur.ErreurRecuperation = $"Récupération impossible ({ex.GetType().Name}).";
        }

        // Messages reçus (y compris lors d'une exécution précédente interrompue) et pas encore traités.
        foreach (var echange in await journal.ListerEnAttenteAsync(flux, TailleLot, cancellationToken))
        {
            await traitement.TraiterAsync(echange, cancellationToken);
            compteur.Compter(echange.Statut);
        }

        return compteur.Rapport();
    }

    /// <summary>BCE à la demande pour une entreprise (lancement manuel ou affilié nouvellement connu).</summary>
    public async Task<Result<EchangeFlux>> ConsulterBceAsync(string numeroBce, CancellationToken cancellationToken)
    {
        if (!NumerosBce.EstEntrepriseValide(numeroBce, out var numero))
        {
            return Error.Validation("bce.numero-invalide", $"Numéro d'entreprise BCE invalide : '{numeroBce}'.");
        }

        var echange = await ConsulterBceAsync(numero, new Compteur(TypeFlux.Bce), cancellationToken);
        if (echange is null)
        {
            return Error.NotFound("bce.entreprise-inconnue", $"L'entreprise {NumerosBce.Formater(numero)} est inconnue de la BCE.");
        }

        if (echange.Statut == StatutEchange.Recu)
        {
            await traitement.TraiterAsync(echange, cancellationToken);
        }

        return echange;
    }

    private async Task<EchangeFlux?> ConsulterBceAsync(string numero, Compteur compteur, CancellationToken cancellationToken)
    {
        var donnees = await registreBce.ConsulterEntrepriseAsync(numero, cancellationToken);
        if (donnees is null)
        {
            compteur.Inconnus++;
            return null;
        }

        // Clé : entreprise, date d'extraction et empreinte du contenu ; une consultation identique le même jour n'est pas rejournalisée.
        var chargeUtile = ChargesUtiles.Serialiser(donnees);
        var cle = $"bce:{numero}:{donnees.DateExtraction.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}:{ChargesUtiles.Empreinte(chargeUtile)}";
        var existant = await journal.GetParCleAsync(TypeFlux.Bce, cle, cancellationToken);
        if (existant is not null)
        {
            compteur.DejaRecus++;
            return existant;
        }

        var echange = EchangeFlux.Recevoir(TypeFlux.Bce, SensFlux.Entrant, TypesMessage.EntrepriseBce, cle, numero,
            1 + (donnees.UnitesEtablissement?.Count ?? 0), chargeUtile, horloge.GetUtcNow());
        journal.Add(echange);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        compteur.Recus++;
        return echange;
    }

    private async Task RecevoirLotAsync<T>(
        TypeFlux flux,
        Func<string?, CancellationToken, Task<LotFlux<T>>> recuperer,
        Func<T, string, (string TypeMessage, string Cle, string? Reference)> journaliser,
        Compteur compteur,
        CancellationToken cancellationToken)
    {
        var position = await positions.GetAsync(flux, cancellationToken);
        var lot = await recuperer(position?.Position, cancellationToken);
        if (position is null)
        {
            position = PositionFlux.Initialiser(flux);
            positions.Add(position);
        }

        var maintenant = horloge.GetUtcNow();
        var clesDuLot = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in lot.Elements)
        {
            var chargeUtile = ChargesUtiles.Serialiser(message);
            var (typeMessage, cle, reference) = journaliser(message, chargeUtile);
            if (!clesDuLot.Add(cle) || await journal.ExisteAsync(flux, cle, cancellationToken))
            {
                compteur.DejaRecus++;
                continue;
            }

            journal.Add(EchangeFlux.Recevoir(flux, SensFlux.Entrant, typeMessage, cle, reference, 1, chargeUtile, maintenant));
            compteur.Recus++;
        }

        // Réception du lot et avancement de la position dans la même transaction (ARC-22).
        position.Avancer(lot.PositionSuivante ?? position.Position, maintenant);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Clé d'idempotence d'une déclaration : sa référence DIMONA (jamais le NISS). Une référence illisible est journalisée
    /// sous l'empreinte de son contenu et sera rejetée au traitement, sans bloquer le reste du lot.
    /// </summary>
    private static (string, string, string?) Journaliser(DeclarationDimona d, string chargeUtile)
    {
        var type = d.Type == TypeDeclarationDimona.Entree ? TypesMessage.EntreeDimona : TypesMessage.SortieDimona;
        if (!ReferenceLisible(d.ReferenceDimona, out var reference))
        {
            return (type, $"illisible:{ChargesUtiles.Empreinte(chargeUtile)}", null);
        }

        return d.Type == TypeDeclarationDimona.Entree
            ? (type, $"entree:{reference}", reference)
            : (type, $"sortie:{reference}:{d.DateFin?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}", reference);
    }

    private static (string, string, string?) Journaliser(MutationRegistreNational m, string chargeUtile)
    {
        var reference = m.ReferenceMutation?.Trim() ?? string.Empty;
        return reference.Length is 0 or > 100 || DonneesPersonnelles.Masquer(reference) != reference
            ? (TypesMessage.MutationIdentite, $"illisible:{ChargesUtiles.Empreinte(chargeUtile)}", null)
            : (TypesMessage.MutationIdentite, $"mutation:{reference}", reference);
    }

    private static bool ReferenceLisible(string? valeur, out string reference)
    {
        try
        {
            reference = CorrespondanceIdentifiant.Normaliser(TypeIdentifiantExterne.ReferenceDimona, valeur);
            return DonneesPersonnelles.Masquer(reference) == reference;
        }
        catch (DomainException)
        {
            reference = string.Empty;
            return false;
        }
    }

    private sealed class Compteur(TypeFlux flux)
    {
        public int Recus { get; set; }

        public int DejaRecus { get; set; }

        public int Inconnus { get; set; }

        public string? ErreurRecuperation { get; set; }

        private int Traites { get; set; }

        private int Rejetes { get; set; }

        private int EnErreur { get; set; }

        public void Compter(StatutEchange statut)
        {
            switch (statut)
            {
                case StatutEchange.Traite:
                    Traites++;
                    break;
                case StatutEchange.Rejete:
                    Rejetes++;
                    break;
                case StatutEchange.EnErreur:
                    EnErreur++;
                    break;
            }
        }

        public RapportExecutionDto Rapport() =>
            new(flux, Recus, DejaRecus, Traites, Rejetes, EnErreur, Inconnus, ErreurRecuperation is null ? null : DonneesPersonnelles.Masquer(ErreurRecuperation));
    }
}

/// <summary>Purge des charges utiles (minimisation RGPD) selon <see cref="ConservationChargesUtiles"/>.</summary>
public sealed class PurgeChargesUtiles(IJournalFluxRepository journal, IUnitOfWork unitOfWork, ConservationChargesUtiles conservation, TimeProvider horloge)
{
    public async Task<int> ExecuterAsync(CancellationToken cancellationToken)
    {
        var maintenant = horloge.GetUtcNow();
        var total = 0;
        while (true)
        {
            var echanges = await journal.ListerChargesAPurgerAsync(maintenant - conservation.ApresTraitement, maintenant - conservation.ApresEchec, 200, cancellationToken);
            var purges = echanges.Count(e => e.PurgerChargeUtile(maintenant));
            if (purges == 0)
            {
                return total;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            total += purges;
        }
    }
}
