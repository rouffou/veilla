using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application;
using Sepp.PostesRisques.Application.Listes;
using Sepp.PostesRisques.Application.Postes;
using Sepp.PostesRisques.Application.Risques;
using Sepp.PostesRisques.Application.Surcharges;

namespace Sepp.PostesRisques.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPostesRisquesApplication(this IServiceCollection services)
    {
        // AFF-10 : catalogue des postes.
        services.AddScoped<IQueryHandler<ListerPostes, IReadOnlyList<PosteDto>>, ListerPostesHandler>();
        services.AddScoped<IQueryHandler<ObtenirPoste, PosteDto>, ObtenirPosteHandler>();
        services.AddScoped<IQueryHandler<ObtenirProfilRisques, ProfilRisquesDto>, ObtenirProfilRisquesHandler>();
        services.AddScoped<ICommandHandler<CreerPoste, Guid>, CreerPosteHandler>();
        services.AddScoped<ICommandHandler<ModifierPoste, Unit>, ModifierPosteHandler>();
        services.AddScoped<ICommandHandler<ArchiverPoste, Unit>, ArchiverPosteHandler>();
        services.AddScoped<ICommandHandler<ReactiverPoste, Unit>, ReactiverPosteHandler>();

        // AFF-14 : propositions de modification du lien poste ↔ risque.
        services.AddScoped<IQueryHandler<ListerPropositionsPosteRisque, IReadOnlyList<PropositionPosteRisqueDto>>, ListerPropositionsPosteRisqueHandler>();
        services.AddScoped<IQueryHandler<ObtenirPropositionPosteRisque, PropositionPosteRisqueDto>, ObtenirPropositionPosteRisqueHandler>();
        services.AddScoped<ICommandHandler<SoumettrePropositionPosteRisque, Guid>, SoumettrePropositionPosteRisqueHandler>();
        services.AddScoped<ICommandHandler<JoindreAvisCppt, Unit>, JoindreAvisCpptHandler>();
        services.AddScoped<ICommandHandler<ValiderPropositionPosteRisque, Unit>, ValiderPropositionPosteRisqueHandler>();
        services.AddScoped<ICommandHandler<RefuserPropositionPosteRisque, Unit>, RefuserPropositionPosteRisqueHandler>();

        // AFF-11, AFF-12 : référentiel des risques et règles de surveillance.
        services.AddScoped<IQueryHandler<ListerRisques, IReadOnlyList<RisqueDto>>, ListerRisquesHandler>();
        services.AddScoped<IQueryHandler<ObtenirRisque, RisqueDto>, ObtenirRisqueHandler>();
        services.AddScoped<ICommandHandler<CreerRisque, Guid>, CreerRisqueHandler>();
        services.AddScoped<ICommandHandler<DefinirRegleSurveillance, int>, DefinirRegleSurveillanceHandler>();
        services.AddScoped<ICommandHandler<ImporterRisques, ImportRisquesDto>, ImporterRisquesHandler>();

        // AFF-13 : surcharges de fréquence.
        services.AddScoped<IQueryHandler<ListerSurchargesFrequence, IReadOnlyList<SurchargeDto>>, ListerSurchargesFrequenceHandler>();
        services.AddScoped<ICommandHandler<DefinirSurchargeFrequence, Guid>, DefinirSurchargeFrequenceHandler>();
        services.AddScoped<ICommandHandler<CloturerSurchargeFrequence, Unit>, CloturerSurchargeFrequenceHandler>();

        // AFF-30, AFF-31 : listes nominatives et propositions de modification.
        services.AddScoped<CalculListesNominatives>();
        services.AddScoped<PolitiqueConservationListes>();
        services.AddScoped<IQueryHandler<ListerListesNominatives, IReadOnlyList<ListeNominativeDto>>, ListerListesNominativesHandler>();
        services.AddScoped<IQueryHandler<ObtenirListeNominative, ListeNominativeDto>, ObtenirListeNominativeHandler>();
        services.AddScoped<ICommandHandler<GenererListeNominative, ListeGenereeDto>, GenererListeNominativeHandler>();
        services.AddScoped<ICommandHandler<AssocierDocumentListe, Unit>, AssocierDocumentListeHandler>();
        services.AddScoped<IQueryHandler<ListerPropositionsListe, IReadOnlyList<PropositionListeDto>>, ListerPropositionsListeHandler>();
        services.AddScoped<IQueryHandler<ObtenirPropositionListe, PropositionListeDto>, ObtenirPropositionListeHandler>();
        services.AddScoped<ICommandHandler<SoumettrePropositionListe, Guid>, SoumettrePropositionListeHandler>();
        services.AddScoped<ICommandHandler<ValiderPropositionListe, ListeGenereeDto>, ValiderPropositionListeHandler>();
        services.AddScoped<ICommandHandler<RefuserPropositionListe, Unit>, RefuserPropositionListeHandler>();
        return services;
    }
}
