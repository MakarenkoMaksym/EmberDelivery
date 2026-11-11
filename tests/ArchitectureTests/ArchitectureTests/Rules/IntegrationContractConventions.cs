using System.Reflection;

namespace EmberDelivery.ArchitectureTests.Rules;

internal static class IntegrationContractConventions
{
    public static IReadOnlyList<Type> FindContractTypes(Assembly assembly) =>
        assembly.GetTypes()
            .Where(type => !type.IsCompilerGenerated())
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

    // "init" is allowed: positional records rely on it, and it cannot change a message once it has been constructed.
    public static IReadOnlyList<string> FindViolations(Assembly assembly)
    {
        List<string> violations = [];

        foreach (Type type in FindContractTypes(assembly))
        {
            // IsVisible requires every enclosing type to be public too; static classes are abstract sealed in IL.
            if (!type.IsVisible || !type.IsSealed)
            {
                violations.Add($"{type.FullName} must be public sealed.");
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                MethodInfo? setter = property.GetSetMethod(nonPublic: false);

                if (setter is not null && !setter.IsInitOnly())
                {
                    violations.Add($"{type.Name}.{property.Name} has a public setter; consumers could mutate a shared message.");
                }
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!field.IsInitOnly)
                {
                    violations.Add($"{type.Name}.{field.Name} is a public mutable field.");
                }
            }
        }

        return violations;
    }
}
