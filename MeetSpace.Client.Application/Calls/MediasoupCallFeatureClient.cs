
using MeetSpace.Client.Contracts.Calls;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Realtime.Rpc;
using MeetSpace.Client.Shared.Json;
using MeetSpace.Client.Shared.Results;
using MeetSpace.Client.Shared.Utilities;

namespace MeetSpace.Client.App.Calls;

public sealed class MediasoupCallFeatureClient : IMediasoupFeatureClient
{
    private readonly IRealtimeRpcClient _rpcClient;
    private readonly SemaphoreSlim _requestLock = new(1, 1);

    public MediasoupCallFeatureClient(IRealtimeRpcClient rpcClient)
    {
        _rpcClient = rpcClient;
    }

    public async Task<Result> CreateRoomIfMissingAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var result = await DispatchOptionalAsync(
            MediasoupProtocol.CreateRoomActions,
            new Dictionary<string, object?>
            {
                ["roomId"] = Guard.NotNullOrWhiteSpace(roomId, nameof(roomId))
            },
            cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
            return Result.Success();

        return IsAlreadyExists(result.Error)
            ? Result.Success()
            : Result.Failure(result.Error!);
    }

    public async Task<Result> JoinRoomAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var result = await DispatchOptionalAsync(
            MediasoupProtocol.JoinRoomActions,
            new Dictionary<string, object?>
            {
                ["roomId"] = Guard.NotNullOrWhiteSpace(roomId, nameof(roomId))
            },
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Result.Success()
            : Result.Failure(result.Error!);
    }

    public async Task<Result<WebRtcTransportInfo>> OpenTransportAsync(
        string roomId,
        string transportId,
        CancellationToken cancellationToken = default)
    {
        var result = await DispatchRequiredAsync(
            MediasoupProtocol.OpenTransportActions,
            new Dictionary<string, object?>
            {
                ["roomId"] = Guard.NotNullOrWhiteSpace(roomId, nameof(roomId)),
                ["transportId"] = Guard.NotNullOrWhiteSpace(transportId, nameof(transportId))
            },
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
            return Result<WebRtcTransportInfo>.Failure(result.Error!);

        try
        {
            var root = GetPayloadRoot(result.Value!);

            string routerCaps = "{}";
            if (root.TryGetAnyProperty(out var routerCapsElement, "routerRtpCapabilities", "router_rtp_capabilities"))
            {
                routerCaps = routerCapsElement.GetRawText();
            }
            else if (result.Value!.TryGetElement("backend", out var backend) &&
                     backend.ValueKind == JsonValueKind.Object &&
                     backend.TryGetAnyProperty(out var backendCapsElement, "routerRtpCapabilities", "router_rtp_capabilities"))
            {
                routerCaps = backendCapsElement.GetRawText();
            }

            var info = new WebRtcTransportInfo(
                root.GetString("roomId", "room_id") ?? roomId,
                root.GetString("transportId", "transport_id", "id") ?? transportId,
                root.TryGetAnyProperty(out var iceParameters, "iceParameters", "ice_parameters")
                    ? iceParameters.GetRawText()
                    : "{}",
                root.TryGetAnyProperty(out var iceCandidates, "iceCandidates", "ice_candidates")
                    ? iceCandidates.GetRawText()
                    : "[]",
                root.TryGetAnyProperty(out var dtlsParameters, "dtlsParameters", "dtls_parameters")
                    ? dtlsParameters.GetRawText()
                    : "{}",
                routerCaps);

            return Result<WebRtcTransportInfo>.Success(info);
        }
        catch (Exception ex)
        {
            return Result<WebRtcTransportInfo>.Failure(
                new Error("mediasoup.open_transport.parse_failed", ex.Message));
        }
    }

    public async Task<Result> ConnectTransportAsync(
        string roomId,
        string transportId,
        string dtlsParametersJson,
        CancellationToken cancellationToken = default)
    {
        var result = await DispatchRequiredAsync(
            MediasoupProtocol.ConnectTransportActions,
            new Dictionary<string, object?>
            {
                ["roomId"] = Guard.NotNullOrWhiteSpace(roomId, nameof(roomId)),
                ["transportId"] = Guard.NotNullOrWhiteSpace(transportId, nameof(transportId)),
                ["dtlsParameters"] = JsonSerializer.Deserialize<JsonElement>(dtlsParametersJson)
            },
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Result.Success()
            : Result.Failure(result.Error!);
    }

    public async Task<Result> ProduceAsync(
        string roomId,
        string transportId,
        string producerId,
        string kind,
        string rtpParametersJson,
        CancellationToken cancellationToken = default)
    {
        var result = await DispatchRequiredAsync(
            MediasoupProtocol.ProduceActions,
            new Dictionary<string, object?>
            {
                ["roomId"] = Guard.NotNullOrWhiteSpace(roomId, nameof(roomId)),
                ["transportId"] = Guard.NotNullOrWhiteSpace(transportId, nameof(transportId)),
                ["producerId"] = Guard.NotNullOrWhiteSpace(producerId, nameof(producerId)),
                ["kind"] = Guard.NotNullOrWhiteSpace(kind, nameof(kind)),
                ["rtpParameters"] = JsonSerializer.Deserialize<JsonElement>(rtpParametersJson)
            },
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Result.Success()
            : Result.Failure(result.Error!);
    }

    public async Task<Result<ConsumerInfo>> ConsumeAsync(
        string roomId,
        string transportId,
        string producerId,
        string recvRtpCapabilitiesJson,
        CancellationToken cancellationToken = default)
    {
        var result = await DispatchRequiredAsync(
            MediasoupProtocol.ConsumeActions,
            new Dictionary<string, object?>
            {
                ["roomId"] = Guard.NotNullOrWhiteSpace(roomId, nameof(roomId)),
                ["transportId"] = Guard.NotNullOrWhiteSpace(transportId, nameof(transportId)),
                ["producerId"] = Guard.NotNullOrWhiteSpace(producerId, nameof(producerId)),
                ["rtpCapabilities"] = JsonSerializer.Deserialize<JsonElement>(recvRtpCapabilitiesJson)
            },
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
            return Result<ConsumerInfo>.Failure(result.Error!);

        try
        {
            var root = GetPayloadRoot(result.Value!);

            var info = new ConsumerInfo(
                root.GetString("consumerId", "consumer_id", "id") ?? string.Empty,
                root.GetString("producerId", "producer_id") ?? producerId,
                root.GetString("kind") ?? "audio",
                root.TryGetAnyProperty(out var rtpParameters, "rtpParameters", "rtp_parameters")
                    ? rtpParameters.GetRawText()
                    : "{}");

            return Result<ConsumerInfo>.Success(info);
        }
        catch (Exception ex)
        {
            return Result<ConsumerInfo>.Failure(
                new Error("mediasoup.consume.parse_failed", ex.Message));
        }
    }

    public async Task<Result<IReadOnlyList<RemoteProducerDescriptor>>> ListProducersAsync(
        string roomId,
        CancellationToken cancellationToken = default)
    {
        var result = await DispatchOptionalAsync(
            MediasoupProtocol.ListProducersActions,
            new Dictionary<string, object?>
            {
                ["roomId"] = Guard.NotNullOrWhiteSpace(roomId, nameof(roomId))
            },
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
            return Result<IReadOnlyList<RemoteProducerDescriptor>>.Failure(result.Error!);

        if (result.Value == null)
            return Result<IReadOnlyList<RemoteProducerDescriptor>>.Success(Array.Empty<RemoteProducerDescriptor>());

        try
        {
            var root = GetPayloadRoot(result.Value);
            var list = new List<RemoteProducerDescriptor>();

            if (root.TryGetAnyProperty(out var producersArray, "producers", "items") &&
                producersArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in producersArray.EnumerateArray())
                {
                    var producerId = item.GetString("producerId", "producer_id", "id");
                    if (string.IsNullOrWhiteSpace(producerId))
                        continue;

                    list.Add(new RemoteProducerDescriptor(
                        item.GetString("peerId", "peer_id", "peer") ?? string.Empty,
                        producerId,
                        item.GetString("kind") ?? "audio"));
                }
            }

            return Result<IReadOnlyList<RemoteProducerDescriptor>>.Success(list);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<RemoteProducerDescriptor>>.Failure(
                new Error("mediasoup.list_producers.parse_failed", ex.Message));
        }
    }

    public async Task<Result> CloseAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var result = await DispatchOptionalAsync(
            MediasoupProtocol.CloseActions,
            new Dictionary<string, object?>
            {
                ["roomId"] = Guard.NotNullOrWhiteSpace(roomId, nameof(roomId))
            },
            cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Result.Success()
            : Result.Failure(result.Error!);
    }

    private async Task<Result<FeatureResponseEnvelope>> DispatchRequiredAsync(
        IReadOnlyList<string> actions,
        Dictionary<string, object?> ctx,
        CancellationToken cancellationToken)
    {
        return await DispatchCoreAsync(actions, ctx, false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<FeatureResponseEnvelope>> DispatchOptionalAsync(
        IReadOnlyList<string> actions,
        Dictionary<string, object?> ctx,
        CancellationToken cancellationToken)
    {
        return await DispatchCoreAsync(actions, ctx, true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<FeatureResponseEnvelope>> DispatchCoreAsync(
        IReadOnlyList<string> actions,
        Dictionary<string, object?> ctx,
        bool allowUnsupportedAsSuccess,
        CancellationToken cancellationToken)
    {
        if (actions == null || actions.Count == 0)
        {
            return Result<FeatureResponseEnvelope>.Failure(
                new Error("mediasoup.actions.empty", "No mediasoup actions configured."));
        }

        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Result<FeatureResponseEnvelope>? lastFailure = null;

            foreach (var action in actions.Where(static x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal))
            {
                var result = await _rpcClient.DispatchAsync(
                    MediasoupProtocol.Object,
                    MediasoupProtocol.DefaultAgent,
                    action,
                    new Dictionary<string, object?>(ctx),
                    TimeSpan.FromSeconds(15),
                    cancellationToken).ConfigureAwait(false);

                if (result.IsSuccess)
                    return result;

                lastFailure = result;

                if (IsUnsupported(result.Error))
                    continue;

                return result;
            }

            if (allowUnsupportedAsSuccess && lastFailure != null && IsUnsupported(lastFailure.Error))
            {
                return Result<FeatureResponseEnvelope>.Success(new FeatureResponseEnvelope
                {
                    Type = ProtocolMessageTypes.DispatchResult,
                    Object = MediasoupProtocol.Object,
                    Agent = MediasoupProtocol.DefaultAgent,
                    Action = actions[0],
                    Ok = true,
                    Message = string.Empty
                });
            }

            return lastFailure ??
                   Result<FeatureResponseEnvelope>.Failure(
                       new Error("mediasoup.dispatch_failed", "No mediasoup action succeeded."));
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private static bool IsUnsupported(Error? error)
    {
        var text = error?.Message ?? string.Empty;

        return text.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("no handler", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsAlreadyExists(Error? error)
    {
        var text = error?.Message ?? string.Empty;

        return text.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("already joined", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static JsonElement GetPayloadRoot(FeatureResponseEnvelope envelope)
    {
        if (envelope.TryGetPayload(out var payload) && payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetAnyProperty(out var nestedData, "data") &&
                nestedData.ValueKind == JsonValueKind.Object)
            {
                return nestedData;
            }

            return payload;
        }

        if (!string.IsNullOrWhiteSpace(envelope.Message))
        {
            using var doc = JsonDocument.Parse(envelope.Message);
            var root = doc.RootElement.Clone();

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetAnyProperty(out var nestedData, "data") &&
                nestedData.ValueKind == JsonValueKind.Object)
            {
                return nestedData.Clone();
            }

            return root;
        }

        throw new InvalidOperationException("Mediasoup payload is missing.");
    }
}