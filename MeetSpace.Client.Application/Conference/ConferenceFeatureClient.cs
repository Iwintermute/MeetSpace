using System.Text.Json;
using MeetSpace.Client.Contracts.Conference;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Domain.Conference;
using MeetSpace.Client.Realtime.Rpc;
using MeetSpace.Client.Shared.Json;
using MeetSpace.Client.Shared.Results;
using MeetSpace.Client.Shared.Utilities;

namespace MeetSpace.Client.App.Conference;

public sealed class ConferenceFeatureClient : IConferenceFeatureClient
{
    private readonly IRealtimeRpcClient _rpcClient;

    public ConferenceFeatureClient(IRealtimeRpcClient rpcClient)
    {
        _rpcClient = rpcClient;
    }

    public async Task<Result<ConferenceDetails>> CreateConferenceAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        var response = await _rpcClient.DispatchFirstAsync(
            ConferenceProtocol.Object,
            ConferenceProtocol.Agents.Lifecycle,
            ConferenceProtocol.CreateActions,
            new Dictionary<string, object?>
            {
                ["conferenceId"] = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId))
            },
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<ConferenceDetails>.Failure(response.Error!);

        return await ParseOrFetchAsync(response.Value!, conferenceId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ConferenceDetails>> GetConferenceAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        var response = await _rpcClient.DispatchFirstAsync(
            ConferenceProtocol.Object,
            ConferenceProtocol.Agents.Lifecycle,
            ConferenceProtocol.GetActions,
            new Dictionary<string, object?>
            {
                ["conferenceId"] = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId))
            },
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<ConferenceDetails>.Failure(response.Error!);

        return ParseConferenceDetails(response.Value!, conferenceId);
    }

    public async Task<Result<ConferenceDetails>> JoinConferenceAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        var response = await _rpcClient.DispatchFirstAsync(
            ConferenceProtocol.Object,
            ConferenceProtocol.Agents.Membership,
            ConferenceProtocol.JoinActions,
            new Dictionary<string, object?>
            {
                ["conferenceId"] = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId))
            },
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<ConferenceDetails>.Failure(response.Error!);

        return await ParseOrFetchAsync(response.Value!, conferenceId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> LeaveConferenceAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        var response = await _rpcClient.DispatchFirstAsync(
            ConferenceProtocol.Object,
            ConferenceProtocol.Agents.Membership,
            ConferenceProtocol.LeaveActions,
            new Dictionary<string, object?>
            {
                ["conferenceId"] = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId))
            },
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        return response.IsSuccess
            ? Result.Success()
            : Result.Failure(response.Error!);
    }

    public async Task<Result> CloseConferenceAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        var response = await _rpcClient.DispatchFirstAsync(
            ConferenceProtocol.Object,
            ConferenceProtocol.Agents.Lifecycle,
            ConferenceProtocol.CloseActions,
            new Dictionary<string, object?>
            {
                ["conferenceId"] = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId))
            },
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        return response.IsSuccess
            ? Result.Success()
            : Result.Failure(response.Error!);
    }

    public async Task<Result<ConferenceDetails>> ListMembersAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        var response = await _rpcClient.DispatchFirstAsync(
            ConferenceProtocol.Object,
            ConferenceProtocol.Agents.Membership,
            ConferenceProtocol.ListMembersActions,
            new Dictionary<string, object?>
            {
                ["conferenceId"] = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId))
            },
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<ConferenceDetails>.Failure(response.Error!);

        return ParseConferenceDetails(response.Value!, conferenceId);
    }

    public async Task<Result<IReadOnlyList<ConferenceSummary>>> ListConferencesAsync(CancellationToken cancellationToken = default)
    {
        var response = await _rpcClient.DispatchFirstAsync(
            ConferenceProtocol.Object,
            ConferenceProtocol.Agents.Directory,
            ConferenceProtocol.ListConferencesActions,
            new Dictionary<string, object?>
            {
                ["limit"] = 200
            },
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<IReadOnlyList<ConferenceSummary>>.Failure(response.Error!);

        return Result<IReadOnlyList<ConferenceSummary>>.Success(ParseConferenceSummaries(response.Value!));
    }

    private async Task<Result<ConferenceDetails>> ParseOrFetchAsync(
        FeatureResponseEnvelope envelope,
        string conferenceId,
        CancellationToken cancellationToken)
    {
        var parsed = ParseConferenceDetails(envelope, conferenceId);
        if (parsed.IsSuccess)
            return parsed;

        return await GetConferenceAsync(conferenceId, cancellationToken).ConfigureAwait(false);
    }

    public static Result<ConferenceDetails> ParseConferenceDetails(FeatureResponseEnvelope envelope, string fallbackConferenceId)
    {
        try
        {
            var root = GetEnvelopeRoot(envelope);
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Result<ConferenceDetails>.Failure(
                    new Error("conference.payload_invalid", "Conference payload is not an object."));
            }

            var conferenceNode = root.GetObject("conference");
            if (conferenceNode.HasValue && conferenceNode.Value.ValueKind == JsonValueKind.Object)
                root = conferenceNode.Value;

            var conferenceId = root.GetString(
                "conferencePublicId",
                "conference_public_id",
                "conferenceId",
                "conference_id",
                "id")
                ?? fallbackConferenceId;

            if (string.IsNullOrWhiteSpace(conferenceId))
            {
                return Result<ConferenceDetails>.Failure(
                    new Error("conference.parse_failed", "Conference ID is missing."));
            }

            var revision = root.GetInt64("revision", "rev") ?? 0L;
            var status = root.GetString("status");
            var isClosed = (root.GetBoolean("isClosed", "is_closed") ?? false) ||
                           string.Equals(status, "closed", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(status, "ended", StringComparison.OrdinalIgnoreCase);
            var ownerPeerId = root.GetString("ownerPeerId", "owner_peer_id", "owner") ?? string.Empty;
            var ownerUserId = root.GetString("ownerUserId", "owner_user_id");
            var title = root.GetString("title", "name");
            var mediaRoomId = root.GetString("mediaRoomId", "media_room_id", "roomId", "room_id");

            var members = new List<ConferenceMember>();
            var membersArray = root.GetArray("members", "participants");
            if (membersArray.HasValue)
            {
                foreach (var item in membersArray.Value.EnumerateArray())
                {
                    var userId = item.GetString("userId", "user_id");
                    var peerId = item.GetString("peerId", "peer_id", "peer") ?? userId;
                    if (string.IsNullOrWhiteSpace(peerId) && string.IsNullOrWhiteSpace(userId))
                        continue;

                    var sessionId = item.GetString("sessionId", "session_id") ?? peerId ?? userId!;
                    var role = item.GetString("role") ?? string.Empty;
                    var membershipStatus = item.GetString("membershipStatus", "membership_status");
                    var isOwner = (item.GetBoolean("isOwner", "is_owner") ?? false) ||
                                  string.Equals(role, "owner", StringComparison.OrdinalIgnoreCase) ||
                                  (!string.IsNullOrWhiteSpace(ownerUserId) && string.Equals(userId, ownerUserId, StringComparison.Ordinal));

                    if (isOwner && string.IsNullOrWhiteSpace(ownerPeerId))
                        ownerPeerId = peerId ?? userId!;

                    members.Add(new ConferenceMember(
                        peerId ?? userId!,
                        sessionId,
                        isOwner,
                        userId,
                        role,
                        membershipStatus,
                        ParseDateTimeOffset(item.GetString("joinedAt", "joined_at")),
                        ParseDateTimeOffset(item.GetString("leftAt", "left_at"))));
                }
            }

            var activePeerIds = new List<string>();
            var activePeersNode = root.GetArray("activePeerIds", "active_peer_ids", "memberPeerIds", "member_peer_ids");
            if (activePeersNode.HasValue)
            {
                foreach (var peer in activePeersNode.Value.EnumerateArray())
                {
                    if (peer.ValueKind != JsonValueKind.String)
                        continue;

                    var value = peer.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        activePeerIds.Add(value);
                }
            }

            return Result<ConferenceDetails>.Success(new ConferenceDetails(
                conferenceId,
                ownerPeerId,
                isClosed,
                revision < 0 ? 0UL : (ulong)revision,
                members,
                title,
                status,
                mediaRoomId,
                activePeerIds));
        }
        catch (Exception ex)
        {
            return Result<ConferenceDetails>.Failure(
                new Error("conference.parse_failed", ex.Message));
        }
    }

    public static IReadOnlyList<ConferenceSummary> ParseConferenceSummaries(FeatureResponseEnvelope envelope)
    {
        try
        {
            var root = GetEnvelopeRoot(envelope);

            JsonElement arrayRoot;

            if (root.ValueKind == JsonValueKind.Array)
            {
                arrayRoot = root;
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     root.TryGetAnyProperty(out var itemsNode, "items", "conferences") &&
                     itemsNode.ValueKind == JsonValueKind.Array)
            {
                arrayRoot = itemsNode;
            }
            else if (root.ValueKind == JsonValueKind.Object &&
                     !string.IsNullOrWhiteSpace(root.GetString("conferencePublicId", "conference_public_id", "conferenceId", "conference_id", "id")))
            {
                var single = ParseSingleSummary(root);
                return single == null
                    ? Array.Empty<ConferenceSummary>()
                    : new[] { single };
            }
            else
            {
                return Array.Empty<ConferenceSummary>();
            }

            var result = new List<ConferenceSummary>();

            foreach (var item in arrayRoot.EnumerateArray())
            {
                var summary = ParseSingleSummary(item);
                if (summary != null)
                    result.Add(summary);
            }

            return result;
        }
        catch
        {
            return Array.Empty<ConferenceSummary>();
        }
    }

    private static ConferenceSummary? ParseSingleSummary(JsonElement item)
    {
        var conferenceId = item.GetString(
            "conferencePublicId",
            "conference_public_id",
            "conferenceId",
            "conference_id",
            "id");

        if (string.IsNullOrWhiteSpace(conferenceId))
            return null;

        var title = item.GetString("title", "name") ?? conferenceId;
        var memberCount = item.GetInt64("memberCount", "member_count", "participantsCount", "participants_count");
        var status = item.GetString("status");
        var membershipStatus = item.GetString("membershipStatus", "membership_status");
        var mediaRoomId = item.GetString("mediaRoomId", "media_room_id", "roomId", "room_id");
        var isClosed = (item.GetBoolean("isClosed", "is_closed") ?? false) ||
                       string.Equals(status, "closed", StringComparison.OrdinalIgnoreCase);
        var updatedAt = ParseDateTimeOffset(item.GetString("updatedAt", "updated_at"));
        var lastMessageAt = ParseDateTimeOffset(item.GetString("lastMessageAt", "last_message_at"));

        if (!memberCount.HasValue && item.GetArray("activePeerIds", "active_peer_ids") is JsonElement activePeers)
            memberCount = activePeers.GetArrayLength();

        return new ConferenceSummary(
            conferenceId,
            title,
            memberCount.GetValueOrDefault() < 0 ? 0 : (int)memberCount.GetValueOrDefault(),
            isClosed,
            status,
            membershipStatus,
            mediaRoomId,
            updatedAt,
            lastMessageAt);
    }

    private static JsonElement GetEnvelopeRoot(FeatureResponseEnvelope envelope)
    {
        if (envelope.TryGetPayload(out var payload))
        {
            if (payload.ValueKind == JsonValueKind.Object || payload.ValueKind == JsonValueKind.Array)
                return payload;
        }

        if (envelope.Extensions is { Count: > 0 })
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(envelope.Extensions));
            return doc.RootElement.Clone();
        }

        if (!string.IsNullOrWhiteSpace(envelope.Message))
        {
            var message = envelope.Message!.Trim();
            if (message.StartsWith("{", StringComparison.Ordinal) ||
                message.StartsWith("[", StringComparison.Ordinal))
            {
                using var doc = JsonDocument.Parse(message);
                return doc.RootElement.Clone();
            }
        }

        throw new InvalidOperationException("Conference response payload is missing.");
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
}
