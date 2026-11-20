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
    // Resolved once per closed generic type, so a failed validation costs a delegate call instead of a reflection lookup.
    private static readonly Func<ValidationError, TResponse>? FailureFactory = CreateFailureFactory();

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        List<Error>? validationErrors = null;

        // Sequential with a context per validator: FluentValidation results alias their context's failure list, so
        // sharing one context across concurrent validators would duplicate errors and mutate a List from several threads.
        foreach (IValidator<TRequest> validator in validators)
        {
            ValidationResult validationResult = await validator.ValidateAsync(
                new ValidationContext<TRequest>(request),
                cancellationToken);

            foreach (ValidationFailure failure in validationResult.Errors)
            {
                validationErrors ??= [];
                validationErrors.Add(Error.Validation(failure.PropertyName, failure.ErrorMessage));
            }
        }

        if (validationErrors is null)
        {
            return await next(cancellationToken);
        }

        if (FailureFactory is null)
        {
            throw new InvalidOperationException(
                $"ValidationFailure method was not found on response type {typeof(TResponse).Name}.");
        }

        return FailureFactory(new ValidationError([.. validationErrors]));
    }

    private static Func<ValidationError, TResponse>? CreateFailureFactory()
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return error => (TResponse)Result.Failure(error);
        }

        MethodInfo? validationFailureMethod = typeof(TResponse).GetMethod(
            nameof(Result<object>.ValidationFailure),
            BindingFlags.Public | BindingFlags.Static,
            [typeof(Error)]);

        if (validationFailureMethod is null || validationFailureMethod.ReturnType != typeof(TResponse))
        {
            return null;
        }

        Func<Error, TResponse> factory = validationFailureMethod.CreateDelegate<Func<Error, TResponse>>();
        return error => factory(error);
    }
}
