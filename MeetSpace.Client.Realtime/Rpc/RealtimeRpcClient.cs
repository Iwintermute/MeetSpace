using System.Collections.Concurrent;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Realtime.Abstractions;
using MeetSpace.Client.Shared.Results;

namespace MeetSpace.Client.Realtime.Rpc;

public sealed class RealtimeRpcClient : IRealtimeRpcClient, IDisposable
{
    private readonly IRealtimeGateway _gateway;
    private readonly ConcurrentDictionary<string, PendingRequest> _pending = new();

    public RealtimeRpcClient(IRealtimeGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _gateway.EnvelopeReceived += OnEnvelopeReceived;
        _gateway.Disconnected += OnGatewayDisconnected;
    }

    public async Task<Result<FeatureResponseEnvelope>> DispatchAsync(
        string @object,
        string agent,
        string action,
        Dictionary<string, object?> ctx,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!_gateway.IsConnected)
        {
            return Result<FeatureResponseEnvelope>.Failure(
                new Error("rpc.not_connected", "Realtime connection is not established."));
        }

        var requestId = Guid.NewGuid().ToString("N");
        ctx ??= new Dictionary<string, object?>();

        ctx["requestId"] = requestId;
        ctx["clientRequestId"] = requestId;
        ctx["correlationId"] = requestId;

        var tcs = new TaskCompletionSource<FeatureResponseEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var pending = new PendingRequest(@object, agent, action, tcs);

        if (!_pending.TryAdd(requestId, pending))
        {
            return Result<FeatureResponseEnvelope>.Failure(
                new Error("rpc.pending_duplicate", "Duplicate pending request id."));
        }

        try
        {
            await _gateway.SendAsync(new FeatureRequestEnvelope
            {
                Object = @object,
                Agent = agent,
                Action = action,
                Ctx = ctx
            }, cancellationToken).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            using var registration = timeoutCts.Token.Register(() =>
            {
                if (_pending.TryRemove(requestId, out var removed))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        removed.Completion.TrySetCanceled(cancellationToken);
                    }
                    else
                    {
                        removed.Completion.TrySetException(
                            new TimeoutException($"Timed out waiting for '{@object}/{action}'."));
                    }
                }
            });

            var envelope = await tcs.Task.ConfigureAwait(false);

            if (envelope.Ok == false)
            {
                return Result<FeatureResponseEnvelope>.Failure(
                    new Error("rpc.rejected", envelope.Message ?? $"{@object}/{action} rejected."));
            }

            return Result<FeatureResponseEnvelope>.Success(envelope);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<FeatureResponseEnvelope>.Failure(
                new Error("rpc.cancelled", $"Request '{@object}/{action}' was cancelled."));
        }
        catch (TimeoutException ex)
        {
            return Result<FeatureResponseEnvelope>.Failure(
                new Error("rpc.timeout", ex.Message));
        }
        catch (OperationCanceledException)
        {
            return Result<FeatureResponseEnvelope>.Failure(
                new Error("rpc.timeout", $"Timed out waiting for '{@object}/{action}'."));
        }
        catch (Exception ex)
        {
            return Result<FeatureResponseEnvelope>.Failure(
                new Error("rpc.failed", ex.Message));
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private void OnEnvelopeReceived(object? sender, FeatureResponseEnvelope envelope)
    {
        if (!string.Equals(envelope.Type, ProtocolMessageTypes.DispatchResult, StringComparison.Ordinal))
            return;

        var requestId = envelope.GetRequestId();
        if (string.IsNullOrWhiteSpace(requestId))
            return;

        if (_pending.TryRemove(requestId, out var pending))
            pending.Completion.TrySetResult(envelope);
    }

    private void OnGatewayDisconnected(object? sender, EventArgs e)
    {
        foreach (var pair in _pending)
        {
            if (_pending.TryRemove(pair.Key, out var pending))
            {
                pending.Completion.TrySetException(
                    new InvalidOperationException(
                        $"Realtime disconnected while waiting for '{pending.Object}/{pending.Action}'."));
            }
        }
    }

    public void Dispose()
    {
        _gateway.EnvelopeReceived -= OnEnvelopeReceived;
        _gateway.Disconnected -= OnGatewayDisconnected;

        foreach (var pair in _pending)
        {
            if (_pending.TryRemove(pair.Key, out var pending))
                pending.Completion.TrySetCanceled();
        }
    }

    private sealed class PendingRequest
    {
        public PendingRequest(
            string objectName,
            string agent,
            string action,
            TaskCompletionSource<FeatureResponseEnvelope> completion)
        {
            Object = objectName;
            Agent = agent;
            Action = action;
            Completion = completion;
        }

        public string Object { get; }
        public string Agent { get; }
        public string Action { get; }
        public TaskCompletionSource<FeatureResponseEnvelope> Completion { get; }
    }
}