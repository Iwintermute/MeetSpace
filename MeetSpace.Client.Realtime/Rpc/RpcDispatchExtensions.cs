using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Shared.Results;
using System.Linq;

namespace MeetSpace.Client.Realtime.Rpc;

public static class RpcDispatchExtensions
{
    public static async Task<Result<FeatureResponseEnvelope>> DispatchFirstAsync(
        this IRealtimeRpcClient rpcClient,
        string objectName,
        string agent,
        IReadOnlyList<string> actions,
        Dictionary<string, object?> ctx,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (rpcClient == null)
            throw new ArgumentNullException(nameof(rpcClient));

        if (actions == null || actions.Count == 0)
        {
            return Result<FeatureResponseEnvelope>.Failure(
                new Error("rpc.actions.empty", "Action candidate list is empty."));
        }

        Result<FeatureResponseEnvelope>? lastFailure = null;

        foreach (var action in actions.Where(static x => !string.IsNullOrWhiteSpace(x))
                                      .Distinct(StringComparer.Ordinal))
        {
            var result = await rpcClient.DispatchAsync(
                objectName,
                agent,
                action,
                new Dictionary<string, object?>(ctx),
                timeout,
                cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
                return result;

            lastFailure = result;

            if (!LooksLikeUnsupported(result.Error))
                return result;
        }

        return lastFailure ??
               Result<FeatureResponseEnvelope>.Failure(
                   new Error("rpc.dispatch_failed", "No action candidate succeeded."));
    }

    private static bool LooksLikeUnsupported(Error? error)
    {
        var text = error?.Message ?? string.Empty;

        return text.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("no handler", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}