using System.Collections.Concurrent;
using System.Reflection;

using Microsoft.AspNetCore.Routing;

namespace EmberDelivery.ArchitectureTests.Infrastructure;

internal static class ApiEndpointCatalog
{
    // Booting a host is the expensive part, so each API is started once and its endpoint snapshot is shared by all rules.
    private static readonly ConcurrentDictionary<Assembly, Lazy<IReadOnlyList<RouteEndpoint>>> Snapshots = new();

    public static IReadOnlyList<RouteEndpoint> GetRouteEndpoints(Assembly apiAssembly) =>
        Snapshots.GetOrAdd(
                apiAssembly,
                static assembly => new Lazy<IReadOnlyList<RouteEndpoint>>(() => Capture(assembly)))
            .Value;

    private static IReadOnlyList<RouteEndpoint> Capture(Assembly apiAssembly)
    {
        Type entryPoint = apiAssembly.EntryPoint?.DeclaringType
            ?? throw new InvalidOperationException($"{apiAssembly.GetName().Name} has no entry point.");

        Type factoryType = typeof(EndpointMetadataFactory<>).MakeGenericType(entryPoint);

        using var factory = (IRouteEndpointSource)(Activator.CreateInstance(factoryType)
            ?? throw new InvalidOperationException($"Could not create {factoryType.Name}."));

        return factory.CaptureRouteEndpoints();
    }
}
