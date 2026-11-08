using System.Reflection;

using EmberDelivery.BuildingBlocks.Application.Messaging;
using EmberDelivery.BuildingBlocks.Domain.Abstractions;
using EmberDelivery.BuildingBlocks.Infrastructure.Persistence.Configurations;
using EmberDelivery.Ordering.Api.Endpoints;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;
using EmberDelivery.Ordering.Infrastructure.Persistence;

using FluentAssertions;

using FluentValidation;

using Microsoft.EntityFrameworkCore;

using Mono.Cecil;

using NetArchTest.Rules;

using ArchTestResult = NetArchTest.Rules.TestResult;

namespace EmberDelivery.ArchitectureTests;

public sealed class CleanArchitectureTests
{
    public static TheoryData<string, Assembly, Assembly, Assembly, Assembly> GetMicroserviceSlices() =>
        new()
        {
            {
                "Ordering",
                typeof(Order).Assembly,
                typeof(EmberDelivery.Ordering.Application.DependencyInjection).Assembly,
                typeof(OrderingDbContext).Assembly,
                typeof(EndpointTags).Assembly
            }
        };

    [Theory]
    [MemberData(nameof(GetMicroserviceSlices))]
    public void DomainLayer_ShouldNotDependOnOuterLayersOrFrameworks(
        string contextName,
        Assembly domainAssembly,
        Assembly applicationAssembly,
        Assembly infrastructureAssembly,
        Assembly apiAssembly)
    {
        ArchTestResult result = Types.InAssembly(domainAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                applicationAssembly.GetName().Name,
                infrastructureAssembly.GetName().Name,
                apiAssembly.GetName().Name,
                "Microsoft.EntityFrameworkCore",
                "MassTransit",
                "StackExchange.Redis")
            .GetResult();

        result.IsSuccessful.Should().BeTrue($"{contextName}.Domain violated layer isolation: {GetFailingTypes(result)}");
    }

    [Theory]
    [MemberData(nameof(GetMicroserviceSlices))]
    public void ApplicationLayer_ShouldNotDependOnInfrastructureOrApi(
        string contextName,
        Assembly domainAssembly,
        Assembly applicationAssembly,
        Assembly infrastructureAssembly,
        Assembly apiAssembly)
    {
        _ = domainAssembly;

        ArchTestResult result = Types.InAssembly(applicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                infrastructureAssembly.GetName().Name,
                apiAssembly.GetName().Name,
                "Microsoft.EntityFrameworkCore",
                "StackExchange.Redis")
            .GetResult();

        result.IsSuccessful.Should().BeTrue($"{contextName}.Application violated layer isolation: {GetFailingTypes(result)}");
    }

    [Theory]
    [MemberData(nameof(GetMicroserviceSlices))]
    public void ValueObjects_ShouldBeTrulyImmutableAtIlFieldLevel(
        string contextName,
        Assembly domainAssembly,
        Assembly applicationAssembly,
        Assembly infrastructureAssembly,
        Assembly apiAssembly)
    {
        _ = applicationAssembly;
        _ = infrastructureAssembly;
        _ = apiAssembly;

        ArchTestResult result = Types.InAssembly(domainAssembly)
            .That()
            .ImplementInterface(typeof(IValueObject))
            .Should()
            .MeetCustomRule(new TrulyImmutableValueObjectRule())
            .GetResult();

        result.IsSuccessful.Should().BeTrue($"{contextName}.Domain has mutable ValueObjects: {GetFailingTypes(result)}");
    }

    [Theory]
    [MemberData(nameof(GetMicroserviceSlices))]
    public void HandlersAndValidators_ShouldBeInternalSealed(
        string contextName,
        Assembly domainAssembly,
        Assembly applicationAssembly,
        Assembly infrastructureAssembly,
        Assembly apiAssembly)
    {
        _ = domainAssembly;
        _ = infrastructureAssembly;
        _ = apiAssembly;

        ArchTestResult handlersResult = Types.InAssembly(applicationAssembly)
            .That()
            .ImplementInterface(typeof(ICommandHandler<>))
            .Or()
            .ImplementInterface(typeof(ICommandHandler<,>))
            .Or()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .Or()
            .ImplementInterface(typeof(IDomainEventHandler<>))
            .Should()
            .NotBePublic()
            .And()
            .BeSealed()
            .GetResult();

        ArchTestResult validatorsResult = Types.InAssembly(applicationAssembly)
            .That()
            .Inherit(typeof(AbstractValidator<>))
            .Should()
            .NotBePublic()
            .And()
            .BeSealed()
            .GetResult();

        handlersResult.IsSuccessful.Should().BeTrue($"{contextName} handlers must be internal sealed: {GetFailingTypes(handlersResult)}");
        validatorsResult.IsSuccessful.Should().BeTrue($"{contextName} validators must be internal sealed: {GetFailingTypes(validatorsResult)}");
    }

    [Theory]
    [MemberData(nameof(GetMicroserviceSlices))]
    public void EfCoreConfigurations_ForDomainEntities_ShouldInheritFromBaseConfigurationClasses(
        string contextName,
        Assembly domainAssembly,
        Assembly applicationAssembly,
        Assembly infrastructureAssembly,
        Assembly apiAssembly)
    {
        _ = domainAssembly;
        _ = applicationAssembly;
        _ = apiAssembly;

        Type openConfigInterface = typeof(IEntityTypeConfiguration<>);
        List<string> violations = [];

        IEnumerable<Type> configurationTypes = infrastructureAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false });

        foreach (Type configType in configurationTypes)
        {
            Type? implementedConfigInterface = configType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == openConfigInterface);

            if (implementedConfigInterface is null)
            {
                continue;
            }

            Type targetModelType = implementedConfigInterface.GetGenericArguments()[0];

            if (targetModelType.IsAssignableTo(typeof(AggregateRoot<Guid>)))
            {
                Type expectedBase = typeof(AggregateRootConfiguration<>).MakeGenericType(targetModelType);
                if (!configType.IsAssignableTo(expectedBase))
                {
                    violations.Add($"{configType.Name} configures AggregateRoot '{targetModelType.Name}' and must inherit from AggregateRootConfiguration<{targetModelType.Name}>.");
                }
            }
            else if (targetModelType.IsAssignableTo(typeof(Entity<Guid>)))
            {
                Type expectedBase = typeof(EntityConfiguration<>).MakeGenericType(targetModelType);
                if (!configType.IsAssignableTo(expectedBase))
                {
                    violations.Add($"{configType.Name} configures Entity '{targetModelType.Name}' and must inherit from EntityConfiguration<{targetModelType.Name}>.");
                }
            }
        }

        violations.Should().BeEmpty($"{contextName}.Infrastructure has entity configurations bypassing base conventions");
    }

    private static string GetFailingTypes(ArchTestResult result) =>
        result.FailingTypeNames is null ? string.Empty : string.Join(", ", result.FailingTypeNames);

    private sealed class TrulyImmutableValueObjectRule : ICustomRule
    {
        public bool MeetsRule(TypeDefinition type)
        {
            return type.Fields
                .Where(f => !f.IsStatic)
                .All(f => f.IsInitOnly);
        }
    }
}
