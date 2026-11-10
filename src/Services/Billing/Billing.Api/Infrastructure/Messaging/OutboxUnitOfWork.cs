using EmberDelivery.Billing.Api.Infrastructure.Persistence;

using MassTransit;

namespace EmberDelivery.Billing.Api.Infrastructure.Messaging;

// Inside a consumer's own scope MassTransit resolves IPublishEndpoint to the ConsumeContext (see
// EntityFrameworkScopedBusContextProvider), which bypasses the bus outbox and publishes straight to the broker even if
// the subsequent SaveChanges fails. A dedicated scope has no ConsumeContext, so IPublishEndpoint there is the EF bus
// outbox bound to this unit of work's DbContext, and the business rows and the events commit in one SaveChanges.
public sealed class OutboxUnitOfWork : IAsyncDisposable
{
    private readonly AsyncServiceScope _scope;
    private readonly ConsumeContext _trigger;
    private readonly IPublishEndpoint _outbox;

    internal OutboxUnitOfWork(AsyncServiceScope scope, ConsumeContext trigger)
    {
        _scope = scope;
        _trigger = trigger;
        DbContext = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        _outbox = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
    }

    public BillingDbContext DbContext { get; }

    // The dedicated scope loses the automatic header flow of ConsumeContext.Publish, so the causal chain is restored
    // explicitly; without it the saga's conversation would be split at Billing in traces and the message audit.
    public Task PublishAsync<T>(T message, CancellationToken cancellationToken)
        where T : class =>
        _outbox.Publish(
            message,
            publishContext =>
            {
                publishContext.ConversationId = _trigger.ConversationId;
                publishContext.InitiatorId = _trigger.MessageId;
                publishContext.CorrelationId ??= _trigger.CorrelationId;
            },
            cancellationToken);

    public Task CommitAsync(CancellationToken cancellationToken) => DbContext.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => _scope.DisposeAsync();
}

public sealed class OutboxUnitOfWorkFactory(IServiceScopeFactory scopeFactory)
{
    public OutboxUnitOfWork Begin(ConsumeContext trigger) => new(scopeFactory.CreateAsyncScope(), trigger);
}
