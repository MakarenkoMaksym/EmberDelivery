using System.Reflection;

using EmberDelivery.ArchitectureTests.Rules;
using EmberDelivery.BuildingBlocks.Application.Messaging;
using EmberDelivery.BuildingBlocks.Domain.Abstractions;
using EmberDelivery.Ordering.Api.Endpoints;
using EmberDelivery.Ordering.Domain.Aggregates.Orders;
using EmberDelivery.Ordering.Infrastructure.Persistence;

using FluentAssertions;

using FluentValidation;

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

        result.IsSuccessful.Should().BeTrue($"{contextName}.Domain violated layer isolation: {result.DescribeFailingTypes()}");
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

        result.IsSuccessful.Should().BeTrue($"{contextName}.Application violated layer isolation: {result.DescribeFailingTypes()}");
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

        result.IsSuccessful.Should().BeTrue($"{contextName}.Domain has mutable ValueObjects: {result.DescribeFailingTypes()}");
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

        handlersResult.IsSuccessful.Should().BeTrue($"{contextName} handlers must be internal sealed: {handlersResult.DescribeFailingTypes()}");
        validatorsResult.IsSuccessful.Should().BeTrue($"{contextName} validators must be internal sealed: {validatorsResult.DescribeFailingTypes()}");
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

        IReadOnlyList<string> violations = EfConfigurationConventions.FindViolations(infrastructureAssembly);

        violations.Should().BeEmpty($"{contextName}.Infrastructure has entity configurations bypassing base conventions");
    }
}
