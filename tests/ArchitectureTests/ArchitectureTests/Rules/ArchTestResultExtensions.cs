using ArchTestResult = NetArchTest.Rules.TestResult;

namespace EmberDelivery.ArchitectureTests.Rules;

internal static class ArchTestResultExtensions
{
    public static string DescribeFailingTypes(this ArchTestResult result) =>
        result.FailingTypeNames is null ? string.Empty : string.Join(", ", result.FailingTypeNames);
}
