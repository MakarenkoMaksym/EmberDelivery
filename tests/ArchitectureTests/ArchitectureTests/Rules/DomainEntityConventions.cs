using System.Reflection;

using EmberDelivery.BuildingBlocks.Domain.Abstractions;

namespace EmberDelivery.ArchitectureTests.Rules;

internal static class DomainEntityConventions
{
    private const BindingFlags AllProperties =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private const BindingFlags AllInstanceConstructors =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // Matches on the open generic so aggregates keyed by something other than Guid are not silently skipped.
    public static bool IsDomainEntity(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Entity<>))
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<Type> FindConcreteEntities(Assembly assembly, string namespacePrefix) =>
        assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.Namespace?.StartsWith(namespacePrefix, StringComparison.Ordinal) ?? false)
            .Where(IsDomainEntity)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

    // Inherited members are included on purpose: a base class with a public setter leaks into every aggregate.
    public static IEnumerable<string> FindSetterViolations(Type entityType)
    {
        foreach (PropertyInfo property in entityType.GetProperties(AllProperties))
        {
            MethodInfo? setter = property.GetSetMethod(nonPublic: true);

            if (setter is null)
            {
                continue;
            }

            if (setter.IsInitOnly())
            {
                yield return $"{entityType.Name}.{property.Name} uses 'init'; state must change only through aggregate methods.";
            }
            else if (setter.IsVisibleOutsideTypeHierarchy())
            {
                yield return $"{entityType.Name}.{property.Name} has a setter visible outside the aggregate; use 'private set'.";
            }
        }
    }

    // Internal stays allowed: it is how an aggregate root creates its child entities, and C# offers nothing narrower
    // that the root could still reach. Public (or protected internal) would let any caller skip the factory invariants.
    public static IEnumerable<string> FindConstructorViolations(Type entityType)
    {
        ConstructorInfo[] constructors = entityType.GetConstructors(AllInstanceConstructors);

        ConstructorInfo? parameterless = constructors.SingleOrDefault(constructor => constructor.GetParameters().Length == 0);

        if (parameterless is null)
        {
            yield return $"{entityType.Name} must declare a parameterless constructor for EF Core materialization.";
        }

        if (parameterless is not null && parameterless.IsVisibleOutsideTypeHierarchy())
        {
            yield return $"{entityType.Name}() exists only for EF Core and must be private or protected.";
        }

        foreach (ConstructorInfo constructor in constructors.Where(c => c.IsPublic || c.IsFamilyOrAssembly))
        {
            if (constructor == parameterless)
            {
                continue;
            }

            string parameters = string.Join(", ", constructor.GetParameters().Select(p => p.ParameterType.Name));

            yield return $"{entityType.Name}({parameters}) must not be public; create instances through the factory method.";
        }
    }
}
