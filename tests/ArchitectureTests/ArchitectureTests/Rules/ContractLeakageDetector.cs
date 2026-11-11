using System.Reflection;

using EmberDelivery.BuildingBlocks.Application.Messaging;

namespace EmberDelivery.ArchitectureTests.Rules;

// Walks the public data shape of a contract (properties, fields, generic arguments, nested DTOs) because an entity
// hidden inside IReadOnlyList<SomeDto> leaks just as badly as one exposed directly.
internal static class ContractLeakageDetector
{
    private const string OwnNamespacePrefix = "EmberDelivery.";

    private static readonly Type[] ResponseCarryingContracts = [typeof(ICommand<>), typeof(IQuery<>)];

    public static bool IsCommandOrQuery(Type type) =>
        type is { IsClass: true, IsAbstract: false } &&
        (type.IsAssignableTo(typeof(ICommand)) || GetResponseTypes(type).Any());

    public static IReadOnlyList<string> FindLeaks(IEnumerable<Type> contracts)
    {
        List<string> leaks = [];

        foreach (Type contract in contracts)
        {
            HashSet<Type> visited = [];

            Inspect(contract.Name, contract, visited, leaks);

            foreach (Type responseType in GetResponseTypes(contract))
            {
                Inspect($"{contract.Name}<response>", responseType, visited, leaks);
            }
        }

        return leaks;
    }

    private static IEnumerable<Type> GetResponseTypes(Type contract) =>
        contract.GetInterfaces()
            .Where(i => i.IsGenericType && ResponseCarryingContracts.Contains(i.GetGenericTypeDefinition()))
            .Select(i => i.GetGenericArguments()[0]);

    private static void Inspect(string path, Type type, HashSet<Type> visited, List<string> leaks)
    {
        if (type.IsArray)
        {
            Inspect(path, type.GetElementType()!, visited, leaks);
            return;
        }

        if (type.IsGenericParameter)
        {
            return;
        }

        if (type.IsGenericType)
        {
            foreach (Type argument in type.GetGenericArguments())
            {
                Inspect(path, argument, visited, leaks);
            }
        }

        if (DomainEntityConventions.IsDomainEntity(type))
        {
            leaks.Add($"{path} exposes domain entity {type.Name}.");
            return;
        }

        bool isOwnType = type.Namespace?.StartsWith(OwnNamespacePrefix, StringComparison.Ordinal) ?? false;

        if (!isOwnType || !visited.Add(type))
        {
            return;
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            Inspect($"{path}.{property.Name}", property.PropertyType, visited, leaks);
        }

        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            Inspect($"{path}.{field.Name}", field.FieldType, visited, leaks);
        }
    }
}
