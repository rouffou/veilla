namespace Sepp.Reintegration.Application.Minuteries;

public sealed record ResultatMinuteries(int DossiersTraites, int MinuteriesDeclenchees);

/// <summary>
/// ADR 0008, SAN-65 : traite un lot de minuteries échues (dossiers d'incapacité et trajets) au jour belge courant. Les
/// processus sont réservés par <c>FOR UPDATE SKIP LOCKED</c> dans la transaction de l'appelant : plusieurs instances ne
/// traitent jamais le même processus. Une minuterie déclenchée devient une alerte du tableau de bord (une seule fois).
/// </summary>
public sealed class TraiterMinuteries(
    IIncapaciteRepository incapacites, ITrajetRepository trajets, ParametresReintegration parametres, BuildingBlocks.Application.IUnitOfWork unitOfWork, TimeProvider horloge)
{
    public const int TailleLot = 50;

    public async Task<ResultatMinuteries> ExecuterLotAsync(CancellationToken cancellationToken)
    {
        var aujourdHui = horloge.Aujourdhui();
        var (traites, declenchees) = (0, 0);
        foreach (var incapacite in await incapacites.ReserverEchuesAsync(aujourdHui, TailleLot, cancellationToken))
        {
            declenchees += incapacite.DeclencherMinuteries(aujourdHui, await parametres.PolitiqueAsync(incapacite.DateDebut, cancellationToken)).Count;
            traites++;
        }

        foreach (var trajet in await trajets.ReserverEchusAsync(aujourdHui, TailleLot, cancellationToken))
        {
            declenchees += trajet.DeclencherMinuteries(aujourdHui, await parametres.PolitiqueAsync(trajet.DateDemande, cancellationToken)).Count;
            traites++;
        }

        if (traites > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new ResultatMinuteries(traites, declenchees);
    }
}
