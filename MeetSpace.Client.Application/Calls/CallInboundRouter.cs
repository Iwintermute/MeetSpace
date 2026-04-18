using System.Text.Json;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Contracts.Calls;
using MeetSpace.Client.Contracts.Conference;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Domain.Calls;
using MeetSpace.Client.Realtime.Abstractions;
using MeetSpace.Client.Shared.Json;

namespace MeetSpace.Client.App.Calls;

public sealed class CallInboundRouter : IDisposable
{
    private readonly IRealtimeGateway _gateway;
    private readonly CallStore _callStore;
    private readonly SessionStore _sessionStore;

    public CallInboundRouter(
        IRealtimeGateway gateway,
        CallStore callStore,
        SessionStore sessionStore)
    {
        _gateway = gateway;
        _callStore = callStore;
        _sessionStore = sessionStore;

        _gateway.EnvelopeReceived += OnEnvelopeReceived;
    }

    private void OnEnvelopeReceived(object? sender, FeatureResponseEnvelope envelope)
    {
        if (!TryResolveSessionScope(envelope, out var sessionId, out var kind))
            return;

        if (!string.IsNullOrWhiteSpace(_callStore.Current.SessionId) &&
            !string.IsNullOrWhiteSpace(sessionId) &&
            !string.Equals(_callStore.Current.SessionId, sessionId, StringComparison.Ordinal))
        {
            return;
        }

        if (IsDirectLifecycleEvent(envelope.Type))
        {
            HandleDirectLifecycleEvent(envelope, sessionId);
            return;
        }

        if (IsMediaLifecycleEvent(envelope.Type))
            HandleMediaLifecycleEvent(envelope, sessionId, kind);

        if (IsPeerOrTrackEvent(envelope.Type))
            HandlePeerOrTrackEvent(envelope, sessionId, kind);
    }

    private void HandleDirectLifecycleEvent(FeatureResponseEnvelope envelope, string? callId)
    {
        if (string.Equals(envelope.Type, ProtocolMessageTypes.DirectCallInvite, StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(callId))
            {
                _callStore.SetStage(
                    CallConnectionStage.JoiningRoom,
                    _callStore.Current.ConversationId,
                    _callStore.Current.RoomId,
                    _callStore.Current.TransportId,
                    callId,
                    CallKind.Direct);
            }

            return;
        }

        if (string.Equals(envelope.Type, ProtocolMessageTypes.DirectCallAccepted, StringComparison.Ordinal))
        {
            _callStore.SetStage(
                CallConnectionStage.Connected,
                _callStore.Current.ConversationId,
                _callStore.Current.RoomId,
                _callStore.Current.TransportId,
                callId ?? _callStore.Current.SessionId,
                CallKind.Direct);
            return;
        }

        if (string.Equals(envelope.Type, ProtocolMessageTypes.DirectCallDeclined, StringComparison.Ordinal) ||
            string.Equals(envelope.Type, ProtocolMessageTypes.DirectCallEnded, StringComparison.Ordinal))
        {
            _callStore.Reset();
        }
    }

    private void HandleMediaLifecycleEvent(FeatureResponseEnvelope envelope, string? sessionId, CallKind kind)
    {
        if (string.Equals(envelope.Type, ProtocolMessageTypes.SessionStarted, StringComparison.Ordinal))
        {
            _callStore.SetStage(
                CallConnectionStage.Negotiating,
                _callStore.Current.ConversationId,
                _callStore.Current.RoomId,
                _callStore.Current.TransportId,
                sessionId ?? _callStore.Current.SessionId,
                kind);
            return;
        }

        if (string.Equals(envelope.Type, ProtocolMessageTypes.SessionEnded, StringComparison.Ordinal) ||
            string.Equals(envelope.Type, ProtocolMessageTypes.SessionClosed, StringComparison.Ordinal))
        {
            _callStore.Reset();
            return;
        }

        if (string.Equals(envelope.Type, ProtocolMessageTypes.TransportError, StringComparison.Ordinal))
        {
            _callStore.SetStage(
                CallConnectionStage.Faulted,
                _callStore.Current.ConversationId,
                _callStore.Current.RoomId,
                _callStore.Current.TransportId,
                sessionId ?? _callStore.Current.SessionId,
                kind);
        }
    }

    private void HandlePeerOrTrackEvent(FeatureResponseEnvelope envelope, string? sessionId, CallKind kind)
    {
        var selfPeerId = _sessionStore.Current.SelfPeerId;
        var peerId = ResolvePeerId(envelope);
        if (string.IsNullOrWhiteSpace(peerId) || string.Equals(peerId, selfPeerId, StringComparison.Ordinal))
            return;

        var participants = _callStore.Current.Participants
            .ToDictionary(x => x.PeerId, x => x, StringComparer.Ordinal);

        if (!participants.TryGetValue(peerId, out var participant))
        {
            participant = new RemoteParticipantState(
                peerId,
                HasAudio: false,
                HasVideo: false,
                HasScreenShare: false,
                IsSpeaking: false);
        }

        if (string.Equals(envelope.Type, ProtocolMessageTypes.PeerJoined, StringComparison.Ordinal))
        {
            participants[peerId] = participant;
        }
        else if (string.Equals(envelope.Type, ProtocolMessageTypes.PeerLeft, StringComparison.Ordinal))
        {
            participants.Remove(peerId);
        }
        else if (string.Equals(envelope.Type, ProtocolMessageTypes.TrackPublished, StringComparison.Ordinal))
        {
            participants[peerId] = ApplyTrackState(participant, ResolveTrackType(envelope), true);
        }
        else if (string.Equals(envelope.Type, ProtocolMessageTypes.TrackClosed, StringComparison.Ordinal))
        {
            participants[peerId] = ApplyTrackState(participant, ResolveTrackType(envelope), false);
        }
        else
        {
            return;
        }

        _callStore.SetParticipants(
            participants.Values
                .OrderBy(x => x.PeerId, StringComparer.Ordinal)
                .ToList());

        if (kind != CallKind.Unknown || !string.IsNullOrWhiteSpace(sessionId))
        {
            _callStore.SetStage(
                _callStore.Current.Stage,
                _callStore.Current.ConversationId,
                _callStore.Current.RoomId,
                _callStore.Current.TransportId,
                sessionId ?? _callStore.Current.SessionId,
                kind == CallKind.Unknown ? null : kind);
        }
    }

    private static bool TryResolveSessionScope(
        FeatureResponseEnvelope envelope,
        out string? sessionId,
        out CallKind kind)
    {
        sessionId = null;
        kind = CallKind.Unknown;

        var objectName = envelope.Object ?? string.Empty;
        if (string.Equals(objectName, DirectCallProtocol.Object, StringComparison.Ordinal) ||
            IsDirectLifecycleEvent(envelope.Type))
        {
            kind = CallKind.Direct;
            sessionId = envelope.GetString("callId") ??
                        envelope.GetString("sessionId");

            if (envelope.TryGetPayload(out var payload) && payload.ValueKind == JsonValueKind.Object)
            {
                sessionId ??= payload.GetString("callId", "call_id", "sessionId", "session_id", "id");
            }

            return true;
        }

        if (string.Equals(objectName, ConferenceProtocol.Object, StringComparison.Ordinal))
        {
            kind = CallKind.Conference;
            sessionId = envelope.GetString("conferenceId") ??
                        envelope.GetString("conferencePublicId");

            if (envelope.TryGetPayload(out var payload) && payload.ValueKind == JsonValueKind.Object)
            {
                sessionId ??= payload.GetString(
                    "conferenceId",
                    "conference_id",
                    "conferencePublicId",
                    "conference_public_id",
                    "id");
            }

            return true;
        }

        return false;
    }

    private static bool IsDirectLifecycleEvent(string? type)
    {
        return string.Equals(type, ProtocolMessageTypes.DirectCallInvite, StringComparison.Ordinal) ||
               string.Equals(type, ProtocolMessageTypes.DirectCallAccepted, StringComparison.Ordinal) ||
               string.Equals(type, ProtocolMessageTypes.DirectCallDeclined, StringComparison.Ordinal) ||
               string.Equals(type, ProtocolMessageTypes.DirectCallEnded, StringComparison.Ordinal);
    }

    private static bool IsMediaLifecycleEvent(string? type)
    {
        return string.Equals(type, ProtocolMessageTypes.SessionStarted, StringComparison.Ordinal) ||
               string.Equals(type, ProtocolMessageTypes.SessionEnded, StringComparison.Ordinal) ||
               string.Equals(type, ProtocolMessageTypes.SessionClosed, StringComparison.Ordinal) ||
               string.Equals(type, ProtocolMessageTypes.TransportOpened, StringComparison.Ordinal) ||
               string.Equals(type, ProtocolMessageTypes.TransportError, StringComparison.Ordinal) ||
               string.Equals(type, ProtocolMessageTypes.RoomState, StringComparison.Ordinal);
    }

    private static bool IsPeerOrTrackEvent(string? type)
    {
        return string.Equals(type, ProtocolMessageTypes.PeerJoined, StringComparison.Ordinal) ||
               string.Equals(type, ProtocolMessageTypes.PeerLeft, StringComparison.Ordinal) ||
               string.Equals(type, ProtocolMessageTypes.TrackPublished, StringComparison.Ordinal) ||
               string.Equals(type, ProtocolMessageTypes.TrackClosed, StringComparison.Ordinal) ||
               string.Equals(type, ProtocolMessageTypes.ConsumerResumed, StringComparison.Ordinal);
    }

    private static string? ResolvePeerId(FeatureResponseEnvelope envelope)
    {
        var peerId = envelope.GetString("peerId") ??
                     envelope.GetString("producerPeerId") ??
                     envelope.GetString("memberPeerId");

        if (envelope.TryGetPayload(out var payload) && payload.ValueKind == JsonValueKind.Object)
        {
            peerId ??= payload.GetString(
                "peerId",
                "peer_id",
                "producerPeerId",
                "producer_peer_id",
                "memberPeerId",
                "member_peer_id");
        }

        return peerId;
    }

    private static string ResolveTrackType(FeatureResponseEnvelope envelope)
    {
        var trackType = envelope.GetString("trackType") ??
                        envelope.GetString("kind");

        if (envelope.TryGetPayload(out var payload) && payload.ValueKind == JsonValueKind.Object)
        {
            trackType ??= payload.GetString("trackType", "track_type", "kind");
        }

        if (string.IsNullOrWhiteSpace(trackType))
            return "microphone";

        var normalized = trackType.Trim().ToLowerInvariant();
        return normalized switch
        {
            "audio" => "microphone",
            "mic" => "microphone",
            "microphone" => "microphone",
            "video" => "camera",
            "camera" => "camera",
            "screen_share" => "screen",
            "screenshare" => "screen",
            "screen" => "screen",
            _ => normalized
        };
    }

    private static RemoteParticipantState ApplyTrackState(
        RemoteParticipantState participant,
        string trackType,
        bool isPublished)
    {
        return trackType switch
        {
            "microphone" => participant with { HasAudio = isPublished },
            "camera" => participant with { HasVideo = isPublished },
            "screen" => participant with { HasScreenShare = isPublished },
            _ => participant
        };
    }

    public void Dispose()
    {
        _gateway.EnvelopeReceived -= OnEnvelopeReceived;
    }
}
