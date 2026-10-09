using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Sepp.Affilies.Application.Affilies;
using Sepp.Affilies.Application.Bce;
using Sepp.Affilies.Application.Concertation;
using Sepp.Affilies.Application.Groupes;
using Sepp.Affilies.Application.Hierarchie;
using Sepp.Affilies.Application.Historique;
using Sepp.Affilies.Application.Operations;
using Sepp.Affilies.Application.Securite;
using Sepp.BuildingBlocks.Application;

namespace Sepp.Affilies.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAffiliesApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ControleAcces>();
        services.AddScoped<ModificateurAffilie>();

        // AFF-01, AFF-05 — Fiche et historique.
        services.AddScoped<ICommandHandler<CreerAffilie, Guid>, CreerAffilieHandler>();
        services.AddScoped<ICommandHandler<ModifierFiche, Unit>, ModifierFicheHandler>();
        services.AddScoped<ICommandHandler<ResilierAffiliation, Unit>, ResilierAffiliationHandler>();
        services.AddScoped<ICommandHandler<AnnulerResiliation, Unit>, AnnulerResiliationHandler>();
        services.AddScoped<IQueryHandler<ObtenirAffilie, AffilieDto>, ObtenirAffilieHandler>();
        services.AddScoped<IQueryHandler<ObtenirAffilieParBce, AffilieDto>, ObtenirAffilieParBceHandler>();
        services.AddScoped<IQueryHandler<RechercherAffilies, PageDto<AffilieResumeDto>>, RechercherAffiliesHandler>();
        services.AddScoped<IQueryHandler<ConsulterHistorique, IReadOnlyList<ModificationDto>>, ConsulterHistoriqueHandler>();

        // AFF-02 — Groupes et hiérarchie.
        services.AddScoped<IQueryHandler<ListerGroupes, IReadOnlyList<GroupeDto>>, ListerGroupesHandler>();
        services.AddScoped<ICommandHandler<CreerGroupe, Guid>, CreerGroupeHandler>();
        services.AddScoped<ICommandHandler<RenommerGroupe, Unit>, RenommerGroupeHandler>();
        services.AddScoped<ICommandHandler<AjouterUniteEtablissement, Guid>, AjouterUniteEtablissementHandler>();
        services.AddScoped<ICommandHandler<ModifierUniteEtablissement, Unit>, ModifierUniteEtablissementHandler>();
        services.AddScoped<ICommandHandler<FermerUniteEtablissement, Unit>, FermerUniteEtablissementHandler>();
        services.AddScoped<ICommandHandler<AjouterSite, Guid>, AjouterSiteHandler>();
        services.AddScoped<ICommandHandler<ModifierSite, Unit>, ModifierSiteHandler>();
        services.AddScoped<ICommandHandler<FermerSite, Unit>, FermerSiteHandler>();
        services.AddScoped<ICommandHandler<AjouterDepartement, Guid>, AjouterDepartementHandler>();
        services.AddScoped<ICommandHandler<ModifierDepartement, Unit>, ModifierDepartementHandler>();
        services.AddScoped<ICommandHandler<FermerDepartement, Unit>, FermerDepartementHandler>();

        // AFF-03, AFF-04 — Contacts et organes de concertation.
        services.AddScoped<ICommandHandler<AjouterContact, Guid>, AjouterContactHandler>();
        services.AddScoped<ICommandHandler<ModifierContact, Guid>, ModifierContactHandler>();
        services.AddScoped<ICommandHandler<TerminerContact, Unit>, TerminerContactHandler>();
        services.AddScoped<ICommandHandler<InstallerOrgane, Guid>, InstallerOrganeHandler>();
        services.AddScoped<ICommandHandler<DissoudreOrgane, Unit>, DissoudreOrganeHandler>();
        services.AddScoped<ICommandHandler<PlanifierReunion, Guid>, PlanifierReunionHandler>();
        services.AddScoped<ICommandHandler<ModifierReunion, Unit>, ModifierReunionHandler>();

        // AFF-01, AFF-02, INT-04 — Mise à jour depuis la BCE (donnees-bce-recues) et écarts du gestionnaire de dossiers.
        services.AddScoped<IQueryHandler<ListerEcartsBce, IReadOnlyList<EcartBceDto>>, ListerEcartsBceHandler>();
        services.AddScoped<ICommandHandler<ResoudreEcartBce, Unit>, ResoudreEcartBceHandler>();

        // AFF-06 — Fusion, scission, transfert.
        services.AddScoped<ICommandHandler<ProjeterOperation, Guid>, ProjeterOperationHandler>();
        services.AddScoped<ICommandHandler<RealiserOperation, Unit>, RealiserOperationHandler>();
        services.AddScoped<ICommandHandler<AnnulerOperation, Unit>, AnnulerOperationHandler>();
        return services;
    }
}
