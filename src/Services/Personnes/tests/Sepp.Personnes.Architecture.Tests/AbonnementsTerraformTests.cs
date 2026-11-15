using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Sepp.Personnes.Adapters;
using Sepp.Personnes.Application;
using Sepp.Testing.Architecture;

using Shouldly;

namespace Sepp.Personnes.Architecture.Tests;

/// <summary>
/// Garde-fou : les rubriques souscrites par le code et <c>subscribes_to</c> de <c>infra/variables.tf</c> concordent dans les
/// deux sens (ARC-05, ADR 0015). Personnes n'a aucun gestionnaire d'événement : il ne doit s'abonner à aucune rubrique.
/// </summary>
public sealed class AbonnementsTerraformTests : AbonnementsTerraformRules
{
    protected override string Service => "personnes";

    protected override string ConnectionStringName => Sepp.Personnes.Adapters.DependencyInjection.ConnectionStringName;

    protected override bool SansGestionnaire => true;

    protected override IServiceCollection Composer(IConfiguration configuration) =>
        new ServiceCollection()
            .AddPersonnesApplication()
            .AddPersonnesAdapters(configuration);

    [Xunit.Fact]
    public void Aucun_abonnement_terraform_pour_un_service_sans_gestionnaire() =>
        VariablesTerraform.Lire().Services[Service].SubscribesTo.ShouldBeEmpty();
}
