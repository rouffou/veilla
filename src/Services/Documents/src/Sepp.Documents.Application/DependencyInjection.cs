using Microsoft.Extensions.DependencyInjection;

using Sepp.BuildingBlocks.Application;
using Sepp.Documents.Application.Documents;
using Sepp.Documents.Application.EvaluationSante;
using Sepp.Documents.Application.Generation;
using Sepp.Documents.Application.Modeles;
using Sepp.Documents.Application.Securite;

namespace Sepp.Documents.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddDocumentsApplication(this IServiceCollection services)
    {
        services.AddScoped<AccesDocuments>();
        services.AddScoped<GenerateurDocuments>();
        services.AddScoped<InitialiserModelesParDefaut>();

        services.AddScoped<IQueryHandler<ListerModeles, IReadOnlyList<ModeleResumeDto>>, ListerModelesHandler>();
        services.AddScoped<IQueryHandler<ObtenirModele, ModeleDto>, ObtenirModeleHandler>();
        services.AddScoped<IQueryHandler<ApercuModele, IReadOnlyList<BlocDto>>, ApercuModeleHandler>();
        services.AddScoped<ICommandHandler<CreerModele, Guid>, CreerModeleHandler>();
        services.AddScoped<ICommandHandler<ModifierModele, Unit>, ModifierModeleHandler>();
        services.AddScoped<ICommandHandler<CreerNouvelleVersion, Guid>, CreerNouvelleVersionHandler>();
        services.AddScoped<ICommandHandler<ValiderModele, Unit>, ValiderModeleHandler>();
        services.AddScoped<ICommandHandler<RenvoyerModeleEnBrouillon, Unit>, RenvoyerModeleEnBrouillonHandler>();
        services.AddScoped<ICommandHandler<PublierModele, Unit>, PublierModeleHandler>();

        services.AddScoped<ICommandHandler<GenererDocument, DocumentDto>, GenererDocumentHandler>();
        services.AddScoped<ICommandHandler<PublierDocument, Unit>, PublierDocumentHandler>();
        services.AddScoped<ICommandHandler<SignerDocument, SignatureDto>, SignerDocumentHandler>();
        services.AddScoped<IQueryHandler<ObtenirDocument, DocumentDto>, ObtenirDocumentHandler>();
        services.AddScoped<IQueryHandler<ListerDocuments, IReadOnlyList<DocumentDto>>, ListerDocumentsHandler>();
        services.AddScoped<IQueryHandler<LireContenuDocument, ContenuDocumentDto>, LireContenuDocumentHandler>();
        services.AddScoped<IQueryHandler<VerifierIntegriteDocument, IntegriteDto>, VerifierIntegriteDocumentHandler>();
        return services;
    }
}
