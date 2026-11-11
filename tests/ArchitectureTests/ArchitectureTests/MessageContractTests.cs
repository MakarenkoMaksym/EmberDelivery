using System.Reflection;

using EmberDelivery.ArchitectureTests.Rules;
using EmberDelivery.Billing.IntegrationEvents;
using EmberDelivery.Dispatch.IntegrationEvents;
using EmberDelivery.Kitchen.IntegrationEvents;
using EmberDelivery.Ordering.IntegrationEvents;

using FluentAssertions;

namespace EmberDelivery.ArchitectureTests;

public sealed class MessageContractTests
{
    public static TheoryData<string, Assembly> GetIntegrationEventAssemblies() =>
        new()
        {
            { "Ordering", typeof(OrderSubmittedIntegrationEvent).Assembly },
            { "Billing", typeof(PaymentContracts).Assembly },
            { "Kitchen", typeof(KitchenSlotContracts).Assembly },
            { "Dispatch", typeof(CourierContracts).Assembly }
        };

    public static TheoryData<string, Assembly> GetApplicationAssemblies() =>
        new()
        {
            { "Ordering", typeof(EmberDelivery.Ordering.Application.DependencyInjection).Assembly }
        };

    [Theory]
    [MemberData(nameof(GetIntegrationEventAssemblies))]
    public void IntegrationContracts_ShouldBePublicSealedAndImmutable(string contextName, Assembly assembly)
    {
        IntegrationContractConventions.FindContractTypes(assembly)
            .Should()
            .NotBeEmpty($"{contextName}.IntegrationEvents must contain contracts");

        IntegrationContractConventions.FindViolations(assembly)
            .Should()
            .BeEmpty($"{contextName} messages are shared across consumers and must not be mutable in memory");
    }

    [Theory]
    [MemberData(nameof(GetIntegrationEventAssemblies))]
    public void IntegrationContracts_ShouldNotLeakDomainEntities(string contextName, Assembly assembly)
    {
        IReadOnlyList<Type> contracts = IntegrationContractConventions.FindContractTypes(assembly);

        ContractLeakageDetector.FindLeaks(contracts)
            .Should()
            .BeEmpty($"{contextName} integration events are a public schema and must not couple consumers to the domain model");
    }

    [Theory]
    [MemberData(nameof(GetApplicationAssemblies))]
    public void CommandsAndQueries_ShouldNotLeakDomainEntities(string contextName, Assembly assembly)
    {
        Type[] contracts = assembly.GetTypes().Where(ContractLeakageDetector.IsCommandOrQuery).ToArray();

        contracts.Should().NotBeEmpty($"{contextName}.Application must contain commands or queries");

        ContractLeakageDetector.FindLeaks(contracts)
            .Should()
            .BeEmpty($"{contextName} commands and queries must carry primitives or DTOs, never aggregates or entities");
    }
}
