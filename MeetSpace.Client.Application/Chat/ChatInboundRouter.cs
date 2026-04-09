using MeetSpace.Client.App.Session;
using MeetSpace.Client.Contracts.Chats;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Domain.Chat;
using MeetSpace.Client.Realtime.Abstractions;
using MeetSpace.Client.Shared.Abstractions;
using System;

namespace MeetSpace.Client.App.Chat;

public sealed class ChatInboundRouter : IDisposable
{
    private readonly IRealtimeGateway _gateway;
    private readonly ChatStore _store;
    private readonly SessionStore _sessionStore;
    private readonly IClock _clock;

    public ChatInboundRouter(
        IRealtimeGateway gateway,
        ChatStore store,
        SessionStore sessionStore,
        IClock clock)
    {
        _gateway = gateway;
        _store = store;
        _sessionStore = sessionStore;
        _clock = clock;

        _gateway.EnvelopeReceived += OnEnvelopeReceived;
    }

    private void OnEnvelopeReceived(object sender, FeatureResponseEnvelope envelope)
    {
        if (string.Equals(envelope.Type, ChatProtocol.ChatMessageType, StringComparison.Ordinal))
        {
            var conferenceId = envelope.GetString("conferenceId");
            var senderPeerId = envelope.GetString("senderPeerId");
            var text = envelope.GetString("text");

            if (string.IsNullOrWhiteSpace(conferenceId) ||
                string.IsNullOrWhiteSpace(senderPeerId) ||
                text == null)
            {
                return;
            }

            var trustedPeer = _sessionStore.Current.TrustedPeer;
            var sentAtUnixMs = envelope.GetInt64("sentAtUnixMs");
            var sentAt = sentAtUnixMs.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(sentAtUnixMs.Value)
                : _clock.UtcNow;

            var isOwn = string.Equals(trustedPeer, senderPeerId, StringComparison.Ordinal);

            var message = new ChatMessageItem(
                localId: envelope.GetString("messageId") ?? Guid.NewGuid().ToString("N"),
                messageId: envelope.GetString("messageId"),
                conferenceId: conferenceId,
                senderPeerId: senderPeerId,
                text: text,
                sentAtUtc: sentAt,
                isOwn: isOwn,
                status: isOwn ? ChatDeliveryState.Sent : ChatDeliveryState.Received,
                clientRequestId: envelope.GetString("clientRequestId"),
                targetPeerId: envelope.GetString("targetPeerId"));

            _store.UpsertMessage(message);
            return;
        }

        if (string.Equals(envelope.Type, ProtocolMessageTypes.DispatchResult, StringComparison.Ordinal) &&
     string.Equals(envelope.Object, ChatProtocol.Object, StringComparison.Ordinal))
        {
            var clientRequestId = envelope.GetString("clientRequestId");

            if (envelope.Ok == false)
            {
                if (!string.IsNullOrWhiteSpace(clientRequestId))
                    _store.MarkLocalFailed(clientRequestId, envelope.Message ?? "Chat request was rejected by server.");
                else
                    _store.SetLastError(envelope.Message ?? "Chat request was rejected by server.");

                return;
            }

            if (envelope.Ok == true && !string.IsNullOrWhiteSpace(clientRequestId))
            {
                _store.MarkLocalDelivered(clientRequestId);
                return;
            }
        }
    }

    public void Dispose()
    {
        _gateway.EnvelopeReceived -= OnEnvelopeReceived;
    }
}