namespace EmberDelivery.Billing.Api.Infrastructure.PaymentGateway;

public sealed class PaymentGatewayUnavailableException : Exception
{
    public PaymentGatewayUnavailableException()
    {
    }

    public PaymentGatewayUnavailableException(string message)
        : base(message)
    {
    }

    public PaymentGatewayUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
