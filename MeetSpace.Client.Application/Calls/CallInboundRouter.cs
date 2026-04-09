using MeetSpace.Client.App.Session;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Domain.Calls;
using MeetSpace.Client.Realtime.Abstractions;
using System;

namespace MeetSpace.Client.App.Calls;

public sealed class CallInboundRouter : IDisposable
{
    private readonly IRealtimeGateway _gateway;
    private readonly CallStore _store;
    private readonly SessionStore _sessionStore;

    public CallInboundRouter(
        IRealtimeGateway gateway,
        CallStore store,
        SessionStore sessionStore)
    {
        _gateway = gateway;
        _store = store;
        _sessionStore = sessionStore;

        _gateway.EnvelopeReceived += OnEnvelopeReceived;
    }

    private void OnEnvelopeReceived(object? sender, FeatureResponseEnvelope envelope)
    {
        if (string.Equals(envelope.Type, ProtocolMessageTypes.DispatchResult, StringComparison.Ordinal) &&
            string.Equals(envelope.Object, "mediasoup", StringComparison.Ordinal))
        {
            if (envelope.Ok == false)
            {
                _store.SetError(envelope.Message ?? "Media transport request failed.");
            }
            return;
        }

        if (!string.Equals(envelope.Type, ProtocolMessageTypes.AudioSessionLifecycle, StringComparison.Ordinal))
            return;

        var roomId = envelope.GetString("roomId");
        if (string.IsNullOrWhiteSpace(roomId))
            return;

        if (!string.Equals(_store.Current.Session.RoomId, roomId, StringComparison.Ordinal))
            return;

        var started = envelope.GetBoolean("started") == true;
        var ended = envelope.GetBoolean("ended") == true;

        if (started && _store.Current.Session.Stage < CallConnectionStage.Connected)
        {
            _store.SetStage(CallConnectionStage.Connected, roomId: roomId);
        }

        if (ended)
        {
            _store.SetStage(CallConnectionStage.Idle);
        }

        if (envelope.TryGetElement("memberPeerIds", out var membersElement) &&
            membersElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var selfPeer = _sessionStore.Current.TrustedPeer;
            var participants = new List<RemoteParticipantState>();

            foreach (var item in membersElement.EnumerateArray())
            {
                if (item.ValueKind != System.Text.Json.JsonValueKind.String)
                    continue;

                var peerId = item.GetString();
                if (string.IsNullOrWhiteSpace(peerId))
                    continue;

                if (string.Equals(peerId, selfPeer, StringComparison.Ordinal))
                    continue;

                participants.Add(new RemoteParticipantState(
                    peerId,
                    HasAudio: true,
                    HasVideo: false,
                    HasScreenShare: false,
                    IsSpeaking: false));
            }

            _store.SetParticipants(participants);
        }
    }

    public void Dispose()
    {
        _gateway.EnvelopeReceived -= OnEnvelopeReceived;
    }
}