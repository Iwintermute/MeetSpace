using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Shared.Results;

namespace MeetSpace.Client.Realtime.Rpc;

public interface IRealtimeRpcClient
{
    Task<Result<FeatureResponseEnvelope>> DispatchAsync(
        string @object,
        string agent,
        string action,
        Dictionary<string, object?> ctx,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}