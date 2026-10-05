using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application;
using Sepp.Personnes.Application.Affectations;
using Sepp.Personnes.Application.EtatsParticuliers;
using Sepp.Personnes.Application.Imports;
using Sepp.Personnes.Application.Occupations;
using Sepp.Personnes.Application.Personnes;

namespace Sepp.Personnes.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPersonnesApplication(this IServiceCollection services)
    {
        services.AddScoped<PerimetreUtilisateur>();
        services.AddScoped<AccesPersonnes>();
        services.AddScoped<EnregistrementTravailleurs>();

        services.AddScoped<ICommandHandler<CreerPersonne, PersonneEnregistreeDto>, CreerPersonneHandler>();
        services.AddScoped<IQueryHandler<RechercherParNiss, PersonneResumeDto>, RechercherParNissHandler>();
        services.AddScoped<IQueryHandler<ObtenirPersonne, PersonneDto>, ObtenirPersonneHandler>();
        services.AddScoped<IQueryHandler<ListerTravailleurs, IReadOnlyList<PersonneResumeDto>>, ListerTravailleursHandler>();
        services.AddScoped<ICommandHandler<ModifierIdentite, Unit>, ModifierIdentiteHandler>();
        services.AddScoped<ICommandHandler<ModifierCoordonnees, Unit>, ModifierCoordonneesHandler>();

        services.AddScoped<IQueryHandler<ListerOccupations, IReadOnlyList<OccupationDto>>, ListerOccupationsHandler>();
        services.AddScoped<ICommandHandler<DebuterOccupation, Guid>, DebuterOccupationHandler>();
        services.AddScoped<ICommandHandler<TerminerOccupation, Unit>, TerminerOccupationHandler>();
        services.AddScoped<ICommandHandler<EnregistrerEntreeDimona, DimonaEnregistreeDto>, EnregistrerEntreeDimonaHandler>();
        services.AddScoped<ICommandHandler<EnregistrerSortieDimona, Unit>, EnregistrerSortieDimonaHandler>();

        services.AddScoped<IQueryHandler<ListerAffectations, IReadOnlyList<AffectationDto>>, ListerAffectationsHandler>();
        services.AddScoped<ICommandHandler<Affecter, Guid>, AffecterHandler>();
        services.AddScoped<ICommandHandler<TerminerAffectation, Unit>, TerminerAffectationHandler>();
        services.AddScoped<ICommandHandler<ChangerAffectation, Guid>, ChangerAffectationHandler>();

        services.AddScoped<IQueryHandler<ListerEtatsParticuliers, IReadOnlyList<EtatParticulierDto>>, ListerEtatsParticuliersHandler>();
        services.AddScoped<ICommandHandler<DeclarerEtatParticulier, Guid>, DeclarerEtatParticulierHandler>();
        services.AddScoped<ICommandHandler<TerminerEtatParticulier, Unit>, TerminerEtatParticulierHandler>();

        services.AddScoped<ICommandHandler<ImporterTravailleurs, RapportImportDto>, ImporterTravailleursHandler>();
        return services;
    }
}
