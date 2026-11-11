using System.Reflection;
using System.Runtime.CompilerServices;

namespace EmberDelivery.ArchitectureTests.Rules;

internal static class PropertyAccessors
{
    // Reflection exposes "init" as an ordinary setter; the only marker is the IsExternalInit modreq on its return value.
    public static bool IsInitOnly(this MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));

    // Private and protected (including private protected) stay inside the class hierarchy; anything wider does not.
    public static bool IsVisibleOutsideTypeHierarchy(this MethodBase method) =>
        method.IsPublic || method.IsAssembly || method.IsFamilyOrAssembly;

    public static bool IsCompilerGenerated(this Type type) =>
        type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);
}
