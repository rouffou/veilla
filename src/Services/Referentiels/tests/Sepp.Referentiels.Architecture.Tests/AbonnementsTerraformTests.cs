using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Referentiels.Adapters;
using Sepp.Referentiels.Application;
using Sepp.Testing.Architecture;

using Shouldly;

namespace Sepp.Referentiels.Architecture.Tests;

/// <summary>
/// Garde-fou : les rubriques souscrites par le code et <c>subscribes_to</c> de <c>infra/variables.tf</c> concordent dans les
/// deux sens (ARC-05, ADR 0015). Referentiels n'a aucun gestionnaire d'événement : il ne doit s'abonner à aucune rubrique.
/// </summary>
public sealed class AbonnementsTerraformTests : AbonnementsTerraformRules
{
    protected override string Service => "referentiels";

    protected override string ConnectionStringName => Sepp.Referentiels.Adapters.DependencyInjection.ConnectionStringName;

    protected override bool SansGestionnaire => true;

    protected override IServiceCollection Composer(IConfiguration configuration) =>
        new ServiceCollection()
            .AddReferentielsApplication()
            .AddReferentielsAdapters(configuration);

    [Xunit.Fact]
    public void Aucun_abonnement_terraform_pour_un_service_sans_gestionnaire() =>
        VariablesTerraform.Lire().Services[Service].SubscribesTo.ShouldBeEmpty();
}
