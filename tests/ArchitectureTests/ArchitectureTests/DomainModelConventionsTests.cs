using System.Reflection;

using EmberDelivery.ArchitectureTests.Rules;
using EmberDelivery.Billing.Api.Domain.PaymentHolds;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;

using FluentAssertions;

namespace EmberDelivery.ArchitectureTests;

public sealed class DomainModelConventionsTests
{
    public static TheoryData<string, Assembly, string> GetDomainModels() =>
        new()
        {
            { "Ordering", typeof(Order).Assembly, "EmberDelivery.Ordering.Domain" },
            { "Billing", typeof(PaymentHold).Assembly, "EmberDelivery.Billing.Api.Domain" }
        };

    [Theory]
    [MemberData(nameof(GetDomainModels))]
    public void AggregatesAndEntities_ShouldBeSealed(string contextName, Assembly assembly, string domainNamespace)
    {
        IReadOnlyList<Type> entities = FindEntities(contextName, assembly, domainNamespace);

        entities.Where(type => !type.IsSealed)
            .Select(type => type.Name)
            .Should()
            .BeEmpty($"{contextName} aggregates and entities must be sealed");
    }

    [Theory]
    [MemberData(nameof(GetDomainModels))]
    public void AggregatesAndEntities_ShouldNotExposePublicOrInitSetters(string contextName, Assembly assembly, string domainNamespace)
    {
        IReadOnlyList<Type> entities = FindEntities(contextName, assembly, domainNamespace);

        entities.SelectMany(DomainEntityConventions.FindSetterViolations)
            .Should()
            .BeEmpty($"{contextName} entity state must only be mutated through intention-revealing methods");
    }

    [Theory]
    [MemberData(nameof(GetDomainModels))]
    public void AggregatesAndEntities_ShouldOnlyDeclareNonPublicConstructors(string contextName, Assembly assembly, string domainNamespace)
    {
        IReadOnlyList<Type> entities = FindEntities(contextName, assembly, domainNamespace);

        entities.SelectMany(DomainEntityConventions.FindConstructorViolations)
            .Should()
            .BeEmpty($"{contextName} entities need an EF Core constructor and no public constructor that bypasses factory invariants");
    }

    private static IReadOnlyList<Type> FindEntities(string contextName, Assembly assembly, string domainNamespace)
    {
        IReadOnlyList<Type> entities = DomainEntityConventions.FindConcreteEntities(assembly, domainNamespace);

        // Guards against every rule passing vacuously if discovery stops matching the domain model.
        entities.Should().NotBeEmpty($"{contextName} domain must contain aggregates or entities");

        return entities;
    }
}
