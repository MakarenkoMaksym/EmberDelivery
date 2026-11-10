using System.Reflection;
using System.Text.RegularExpressions;

using EmberDelivery.ArchitectureTests.Rules;
using EmberDelivery.Billing.Api.Domain;
using EmberDelivery.Billing.Api.Infrastructure.Persistence;
using EmberDelivery.BuildingBlocks.Domain.Abstractions;

using FluentAssertions;

using NetArchTest.Rules;

using ArchTestResult = NetArchTest.Rules.TestResult;

namespace EmberDelivery.ArchitectureTests;

// Billing is a single vertical-slice project, so its layers are namespaces inside one assembly rather than separate
// projects; these rules restore the isolation that project references would otherwise enforce at compile time.
public sealed class BillingArchitectureTests
{
    private const string RootNamespace = "EmberDelivery.Billing.Api";
    private const string DomainNamespace = $"{RootNamespace}.Domain";
    private const string InfrastructureNamespace = $"{RootNamespace}.Infrastructure";
    private const string FeaturesNamespace = $"{RootNamespace}.Features";
    private const string ExtensionsNamespace = $"{RootNamespace}.Extensions";

    private static readonly Assembly BillingAssembly = typeof(BillingDbContext).Assembly;

    public static TheoryData<string> GetFeatureSlices()
    {
        TheoryData<string> slices = [];

        foreach (string slice in DiscoverFeatureSlices())
        {
            slices.Add(slice);
        }

        return slices;
    }

    [Fact]
    public void Domain_ShouldNotDependOnOtherNamespacesOrFrameworks()
    {
        ArchTestResult result = Types.InAssembly(BillingAssembly)
            .That()
            .ResideInNamespace(DomainNamespace)
            .Should()
            .NotHaveDependencyOnAny(
                InfrastructureNamespace,
                FeaturesNamespace,
                ExtensionsNamespace,
                "EmberDelivery.Billing.IntegrationEvents",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "MassTransit",
                "Npgsql")
            .GetResult();

        result.IsSuccessful.Should().BeTrue($"Billing Domain violated isolation: {result.DescribeFailingTypes()}");
    }

    [Fact]
    public void Infrastructure_ShouldNotDependOnFeaturesOrExtensions()
    {
        ArchTestResult result = Types.InAssembly(BillingAssembly)
            .That()
            .ResideInNamespace(InfrastructureNamespace)
            .Should()
            .NotHaveDependencyOnAny(FeaturesNamespace, ExtensionsNamespace)
            .GetResult();

        result.IsSuccessful.Should().BeTrue($"Billing Infrastructure depends on use-case code: {result.DescribeFailingTypes()}");
    }

    [Theory]
    [MemberData(nameof(GetFeatureSlices))]
    public void FeatureSlice_ShouldNotDependOnOtherSlices(string slice)
    {
        string[] otherSlices = DiscoverFeatureSlices()
            .Where(other => !string.Equals(other, slice, StringComparison.Ordinal))
            .Select(other => $"{FeaturesNamespace}.{other}")
            .ToArray();

        if (otherSlices.Length == 0)
        {
            return;
        }

        ArchTestResult result = Types.InAssembly(BillingAssembly)
            .That()
            .ResideInNamespaceMatching($"^{Regex.Escape($"{FeaturesNamespace}.{slice}")}(\\..+)?$")
            .Should()
            .NotHaveDependencyOnAny(otherSlices)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Slice '{slice}' must only share code through the Features root namespace: {result.DescribeFailingTypes()}");
    }

    [Fact]
    public void ValueObjects_ShouldBeTrulyImmutableAtIlFieldLevel()
    {
        IEnumerable<Type> valueObjects = Types.InAssembly(BillingAssembly)
            .That()
            .ImplementInterface(typeof(IValueObject))
            .GetTypes();

        // Guards against the rule passing vacuously if Money stops implementing the marker interface.
        valueObjects.Should().Contain(typeof(Money));

        ArchTestResult result = Types.InAssembly(BillingAssembly)
            .That()
            .ImplementInterface(typeof(IValueObject))
            .Should()
            .MeetCustomRule(new TrulyImmutableValueObjectRule())
            .GetResult();

        result.IsSuccessful.Should().BeTrue($"Billing has mutable value objects: {result.DescribeFailingTypes()}");
    }

    [Fact]
    public void AggregatesAndEntities_ShouldBeSealedWithPrivateParameterlessConstructor()
    {
        IEnumerable<Type> domainEntities = Types.InAssembly(BillingAssembly)
            .That()
            .ResideInNamespace(DomainNamespace)
            .And()
            .Inherit(typeof(Entity<Guid>))
            .GetTypes();

        List<string> violations = [];

        foreach (Type entityType in domainEntities)
        {
            if (!entityType.IsSealed)
            {
                violations.Add($"{entityType.Name} must be sealed.");
            }

            ConstructorInfo? parameterless = entityType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                Type.EmptyTypes);

            if (parameterless is null || !parameterless.IsPrivate)
            {
                violations.Add($"{entityType.Name} must declare a private parameterless constructor for EF Core.");
            }
        }

        violations.Should().BeEmpty();
    }

    [Fact]
    public void EfCoreConfigurations_ShouldInheritFromBaseConfigurationClasses()
    {
        IReadOnlyList<string> violations = EfConfigurationConventions.FindViolations(BillingAssembly);

        violations.Should().BeEmpty("Billing entity configurations must not bypass the shared base conventions");
    }

    [Fact]
    public void EveryDomainEntity_ShouldHaveAnExplicitEfCoreConfiguration()
    {
        IReadOnlyDictionary<Type, Type> configurations = EfConfigurationConventions.FindConfigurations(BillingAssembly);

        IEnumerable<Type> domainEntities = Types.InAssembly(BillingAssembly)
            .That()
            .ResideInNamespace(DomainNamespace)
            .And()
            .Inherit(typeof(Entity<Guid>))
            .GetTypes();

        // Without this, a new entity mapped purely by EF conventions would skip the base-class rule above entirely.
        domainEntities.Should().OnlyContain(
            entityType => configurations.ContainsKey(entityType),
            "every Billing aggregate and entity must be mapped by an AggregateRootConfiguration or EntityConfiguration");
    }

    private static IReadOnlyList<string> DiscoverFeatureSlices() =>
        BillingAssembly.GetTypes()
            .Select(type => type.Namespace)
            .OfType<string>()
            .Where(ns => ns.StartsWith($"{FeaturesNamespace}.", StringComparison.Ordinal))
            .Select(ns => ns[(FeaturesNamespace.Length + 1)..].Split('.')[0])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
}
