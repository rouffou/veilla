using Sepp.Referentiels.Domain.Calendrier;
using Sepp.Referentiels.Domain.Nomenclatures;
using Sepp.Referentiels.Domain.Parametres;

namespace Sepp.Referentiels.Application;

public interface IParametreLegalRepository
{
    Task<ParametreLegal?> GetAsync(CodeParametre code, CancellationToken cancellationToken);

    Task<IReadOnlyList<ParametreLegal>> ListAsync(CancellationToken cancellationToken);

    void Add(ParametreLegal parametre);
}

public interface INomenclatureRepository
{
    Task<Nomenclature?> GetAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<Nomenclature>> ListAsync(CancellationToken cancellationToken);

    void Add(Nomenclature nomenclature);
}

public interface ICalendrierRepository
{
    Task<CalendrierAnnuel?> GetAsync(int annee, CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendrierAnnuel>> ListAsync(int anneeDebut, int anneeFin, CancellationToken cancellationToken);

    void Add(CalendrierAnnuel calendrier);
}
