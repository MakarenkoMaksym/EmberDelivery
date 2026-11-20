using MassTransit;

using Microsoft.Extensions.Logging;

namespace EmberDelivery.Ordering.Infrastructure.Messaging;

// Registered before UseEntityFrameworkOutbox, so it wraps the outbox filter: when next.Send returns, the consumer's
// transaction has already committed and readers can only observe the new state.
public sealed class PostCommitActionsFilter<TMessage>(
    PostCommitActions postCommitActions,
    ILogger<PostCommitActionsFilter<TMessage>> logger) : IFilter<ConsumeContext<TMessage>>
    where TMessage : class
{
    public async Task Send(ConsumeContext<TMessage> context, IPipe<ConsumeContext<TMessage>> next)
    {
        await next.Send(context);

        foreach (Func<CancellationToken, Task> action in postCommitActions.Drain())
        {
            // The business change is durable at this point; failing the message would only replay a committed consume.
            try
            {
                await action(context.CancellationToken);
            }
            catch (Exception ex) when (!context.CancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    ex,
                    "Post-commit action failed for message {MessageType} {MessageId}",
                    typeof(TMessage).Name,
                    context.MessageId);
            }
        }
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("postCommitActions");
}
