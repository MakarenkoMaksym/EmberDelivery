using System.Reflection;

using EmberDelivery.BuildingBlocks.Domain.Results;

using FluentValidation;
using FluentValidation.Results;

using MediatR;

namespace EmberDelivery.BuildingBlocks.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next(cancellationToken);
        }

        ValidationContext<TRequest> context = new(request);

        ValidationResult[] validationResults = await Task.WhenAll(
            validators.Select(validator => validator.ValidateAsync(context, cancellationToken)));

        Error[] validationErrors = validationResults
            .Where(validationResult => validationResult.Errors.Count != 0)
            .SelectMany(validationResult => validationResult.Errors)
            .Select(validationFailure => Error.Validation(
                validationFailure.PropertyName,
                validationFailure.ErrorMessage))
            .ToArray();

        if (validationErrors.Length == 0)
        {
            return await next(cancellationToken);
        }

        return CreateValidationResult(validationErrors);
    }

    private static TResponse CreateValidationResult(Error[] errors)
    {
        ValidationError validationError = new(errors);

        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)(object)Result.Failure(validationError);
        }

        MethodInfo? validationFailureMethod = typeof(TResponse)
            .GetMethod(nameof(Result<object>.ValidationFailure), BindingFlags.Public | BindingFlags.Static);

        if (validationFailureMethod is not null)
        {
            return (TResponse)validationFailureMethod.Invoke(null, [validationError])!;
        }

        throw new InvalidOperationException($"ValidationFailure method was not found on response type {typeof(TResponse).Name}.");
    }
}
