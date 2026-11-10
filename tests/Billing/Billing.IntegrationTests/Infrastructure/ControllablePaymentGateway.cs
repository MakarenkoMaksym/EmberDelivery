using EmberDelivery.Billing.Api.Infrastructure.PaymentGateway;

namespace EmberDelivery.Billing.IntegrationTests.Infrastructure;

public enum GatewayOperation
{
    Authorize,
    Capture,
    Release
}

public sealed record GatewayCall(GatewayOperation Operation, string IdempotencyKey, PaymentGatewayResult? Result);

// Wraps the real fake gateway (keeping its provider-side idempotency) and adds the knobs a race test needs:
// recording every call, injecting outages, and parking callers at a rendezvous so interleavings are deterministic.
public sealed class ControllablePaymentGateway(IPaymentGateway inner) : IPaymentGateway
{
    private readonly Lock _gate = new();
    private readonly List<GatewayCall> _calls = [];
    private readonly Dictionary<GatewayOperation, int> _pendingOutages = [];
    private GatewayRendezvous? _rendezvous;

    public IReadOnlyList<GatewayCall> CallsFor(string idempotencyKey)
    {
        lock (_gate)
        {
            return _calls
                .Where(call => string.Equals(call.IdempotencyKey, idempotencyKey, StringComparison.Ordinal))
                .ToList();
        }
    }

    public void FailNext(GatewayOperation operation, int times)
    {
        lock (_gate)
        {
            _pendingOutages[operation] = times;
        }
    }

    public GatewayRendezvous ParkUntilAllArrive(int participants, params GatewayOperation[] operations)
    {
        GatewayRendezvous rendezvous = new(participants, operations);

        lock (_gate)
        {
            _rendezvous = rendezvous;
        }

        return rendezvous;
    }

    public void Reset()
    {
        lock (_gate)
        {
            _calls.Clear();
            _pendingOutages.Clear();
            _rendezvous?.Release();
            _rendezvous = null;
        }
    }

    public Task<PaymentGatewayResult> AuthorizeAsync(AuthorizePaymentRequest request, CancellationToken cancellationToken) =>
        InvokeAsync(
            GatewayOperation.Authorize,
            request.IdempotencyKey,
            () => inner.AuthorizeAsync(request, cancellationToken),
            cancellationToken);

    public Task<PaymentGatewayResult> CaptureAsync(CapturePaymentRequest request, CancellationToken cancellationToken) =>
        InvokeAsync(
            GatewayOperation.Capture,
            request.IdempotencyKey,
            () => inner.CaptureAsync(request, cancellationToken),
            cancellationToken);

    public Task<PaymentGatewayResult> ReleaseAsync(ReleasePaymentRequest request, CancellationToken cancellationToken) =>
        InvokeAsync(
            GatewayOperation.Release,
            request.IdempotencyKey,
            () => inner.ReleaseAsync(request, cancellationToken),
            cancellationToken);

    private async Task<PaymentGatewayResult> InvokeAsync(
        GatewayOperation operation,
        string idempotencyKey,
        Func<Task<PaymentGatewayResult>> call,
        CancellationToken cancellationToken)
    {
        GatewayRendezvous? rendezvous;

        lock (_gate)
        {
            if (_pendingOutages.TryGetValue(operation, out int remaining) && remaining > 0)
            {
                _pendingOutages[operation] = remaining - 1;
                _calls.Add(new GatewayCall(operation, idempotencyKey, null));
                throw new PaymentGatewayUnavailableException($"Simulated {operation} outage.");
            }

            rendezvous = _rendezvous is not null && _rendezvous.Applies(operation) ? _rendezvous : null;
        }

        if (rendezvous is not null)
        {
            await rendezvous.ArriveAndWaitAsync(cancellationToken);
        }

        PaymentGatewayResult result = await call();

        lock (_gate)
        {
            _calls.Add(new GatewayCall(operation, idempotencyKey, result));
        }

        return result;
    }
}

public sealed class GatewayRendezvous(int participants, IReadOnlyCollection<GatewayOperation> operations)
{
    // Bounded so a test whose messages never arrive fails on its assertions instead of hanging the whole run.
    private static readonly TimeSpan MaxParkingTime = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrivals;

    public Task AllArrived => _allArrived.Task;

    public void Release() => _released.TrySetResult();

    internal bool Applies(GatewayOperation operation) => operations.Contains(operation);

    internal async Task ArriveAndWaitAsync(CancellationToken cancellationToken)
    {
        // Retries after the release must pass straight through; only the first wave of callers is parked.
        if (_released.Task.IsCompleted)
        {
            return;
        }

        if (Interlocked.Increment(ref _arrivals) >= participants)
        {
            _allArrived.TrySetResult();
        }

        await Task.WhenAny(_released.Task, Task.Delay(MaxParkingTime, cancellationToken));
    }
}
