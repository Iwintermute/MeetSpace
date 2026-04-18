using System.Text.Json;
using MeetSpace.Client.Contracts.Conference;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Realtime.Rpc;
using MeetSpace.Client.Shared.Json;
using MeetSpace.Client.Shared.Results;
using MeetSpace.Client.Shared.Utilities;

namespace MeetSpace.Client.App.Calls;

public sealed class ConferenceMediaFeatureClient : IConferenceMediaFeatureClient
{
    private readonly IRealtimeRpcClient _rpcClient;
    private readonly SemaphoreSlim _requestLock = new(1, 1);

    public ConferenceMediaFeatureClient(IRealtimeRpcClient rpcClient)
    {
        _rpcClient = rpcClient;
    }

    public async Task<Result<WebRtcTransportInfo>> OpenTransportAsync(
        string sessionId,
        string transportId,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            ConferenceProtocol.OpenTransportActions,
            CreateContext(sessionId, new Dictionary<string, object?>
            {
                ["transportId"] = Guard.NotNullOrWhiteSpace(transportId, nameof(transportId))
            }),
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<WebRtcTransportInfo>.Failure(response.Error!);

        try
        {
            var root = GetPayloadRoot(response.Value!);

            var info = new WebRtcTransportInfo(
                root.GetString("roomId", "room_id") ?? sessionId,
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
                root.TryGetAnyProperty(out var routerCaps, "routerRtpCapabilities", "router_rtp_capabilities")
                    ? routerCaps.GetRawText()
                    : "{}");

            return Result<WebRtcTransportInfo>.Success(info);
        }
        catch (Exception ex)
        {
            return Result<WebRtcTransportInfo>.Failure(
                new Error("conference.media.open_transport.parse_failed", ex.Message));
        }
    }

    public async Task<Result> ConnectTransportAsync(
        string sessionId,
        string transportId,
        string dtlsParametersJson,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            ConferenceProtocol.WebRtcOfferActions,
            CreateContext(sessionId, new Dictionary<string, object?>
            {
                ["transportId"] = Guard.NotNullOrWhiteSpace(transportId, nameof(transportId)),
                ["dtlsParameters"] = JsonSerializer.Deserialize<JsonElement>(dtlsParametersJson)
            }),
            cancellationToken).ConfigureAwait(false);

        return response.IsSuccess
            ? Result.Success()
            : Result.Failure(response.Error!);
    }

    public async Task<Result> PublishTrackAsync(
        string sessionId,
        string transportId,
        string producerId,
        string kind,
        string trackType,
        string rtpParametersJson,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            ConferenceProtocol.PublishTrackActions,
            CreateContext(sessionId, new Dictionary<string, object?>
            {
                ["transportId"] = Guard.NotNullOrWhiteSpace(transportId, nameof(transportId)),
                ["producerId"] = Guard.NotNullOrWhiteSpace(producerId, nameof(producerId)),
                ["kind"] = Guard.NotNullOrWhiteSpace(kind, nameof(kind)),
                ["trackType"] = string.IsNullOrWhiteSpace(trackType) ? kind : trackType,
                ["rtpParameters"] = JsonSerializer.Deserialize<JsonElement>(rtpParametersJson)
            }),
            cancellationToken).ConfigureAwait(false);

        return response.IsSuccess
            ? Result.Success()
            : Result.Failure(response.Error!);
    }

    public async Task<Result<ConsumerInfo>> ConsumeTrackAsync(
        string sessionId,
        string transportId,
        string producerId,
        string recvRtpCapabilitiesJson,
        string? consumerId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            ConferenceProtocol.ConsumeTrackActions,
            CreateContext(sessionId, new Dictionary<string, object?>
            {
                ["transportId"] = Guard.NotNullOrWhiteSpace(transportId, nameof(transportId)),
                ["producerId"] = Guard.NotNullOrWhiteSpace(producerId, nameof(producerId)),
                ["consumerId"] = string.IsNullOrWhiteSpace(consumerId) ? "consumer-" + Guid.NewGuid().ToString("N") : consumerId,
                ["rtpCapabilities"] = JsonSerializer.Deserialize<JsonElement>(recvRtpCapabilitiesJson)
            }),
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<ConsumerInfo>.Failure(response.Error!);

        try
        {
            var root = GetPayloadRoot(response.Value!);
            var info = new ConsumerInfo(
                root.GetString("consumerId", "consumer_id", "id") ?? (consumerId ?? string.Empty),
                root.GetString("producerId", "producer_id") ?? producerId,
                root.GetString("kind") ?? "audio",
                root.TryGetAnyProperty(out var rtpParameters, "rtpParameters", "rtp_parameters")
                    ? rtpParameters.GetRawText()
                    : "{}",
                root.GetString("trackType", "track_type"),
                root.GetString("producerPeerId", "producer_peer_id"),
                root.GetBoolean("paused") ?? false);

            return Result<ConsumerInfo>.Success(info);
        }
        catch (Exception ex)
        {
            return Result<ConsumerInfo>.Failure(
                new Error("conference.media.consume_track.parse_failed", ex.Message));
        }
    }

    public async Task<Result> ConsumerReadyAsync(
        string sessionId,
        string consumerId,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            ConferenceProtocol.ConsumerReadyActions,
            CreateContext(sessionId, new Dictionary<string, object?>
            {
                ["consumerId"] = Guard.NotNullOrWhiteSpace(consumerId, nameof(consumerId))
            }),
            cancellationToken).ConfigureAwait(false);

        return response.IsSuccess
            ? Result.Success()
            : Result.Failure(response.Error!);
    }

    public async Task<Result<MediaStatsSnapshot>> GetMediaStatsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            ConferenceProtocol.MediaStatsActions,
            CreateContext(sessionId, new Dictionary<string, object?>()),
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<MediaStatsSnapshot>.Failure(response.Error!);

        try
        {
            var root = GetPayloadRoot(response.Value!);
            var producers = ParseProducers(root);
            var memberPeerIds = ParsePeerList(root, "memberPeerIds", "member_peer_ids", "activePeerIds", "active_peer_ids");
            var roomId = root.GetString("roomId", "room_id") ?? sessionId;

            var snapshot = new MediaStatsSnapshot(
                sessionId,
                roomId,
                producers,
                memberPeerIds,
                ConvertToInt(root.GetInt64("transportCount", "transport_count")),
                ConvertToInt(root.GetInt64("producerCount", "producer_count")),
                ConvertToInt(root.GetInt64("consumerCount", "consumer_count")));

            return Result<MediaStatsSnapshot>.Success(snapshot);
        }
        catch (Exception ex)
        {
            return Result<MediaStatsSnapshot>.Failure(
                new Error("conference.media.media_stats.parse_failed", ex.Message));
        }
    }

    public async Task<Result> CloseSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            ConferenceProtocol.WebRtcCloseActions,
            CreateContext(sessionId, new Dictionary<string, object?>()),
            cancellationToken).ConfigureAwait(false);

        return response.IsSuccess
            ? Result.Success()
            : Result.Failure(response.Error!);
    }

    private async Task<Result<FeatureResponseEnvelope>> DispatchAsync(
        IReadOnlyList<string> actions,
        Dictionary<string, object?> context,
        CancellationToken cancellationToken)
    {
        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _rpcClient.DispatchFirstAsync(
                ConferenceProtocol.Object,
                ConferenceProtocol.Agents.Lifecycle,
                actions,
                context,
                TimeSpan.FromSeconds(15),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private static Dictionary<string, object?> CreateContext(
        string conferenceId,
        IDictionary<string, object?>? additions)
    {
        var result = new Dictionary<string, object?>
        {
            ["conferenceId"] = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId))
        };

        if (additions != null)
        {
            foreach (var pair in additions)
                result[pair.Key] = pair.Value;
        }

        return result;
    }

    private static JsonElement GetPayloadRoot(FeatureResponseEnvelope envelope)
    {
        if (envelope.TryGetPayload(out var payload) && payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetAnyProperty(out var nestedData, "data") && nestedData.ValueKind == JsonValueKind.Object)
                return nestedData;

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

        throw new InvalidOperationException("Conference media payload is missing.");
    }

    private static IReadOnlyList<RemoteProducerDescriptor> ParseProducers(JsonElement root)
    {
        if (!root.TryGetAnyProperty(out var producersNode, "producers", "activeProducers", "active_producers") ||
            producersNode.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<RemoteProducerDescriptor>();
        }

        var result = new List<RemoteProducerDescriptor>();
        foreach (var item in producersNode.EnumerateArray())
        {
            var producerId = item.GetString("producerId", "producer_id", "id");
            if (string.IsNullOrWhiteSpace(producerId))
                continue;

            result.Add(new RemoteProducerDescriptor(
                item.GetString("peerId", "peer_id", "producerPeerId", "producer_peer_id") ?? string.Empty,
                producerId,
                item.GetString("kind") ?? "audio",
                item.GetString("trackType", "track_type")));
        }

        return result;
    }

    private static IReadOnlyList<string> ParsePeerList(JsonElement root, params string[] names)
    {
        if (!root.TryGetAnyProperty(out var peersNode, names) || peersNode.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var result = new List<string>();
        foreach (var peer in peersNode.EnumerateArray())
        {
            if (peer.ValueKind != JsonValueKind.String)
                continue;

            var value = peer.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                result.Add(value);
        }

        return result;
    }

    private static int ConvertToInt(long? value)
    {
        if (!value.HasValue)
            return 0;

        if (value.Value < 0)
            return 0;

        if (value.Value > int.MaxValue)
            return int.MaxValue;

        return (int)value.Value;
    }
}
