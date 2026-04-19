using System.Text.Json;
using MeetSpace.Client.Contracts.Calls;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Domain.Calls;
using MeetSpace.Client.Realtime.Rpc;
using MeetSpace.Client.Shared.Json;
using MeetSpace.Client.Shared.Results;
using MeetSpace.Client.Shared.Utilities;

namespace MeetSpace.Client.App.Calls;

public sealed class DirectCallFeatureClient : IDirectCallFeatureClient
{
    private readonly IRealtimeRpcClient _rpcClient;
    private readonly SemaphoreSlim _requestLock = new(1, 1);

    public DirectCallFeatureClient(IRealtimeRpcClient rpcClient)
    {
        _rpcClient = rpcClient;
    }

    public async Task<Result<DirectCallSessionInfo>> CreateCallAsync(
        string targetUserId,
        string? mode = null,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default)
    {
        return await CreateCallCoreAsync(targetUserId, mode, clientRequestId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<DirectCallSessionInfo>> AcceptCallAsync(
        string callId,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default)
    {
        var context = CreateCallContext(callId);
        if (!string.IsNullOrWhiteSpace(clientRequestId))
            context["clientRequestId"] = clientRequestId;

        var response = await DispatchAsync(
            DirectCallProtocol.AcceptCallActions,
            context,
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<DirectCallSessionInfo>.Failure(response.Error!);

        return ParseSessionInfo(response.Value!, DirectCallStatus.Accepted, callId);
    }

    public async Task<Result<DirectCallSessionInfo>> DeclineCallAsync(
        string callId,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default)
    {
        var context = CreateCallContext(callId);
        if (!string.IsNullOrWhiteSpace(clientRequestId))
            context["clientRequestId"] = clientRequestId;

        var response = await DispatchAsync(
            DirectCallProtocol.DeclineCallActions,
            context,
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<DirectCallSessionInfo>.Failure(response.Error!);

        return ParseSessionInfo(response.Value!, DirectCallStatus.Declined, callId);
    }

    public async Task<Result<DirectCallSessionInfo>> HangupCallAsync(
        string callId,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default)
    {
        var context = CreateCallContext(callId);
        if (!string.IsNullOrWhiteSpace(clientRequestId))
            context["clientRequestId"] = clientRequestId;

        var response = await DispatchAsync(
            DirectCallProtocol.HangupCallActions,
            context,
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<DirectCallSessionInfo>.Failure(response.Error!);

        return ParseSessionInfo(response.Value!, DirectCallStatus.Ended, callId);
    }

    public async Task<Result<IReadOnlyList<DirectCallSessionInfo>>> ListActiveCallsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            DirectCallProtocol.ListActiveCallsActions,
            CreateContext(new Dictionary<string, object?>
            {
                ["limit"] = limit <= 0 ? 100 : limit
            }),
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<IReadOnlyList<DirectCallSessionInfo>>.Failure(response.Error!);

        return ParseActiveCalls(response.Value!);
    }

    public async Task<Result<WebRtcTransportInfo>> OpenTransportAsync(
        string callId,
        string transportId,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            DirectCallProtocol.OpenTransportActions,
            CreateCallContext(callId, new Dictionary<string, object?>
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
                root.GetString("roomId", "room_id", "sessionId", "session_id") ?? callId,
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
                new Error("direct_call.open_transport.parse_failed", ex.Message));
        }
    }

    public async Task<Result> ConnectTransportAsync(
        string callId,
        string transportId,
        string dtlsParametersJson,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            DirectCallProtocol.WebRtcOfferActions,
            CreateCallContext(callId, new Dictionary<string, object?>
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
        string callId,
        string transportId,
        string producerId,
        string kind,
        string trackType,
        string rtpParametersJson,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            DirectCallProtocol.PublishTrackActions,
            CreateCallContext(callId, new Dictionary<string, object?>
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

    public async Task<Result> PauseTrackAsync(
        string callId,
        string producerId,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            DirectCallProtocol.PauseTrackActions,
            CreateCallContext(callId, new Dictionary<string, object?>
            {
                ["producerId"] = Guard.NotNullOrWhiteSpace(producerId, nameof(producerId))
            }),
            cancellationToken).ConfigureAwait(false);

        return response.IsSuccess
            ? Result.Success()
            : Result.Failure(response.Error!);
    }

    public async Task<Result> ResumeTrackAsync(
        string callId,
        string producerId,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            DirectCallProtocol.ResumeTrackActions,
            CreateCallContext(callId, new Dictionary<string, object?>
            {
                ["producerId"] = Guard.NotNullOrWhiteSpace(producerId, nameof(producerId))
            }),
            cancellationToken).ConfigureAwait(false);

        return response.IsSuccess
            ? Result.Success()
            : Result.Failure(response.Error!);
    }

    public async Task<Result> CloseTrackAsync(
        string callId,
        string producerId,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            DirectCallProtocol.CloseTrackActions,
            CreateCallContext(callId, new Dictionary<string, object?>
            {
                ["producerId"] = Guard.NotNullOrWhiteSpace(producerId, nameof(producerId))
            }),
            cancellationToken).ConfigureAwait(false);

        return response.IsSuccess
            ? Result.Success()
            : Result.Failure(response.Error!);
    }

    public async Task<Result<ConsumerInfo>> ConsumeTrackAsync(
        string callId,
        string transportId,
        string producerId,
        string recvRtpCapabilitiesJson,
        string? consumerId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            DirectCallProtocol.ConsumeTrackActions,
            CreateCallContext(callId, new Dictionary<string, object?>
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
                new Error("direct_call.consume_track.parse_failed", ex.Message));
        }
    }

    public async Task<Result> ConsumerReadyAsync(
        string callId,
        string consumerId,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            DirectCallProtocol.ConsumerReadyActions,
            CreateCallContext(callId, new Dictionary<string, object?>
            {
                ["consumerId"] = Guard.NotNullOrWhiteSpace(consumerId, nameof(consumerId))
            }),
            cancellationToken).ConfigureAwait(false);

        return response.IsSuccess
            ? Result.Success()
            : Result.Failure(response.Error!);
    }

    public async Task<Result<MediaStatsSnapshot>> GetMediaStatsAsync(
        string callId,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            DirectCallProtocol.MediaStatsActions,
            CreateCallContext(callId),
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<MediaStatsSnapshot>.Failure(response.Error!);

        try
        {
            var root = GetPayloadRoot(response.Value!);
            var producers = ParseProducers(root);
            var peers = ParsePeerList(root, "memberPeerIds", "member_peer_ids", "activePeerIds", "active_peer_ids");
            var roomId = root.GetString("roomId", "room_id", "sessionId", "session_id") ?? callId;

            var snapshot = new MediaStatsSnapshot(
                callId,
                roomId,
                producers,
                peers,
                ConvertToInt(root.GetInt64("transportCount", "transport_count")),
                ConvertToInt(root.GetInt64("producerCount", "producer_count")),
                ConvertToInt(root.GetInt64("consumerCount", "consumer_count")));

            return Result<MediaStatsSnapshot>.Success(snapshot);
        }
        catch (Exception ex)
        {
            return Result<MediaStatsSnapshot>.Failure(
                new Error("direct_call.media_stats.parse_failed", ex.Message));
        }
    }

    public async Task<Result> CloseSessionAsync(
        string callId,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            DirectCallProtocol.WebRtcCloseActions,
            CreateCallContext(callId),
            cancellationToken).ConfigureAwait(false);

        return response.IsSuccess
            ? Result.Success()
            : Result.Failure(response.Error!);
    }

    public async Task<Result<string>> SendInviteAsync(
        string targetUserId,
        string mode,
        CancellationToken cancellationToken = default)
    {
        var created = await CreateCallCoreAsync(
            targetUserId,
            string.IsNullOrWhiteSpace(mode) ? null : mode,
            null,
            cancellationToken).ConfigureAwait(false);

        if (created.IsFailure)
            return Result<string>.Failure(created.Error!);

        return Result<string>.Success(created.Value!.CallId);
    }

    public async Task<Result> AcceptAsync(
        string callId,
        CancellationToken cancellationToken = default)
    {
        var result = await AcceptCallAsync(callId, null, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error!);
    }

    public async Task<Result> DeclineAsync(
        string callId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var result = await DeclineCallAsync(callId, reason, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error!);
    }

    public async Task<Result> EndAsync(
        string callId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var result = await HangupCallAsync(callId, reason, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error!);
    }

    public Task<Result> CloseMediaSessionAsync(
        string callId,
        CancellationToken cancellationToken = default)
    {
        return CloseSessionAsync(callId, cancellationToken);
    }

    public async Task<Result> SendIceCandidateAsync(
        string callId,
        string transportId,
        string candidateJson,
        CancellationToken cancellationToken = default)
    {
        var response = await DispatchAsync(
            DirectCallProtocol.WebRtcIceActions,
            CreateCallContext(callId, new Dictionary<string, object?>
            {
                ["transportId"] = Guard.NotNullOrWhiteSpace(transportId, nameof(transportId)),
                ["candidate"] = JsonSerializer.Deserialize<JsonElement>(candidateJson)
            }),
            cancellationToken).ConfigureAwait(false);

        return response.IsSuccess
            ? Result.Success()
            : Result.Failure(response.Error!);
    }

    private async Task<Result<DirectCallSessionInfo>> CreateCallCoreAsync(
        string targetUserId,
        string? mode,
        string? clientRequestId,
        CancellationToken cancellationToken)
    {
        var context = CreateContext(new Dictionary<string, object?>
        {
            ["targetUserId"] = Guard.NotNullOrWhiteSpace(targetUserId, nameof(targetUserId))
        });

        if (!string.IsNullOrWhiteSpace(mode))
            context["mode"] = mode;

        if (!string.IsNullOrWhiteSpace(clientRequestId))
            context["clientRequestId"] = clientRequestId;

        var response = await DispatchAsync(
            DirectCallProtocol.CreateCallActions,
            context,
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<DirectCallSessionInfo>.Failure(response.Error!);

        return ParseSessionInfo(response.Value!, DirectCallStatus.Invited, null);
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
                DirectCallProtocol.Object,
                DirectCallProtocol.Agents.Lifecycle,
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

    private static Result<DirectCallSessionInfo> ParseSessionInfo(
        FeatureResponseEnvelope envelope,
        DirectCallStatus fallbackStatus,
        string? fallbackCallId)
    {
        try
        {
            var root = GetPayloadRoot(envelope);
            var parsed = ParseSessionInfoFromNode(root, fallbackStatus, fallbackCallId);
            if (parsed == null)
            {
                return Result<DirectCallSessionInfo>.Failure(
                    new Error("direct_call.parse_failed", "Call ID is missing in response payload."));
            }

            return Result<DirectCallSessionInfo>.Success(parsed);
        }
        catch (Exception ex)
        {
            return Result<DirectCallSessionInfo>.Failure(
                new Error("direct_call.parse_failed", ex.Message));
        }
    }

    private static Result<IReadOnlyList<DirectCallSessionInfo>> ParseActiveCalls(FeatureResponseEnvelope envelope)
    {
        try
        {
            var root = GetPayloadRoot(envelope);

            if (root.ValueKind == JsonValueKind.Object &&
                ParseSessionInfoFromNode(root, DirectCallStatus.Unknown, null) is { } singleCall)
            {
                return Result<IReadOnlyList<DirectCallSessionInfo>>.Success(new[] { singleCall });
            }

            JsonElement callsNode;
            if (root.ValueKind == JsonValueKind.Array)
            {
                callsNode = root;
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     root.TryGetAnyProperty(out var itemsNode, "items", "calls", "activeCalls", "active_calls") &&
                     itemsNode.ValueKind == JsonValueKind.Array)
            {
                callsNode = itemsNode;
            }
            else
            {
                return Result<IReadOnlyList<DirectCallSessionInfo>>.Success(Array.Empty<DirectCallSessionInfo>());
            }

            var list = new List<DirectCallSessionInfo>();
            foreach (var item in callsNode.EnumerateArray())
            {
                var parsed = ParseSessionInfoFromNode(item, DirectCallStatus.Unknown, null);
                if (parsed != null)
                    list.Add(parsed);
            }

            return Result<IReadOnlyList<DirectCallSessionInfo>>.Success(list);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<DirectCallSessionInfo>>.Failure(
                new Error("direct_call.list_active.parse_failed", ex.Message));
        }
    }

    private static DirectCallSessionInfo? ParseSessionInfoFromNode(
        JsonElement node,
        DirectCallStatus fallbackStatus,
        string? fallbackCallId)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return null;

        if (node.TryGetAnyProperty(out var nestedCall, "call", "session") &&
            nestedCall.ValueKind == JsonValueKind.Object)
        {
            node = nestedCall;
        }

        var callId = node.GetString("callId", "call_id", "sessionId", "session_id", "id") ?? fallbackCallId;
        if (string.IsNullOrWhiteSpace(callId))
            return null;

        var roomId = node.GetString("roomId", "room_id", "mediaRoomId", "media_room_id", "sessionRoomId", "session_room_id") ?? callId;
        var status = ParseStatus(node.GetString("status", "callStatus", "call_status"), fallbackStatus);
        var initiatorUserId = node.GetString("initiatorUserId", "initiator_user_id", "callerUserId", "caller_user_id");

        var participants = ParseParticipants(node);
        var participantPeerIds = ParsePeerList(node, "participantPeerIds", "participant_peer_ids", "activePeerIds", "active_peer_ids");

        return new DirectCallSessionInfo(
            callId,
            roomId,
            status,
            initiatorUserId,
            participants,
            participantPeerIds,
            ParseDateTimeOffset(node.GetString("startedAt", "started_at", "createdAt", "created_at")),
            ParseDateTimeOffset(node.GetString("answeredAt", "answered_at")),
            ParseDateTimeOffset(node.GetString("endedAt", "ended_at")));
    }

    private static IReadOnlyList<DirectCallParticipant> ParseParticipants(JsonElement root)
    {
        if (!root.TryGetAnyProperty(out var participantsNode, "participants", "members") ||
            participantsNode.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<DirectCallParticipant>();
        }

        var result = new List<DirectCallParticipant>();
        foreach (var item in participantsNode.EnumerateArray())
        {
            var userId = item.GetString("userId", "user_id", "id") ??
                         item.GetString("peerId", "peer_id");

            if (string.IsNullOrWhiteSpace(userId))
                continue;

            result.Add(new DirectCallParticipant(
                userId,
                item.GetString("role", "participantRole", "participant_role") ?? string.Empty,
                item.GetString("status", "participantStatus", "participant_status") ?? string.Empty,
                ParseDateTimeOffset(item.GetString("invitedAt", "invited_at", "createdAt", "created_at")),
                ParseDateTimeOffset(item.GetString("answeredAt", "answered_at")),
                ParseDateTimeOffset(item.GetString("leftAt", "left_at", "endedAt", "ended_at")),
                item.GetString("endReason", "end_reason", "reason")));
        }

        return result;
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

    private static DirectCallStatus ParseStatus(string? rawStatus, DirectCallStatus fallbackStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
            return fallbackStatus;

        return rawStatus.Trim().ToLowerInvariant() switch
        {
            "invited" => DirectCallStatus.Invited,
            "ringing" => DirectCallStatus.Ringing,
            "accepted" => DirectCallStatus.Accepted,
            "declined" => DirectCallStatus.Declined,
            "ended" => DirectCallStatus.Ended,
            _ => fallbackStatus
        };
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTimeOffset.TryParse(value, out var parsed))
            return parsed;

        if (long.TryParse(value, out var unixMs))
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs);

        return null;
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

    private static Dictionary<string, object?> CreateCallContext(
        string callId,
        IDictionary<string, object?>? additions = null)
    {
        var context = CreateContext(additions);
        context["callId"] = Guard.NotNullOrWhiteSpace(callId, nameof(callId));
        return context;
    }

    private static Dictionary<string, object?> CreateContext(IDictionary<string, object?>? additions)
    {
        var context = new Dictionary<string, object?>();
        if (additions == null)
            return context;

        foreach (var pair in additions)
            context[pair.Key] = pair.Value;

        return context;
    }

    private static JsonElement GetPayloadRoot(FeatureResponseEnvelope envelope)
    {
        if (envelope.TryGetPayload(out var payload))
        {
            if (payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetAnyProperty(out var nestedData, "data") &&
                (nestedData.ValueKind == JsonValueKind.Object || nestedData.ValueKind == JsonValueKind.Array))
            {
                return nestedData;
            }

            if (payload.ValueKind == JsonValueKind.Object || payload.ValueKind == JsonValueKind.Array)
                return payload;
        }

        if (!string.IsNullOrWhiteSpace(envelope.Message))
        {
            using var doc = JsonDocument.Parse(envelope.Message);
            var root = doc.RootElement.Clone();
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetAnyProperty(out var nestedData, "data") &&
                (nestedData.ValueKind == JsonValueKind.Object || nestedData.ValueKind == JsonValueKind.Array))
            {
                return nestedData.Clone();
            }

            return root;
        }

        throw new InvalidOperationException("Direct call payload is missing.");
    }
}
