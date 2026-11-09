namespace EmberDelivery.Ordering.IntegrationTests.Infrastructure;

public sealed record ProblemResponse(
    int Status,
    string Title,
    string? Detail,
    IReadOnlyList<ProblemErrorResponse>? Errors);

public sealed record ProblemErrorResponse(
    string Code,
    string Description);
