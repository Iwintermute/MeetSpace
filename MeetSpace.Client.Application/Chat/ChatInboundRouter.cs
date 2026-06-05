using System.Text.Json;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Domain.Chat;
using MeetSpace.Client.Realtime.Abstractions;
using MeetSpace.Client.Shared.Abstractions;
using MeetSpace.Client.Shared.Json;
using MeetSpace.Client.Shared.Utilities;

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

    private void OnEnvelopeReceived(object? sender, FeatureResponseEnvelope envelope)
    {
        if (string.Equals(envelope.Type, ProtocolMessageTypes.DirectChatMessage, StringComparison.Ordinal))
        {
            UpsertDirectInboundMessage(envelope);
            return;
        }

        if (string.Equals(envelope.Type, ProtocolMessageTypes.ConferenceChatMessage, StringComparison.Ordinal) &&
            string.Equals(envelope.Object, "chat", StringComparison.Ordinal))
        {
            UpsertConferenceInboundMessage(envelope);
            return;
        }

        if (string.Equals(envelope.Type, ProtocolMessageTypes.DispatchResult, StringComparison.Ordinal))
        {
            HandleDispatchResult(envelope);
        }
    }

    private void HandleDispatchResult(FeatureResponseEnvelope envelope)
    {
        var objectName = envelope.Object ?? string.Empty;
        if (!string.Equals(objectName, "direct_chat", StringComparison.Ordinal) &&
            !string.Equals(objectName, "chat", StringComparison.Ordinal))
        {
            return;
        }

        var clientRequestId = envelope.GetString("clientRequestId");
        if (string.IsNullOrWhiteSpace(clientRequestId))
            clientRequestId = envelope.GetRequestId();

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
            var ack = ChatPayloadParser.ParseAck(envelope, clientRequestId);
            _store.MarkLocalDelivered(
                ack.ClientRequestId,
                ack.MessageId,
                ack.SentAtUtc,
                ack.ConversationId);
        }
    }

    private void UpsertDirectInboundMessage(FeatureResponseEnvelope envelope)
    {
        var selfPeerId = _sessionStore.Current.SelfPeerId;
        if (string.IsNullOrWhiteSpace(selfPeerId))
            return;
        string? senderUserId = envelope.GetString("senderUserId");
        string? senderPeerId = envelope.GetString("senderPeerId") ?? senderUserId;
        string? senderDisplayName = envelope.GetString("senderDisplayName");
        string? senderEmail = envelope.GetString("senderEmail");
        string? targetUserId = envelope.GetString("targetUserId");
        string? targetPeerId = envelope.GetString("targetPeerId") ?? targetUserId;
        string? bodyType = envelope.GetString("bodyType") ?? "text";
        string? fileName = envelope.GetString("fileName");
        string? mimeType = envelope.GetString("mimeType");
        long? fileSizeBytes = envelope.GetInt64("fileSizeBytes");
        string? fileContentBase64 = envelope.GetString("fileContentBase64");
        string? text = envelope.GetString("text");
        string? messageId = envelope.GetString("messageId");
        string? clientRequestId = envelope.GetString("clientRequestId");
        string? conversationId = envelope.GetString("conversationId") ?? envelope.GetString("threadId");
        long? sentAtUnixMs = envelope.GetInt64("sentAtUnixMs");
        string? sentAtRaw = envelope.GetString("createdAt");

        if (envelope.TryGetPayload(out var payload) && payload.ValueKind == JsonValueKind.Object)
        {
            senderPeerId ??= payload.GetString(
                "senderPeerId",
                "sender_peer_id",
                "peerId",
                "peer_id",
                "sender",
                "senderUserId",
                "sender_user_id");
            senderUserId ??= payload.GetString("senderUserId", "sender_user_id");
            senderDisplayName ??= payload.GetString(
                "senderDisplayName",
                "sender_display_name",
                "displayName",
                "display_name",
                "senderName",
                "sender_name");
            senderEmail ??= payload.GetString("senderEmail", "sender_email");

            targetUserId ??= payload.GetString("targetUserId", "target_user_id");
            targetPeerId ??= payload.GetString(
                "targetPeerId",
                "target_peer_id",
                "targetUserId",
                "target_user_id");
            bodyType = payload.GetString("bodyType", "body_type") ?? bodyType ?? "text";
            fileName ??= payload.GetString("fileName", "file_name");
            mimeType ??= payload.GetString("mimeType", "mime_type");
            fileSizeBytes ??= payload.GetInt64("fileSizeBytes", "file_size_bytes");
            fileContentBase64 ??= payload.GetString("fileContentBase64", "file_content_base64");
            var metadata = payload.GetObject("metadata", "meta");
            if (metadata.HasValue)
            {
                fileName ??= metadata.Value.GetString("fileName", "file_name");
                mimeType ??= metadata.Value.GetString("mimeType", "mime_type");
                fileSizeBytes ??= metadata.Value.GetInt64("fileSizeBytes", "file_size_bytes");
                fileContentBase64 ??= metadata.Value.GetString("fileContentBase64", "file_content_base64");
            }

            text ??= payload.GetString("text", "message", "body");
            messageId ??= payload.GetString("messageId", "message_id", "id");
            clientRequestId ??= payload.GetString("clientRequestId", "client_request_id");
            conversationId ??= payload.GetString(
                "conversationId",
                "conversation_id",
                "dialogId",
                "dialog_id",
                "threadId",
                "thread_id");
            sentAtUnixMs ??= payload.GetInt64("sentAtUnixMs", "sent_at_unix_ms", "timestamp");
            sentAtRaw ??= payload.GetString("createdAt", "created_at", "sentAt", "sent_at");
        }

        var fileAttachment = BuildFileAttachment(
            fileName,
            mimeType,
            fileSizeBytes,
            fileContentBase64,
            string.Equals(bodyType, "file", StringComparison.OrdinalIgnoreCase) ? text : null);
        text = BuildDisplayText(text, bodyType, fileAttachment);
        var senderIdentity = senderUserId ?? senderPeerId;
        if (string.IsNullOrWhiteSpace(senderIdentity) || string.IsNullOrWhiteSpace(text))
            return;

        var isOwn = !string.IsNullOrWhiteSpace(senderPeerId) &&
                    string.Equals(senderPeerId, selfPeerId, StringComparison.Ordinal);
        var counterpartId = isOwn ? (targetUserId ?? targetPeerId) : senderIdentity;
        if (string.IsNullOrWhiteSpace(counterpartId))
            return;

        conversationId ??= ConversationKeys.BuildDirectDialogId(selfPeerId!, counterpartId);

        var message = new ChatMessageItem(
            localId: messageId ?? clientRequestId ?? Guid.NewGuid().ToString("N"),
            messageId: messageId,
            conversationId: conversationId,
            senderPeerId: senderIdentity,
            text: text,
            sentAtUtc: ResolveTimestamp(sentAtUnixMs, sentAtRaw),
            isOwn: isOwn,
            status: isOwn ? ChatDeliveryState.Sent : ChatDeliveryState.Received,
            clientRequestId: clientRequestId,
            isDirect: true,
            targetId: counterpartId,
            senderUserId: senderUserId,
            senderDisplayName: senderDisplayName,
            senderEmail: senderEmail,
            bodyType: bodyType,
            fileAttachment: fileAttachment);

        _store.UpsertMessage(message);
    }

    private void UpsertConferenceInboundMessage(FeatureResponseEnvelope envelope)
    {
        var selfPeerId = _sessionStore.Current.SelfPeerId;
        if (string.IsNullOrWhiteSpace(selfPeerId))
            return;

        string? conferenceId = envelope.GetString("conferenceId") ?? envelope.GetString("conferencePublicId");
        string? senderUserId = envelope.GetString("senderUserId");
        string? senderPeerId = envelope.GetString("senderPeerId") ?? senderUserId;
        string? senderDisplayName = envelope.GetString("senderDisplayName");
        string? senderEmail = envelope.GetString("senderEmail");
        string? text = envelope.GetString("text");
        string? messageId = envelope.GetString("messageId");
        string? clientRequestId = envelope.GetString("clientRequestId");
        long? sentAtUnixMs = envelope.GetInt64("sentAtUnixMs");
        string? sentAtRaw = envelope.GetString("createdAt");

        if (envelope.TryGetPayload(out var payload) && payload.ValueKind == JsonValueKind.Object)
        {
            conferenceId ??= payload.GetString(
                "conferenceId",
                "conference_id",
                "conferencePublicId",
                "conference_public_id",
                "conversationId",
                "conversation_id");
            senderPeerId ??= payload.GetString(
                "senderPeerId",
                "sender_peer_id",
                "peerId",
                "peer_id",
                "sender",
                "senderUserId",
                "sender_user_id");
            senderUserId ??= payload.GetString("senderUserId", "sender_user_id");
            senderDisplayName ??= payload.GetString(
                "senderDisplayName",
                "sender_display_name",
                "displayName",
                "display_name",
                "senderName",
                "sender_name");
            senderEmail ??= payload.GetString("senderEmail", "sender_email");
            text ??= payload.GetString("text", "message", "body");
            messageId ??= payload.GetString("messageId", "message_id", "id");
            clientRequestId ??= payload.GetString("clientRequestId", "client_request_id");
            sentAtUnixMs ??= payload.GetInt64("sentAtUnixMs", "sent_at_unix_ms", "timestamp");
            sentAtRaw ??= payload.GetString("createdAt", "created_at", "sentAt", "sent_at");
        }

        var senderIdentity = senderPeerId ?? senderUserId;
        if (string.IsNullOrWhiteSpace(conferenceId) ||
            string.IsNullOrWhiteSpace(senderIdentity) ||
            text is null)
        {
            return;
        }

        var isOwn = !string.IsNullOrWhiteSpace(senderPeerId) &&
                    string.Equals(selfPeerId, senderPeerId, StringComparison.Ordinal);

        var message = new ChatMessageItem(
            localId: messageId ?? clientRequestId ?? Guid.NewGuid().ToString("N"),
            messageId: messageId,
            conversationId: conferenceId,
            senderPeerId: senderIdentity,
            text: text,
            sentAtUtc: ResolveTimestamp(sentAtUnixMs, sentAtRaw),
            isOwn: isOwn,
            status: isOwn ? ChatDeliveryState.Sent : ChatDeliveryState.Received,
            clientRequestId: clientRequestId,
            isDirect: false,
            targetId: conferenceId,
            senderUserId: senderUserId,
            senderDisplayName: senderDisplayName,
            senderEmail: senderEmail);

        _store.UpsertMessage(message);
    }

    public void Dispose()
    {
        _gateway.EnvelopeReceived -= OnEnvelopeReceived;
    }

    private static ChatFileAttachment? BuildFileAttachment(
        string? fileName,
        string? mimeType,
        long? fileSizeBytes,
        string? fileContentBase64,
        string? fallbackFileName)
    {
        fileName ??= fallbackFileName;
        if (string.IsNullOrWhiteSpace(fileName) &&
            string.IsNullOrWhiteSpace(mimeType) &&
            !fileSizeBytes.HasValue &&
            string.IsNullOrWhiteSpace(fileContentBase64))
        {
            return null;
        }

        return new ChatFileAttachment(fileName ?? "file.bin", mimeType, fileSizeBytes, fileContentBase64);
    }

    private static string? BuildDisplayText(string? rawText, string? bodyType, ChatFileAttachment? fileAttachment)
    {
        if (string.Equals(bodyType, "file", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = fileAttachment?.FileName;
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = rawText;
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "Файл";
            return "📎 " + fileName;
        }

        return rawText;
    }

    private DateTimeOffset ResolveTimestamp(long? unixMs, string? raw)
    {
        if (unixMs.HasValue)
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs.Value);

        if (!string.IsNullOrWhiteSpace(raw) && DateTimeOffset.TryParse(raw, out var parsed))
            return parsed;

        if (!string.IsNullOrWhiteSpace(raw) && long.TryParse(raw, out var rawUnix))
            return DateTimeOffset.FromUnixTimeMilliseconds(rawUnix);

        return _clock.UtcNow;
    }
}
