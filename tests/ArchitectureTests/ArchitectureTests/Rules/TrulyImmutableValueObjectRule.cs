using Mono.Cecil;

using NetArchTest.Rules;

namespace EmberDelivery.ArchitectureTests.Rules;

// Inspects IL rather than reflection metadata: "{ get; private set; }" and "{ get; init; }" both compile to backing
// fields without the initonly flag, which reflection-based property checks cannot tell apart from "{ get; }".
internal sealed class TrulyImmutableValueObjectRule : ICustomRule
{
    public bool MeetsRule(TypeDefinition type) =>
        type.Fields
            .Where(field => !field.IsStatic)
            .All(field => field.IsInitOnly);
}
