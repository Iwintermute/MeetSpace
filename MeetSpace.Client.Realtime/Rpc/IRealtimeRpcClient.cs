using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Shared.Results;

namespace MeetSpace.Client.Realtime.Rpc;
/// <summary>
/// Defines request/response RPC dispatch over realtime gateway.
/// </summary>

public interface IRealtimeRpcClient
{
    /// <summary>
    /// Sends a dispatch request and awaits a correlated dispatch_result envelope.
    /// </summary>
    /// <param name="object">Feature object namespace.</param>
    /// <param name="agent">Feature agent namespace.</param>
    /// <param name="action">Action name inside the feature namespace.</param>
    /// <param name="ctx">Context payload sent with request.</param>
    /// <param name="timeout">Maximum wait duration for correlated response.</param>
    /// <param name="cancellationToken">Cancellation token for send/wait operation.</param>
    /// <returns>Success with response envelope or failure with transport/protocol error.</returns>
    Task<Result<FeatureResponseEnvelope>> DispatchAsync(
        string @object,
        string agent,
        string action,
        Dictionary<string, object?> ctx,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}