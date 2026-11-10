using System.Reflection;

using EmberDelivery.BuildingBlocks.Domain.Abstractions;
using EmberDelivery.BuildingBlocks.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

namespace EmberDelivery.ArchitectureTests.Rules;

internal static class EfConfigurationConventions
{
    // Maps each configured CLR type to the configuration class that targets it.
    public static IReadOnlyDictionary<Type, Type> FindConfigurations(Assembly assembly)
    {
        Type openConfigInterface = typeof(IEntityTypeConfiguration<>);
        Dictionary<Type, Type> configurations = [];

        foreach (Type configType in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            Type? implementedConfigInterface = configType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == openConfigInterface);

            if (implementedConfigInterface is not null)
            {
                configurations[implementedConfigInterface.GetGenericArguments()[0]] = configType;
            }
        }

        return configurations;
    }

    public static IReadOnlyList<string> FindViolations(Assembly assembly)
    {
        List<string> violations = [];

        foreach ((Type targetModelType, Type configType) in FindConfigurations(assembly))
        {
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

        return violations;
    }
}
