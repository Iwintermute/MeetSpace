using System.Text.Json;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Domain.Chat;
using MeetSpace.Client.Shared.Json;
using MeetSpace.Client.Shared.Utilities;

namespace MeetSpace.Client.App.Chat;

internal static class ChatPayloadParser
{
    public static ChatSendAck ParseAck(FeatureResponseEnvelope envelope, string fallbackClientRequestId)
    {
        string? messageId = envelope.GetString("messageId");
        DateTimeOffset? sentAt = null;
        string? conversationId = envelope.GetString("conversationId");

        if (envelope.TryGetPayload(out var payload) && payload.ValueKind == JsonValueKind.Object)
        {
            messageId ??= payload.GetString("messageId", "message_id", "id");
            conversationId ??= payload.GetString(
                "conversationId",
                "conversation_id",
                "dialogId",
                "dialog_id",
                "conferenceId",
                "conference_id",
                "threadId",
                "thread_id");

            sentAt = ParseTimestamp(
                payload,
                DateTimeOffset.UtcNow,
                "sentAtUnixMs",
                "sent_at_unix_ms",
                "timestamp",
                "createdAt",
                "created_at",
                "sentAt",
                "sent_at");
        }

        return new ChatSendAck(
            fallbackClientRequestId,
            messageId,
            sentAt,
            conversationId);
    }

    public static IReadOnlyList<ChatDialogItem> ParseDirectDialogs(FeatureResponseEnvelope envelope, string selfPeerId)
    {
        if (!envelope.TryGetPayload(out var payload))
            return Array.Empty<ChatDialogItem>();

        JsonElement arrayRoot;

        if (payload.ValueKind == JsonValueKind.Array)
        {
            arrayRoot = payload;
        }
        else if (payload.ValueKind == JsonValueKind.Object &&
                 payload.TryGetAnyProperty(out var dialogsNode, "threads", "dialogs", "items", "conversations") &&
                 dialogsNode.ValueKind == JsonValueKind.Array)
        {
            arrayRoot = dialogsNode;
        }
        else
        {
            return Array.Empty<ChatDialogItem>();
        }

        var result = new List<ChatDialogItem>();

        foreach (var item in arrayRoot.EnumerateArray())
        {
            var peerId = item.GetString(
                "counterpartUserId",
                "counterpart_user_id",
                "targetUserId",
                "target_user_id",
                "peerId",
                "peer_id",
                "otherPeerId",
                "other_peer_id",
                "targetPeerId",
                "target_peer_id");

            if (string.IsNullOrWhiteSpace(peerId))
                continue;

            var conversationId =
                item.GetString(
                    "threadId",
                    "thread_id",
                    "conversationId",
                    "conversation_id",
                    "dialogId",
                    "dialog_id")
                ?? ConversationKeys.BuildDirectDialogId(selfPeerId, peerId);

            var title = item.GetString(
                "title",
                "displayName",
                "display_name",
                "counterpartDisplayName",
                "counterpart_display_name")
                ?? peerId;

            var subtitle = item.GetString("subtitle") ?? "Личный чат";
            var preview = item.GetString(
                "lastMessagePreview",
                "last_message_preview",
                "preview",
                "lastText",
                "last_text")
                ?? string.Empty;

            var lastMessage = item.GetObject("lastMessage", "last_message");
            if (string.IsNullOrWhiteSpace(preview) && lastMessage.HasValue)
                preview = lastMessage.Value.GetString("text", "body", "message") ?? string.Empty;

            var unread = item.GetInt64("unreadCount", "unread_count") ?? 0;

            var lastActivity = ParseTimestamp(
                item,
                DateTimeOffset.MinValue,
                "lastActivityUnixMs",
                "last_activity_unix_ms",
                "timestamp",
                "updatedAtUnixMs",
                "updated_at_unix_ms",
                "lastMessageAt",
                "last_message_at",
                "updatedAt",
                "updated_at");

            if (lastActivity == DateTimeOffset.MinValue && lastMessage.HasValue)
            {
                lastActivity = ParseTimestamp(
                    lastMessage.Value,
                    DateTimeOffset.MinValue,
                    "sentAtUnixMs",
                    "sent_at_unix_ms",
                    "timestamp",
                    "createdAt",
                    "created_at",
                    "sentAt",
                    "sent_at");
            }

            result.Add(new ChatDialogItem
            {
                ConversationId = conversationId,
                Kind = ChatDialogKind.Direct,
                PeerId = peerId,
                Title = title,
                Subtitle = subtitle,
                LastMessagePreview = preview,
                LastActivityUtc = lastActivity,
                UnreadCount = unread < 0 ? 0 : (int)unread,
                IsPinned = item.GetBoolean("isPinned", "is_pinned") ?? false
            });
        }

        return result
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.LastActivityUtc)
            .ToList();
    }

    public static IReadOnlyList<ChatMessageItem> ParseMessages(
        FeatureResponseEnvelope envelope,
        string selfPeerId,
        bool isDirect,
        string fallbackConversationId,
        string? fallbackTargetId = null)
    {
        if (!envelope.TryGetPayload(out var payload))
            return Array.Empty<ChatMessageItem>();

        JsonElement arrayRoot;

        if (payload.ValueKind == JsonValueKind.Array)
        {
            arrayRoot = payload;
        }
        else if (payload.ValueKind == JsonValueKind.Object &&
                 payload.TryGetAnyProperty(out var itemsNode, "messages", "items", "history") &&
                 itemsNode.ValueKind == JsonValueKind.Array)
        {
            arrayRoot = itemsNode;
        }
        else
        {
            return Array.Empty<ChatMessageItem>();
        }

        var result = new List<ChatMessageItem>();

        foreach (var item in arrayRoot.EnumerateArray())
        {
            var senderPeerId = item.GetString(
                "senderPeerId",
                "sender_peer_id",
                "peerId",
                "peer_id",
                "sender",
                "senderUserId",
                "sender_user_id");

            if (string.IsNullOrWhiteSpace(senderPeerId))
                continue;

            var text = item.GetString("text", "message", "body");
            if (text is null)
                continue;

            var targetId = item.GetString(
                "targetPeerId",
                "target_peer_id",
                "peerIdTarget",
                "peer_id_target",
                "targetUserId",
                "target_user_id",
                "counterpartUserId",
                "counterpart_user_id")
                ?? fallbackTargetId;

            var conversationId =
                item.GetString(
                    "conversationId",
                    "conversation_id",
                    "dialogId",
                    "dialog_id",
                    "conferenceId",
                    "conference_id",
                    "threadId",
                    "thread_id")
                ?? (isDirect && !string.IsNullOrWhiteSpace(targetId)
                    ? ConversationKeys.BuildDirectDialogId(selfPeerId, senderPeerId == selfPeerId ? targetId! : senderPeerId)
                    : fallbackConversationId);

            var messageId = item.GetString("messageId", "message_id", "id");
            var clientRequestId = item.GetString("clientRequestId", "client_request_id");

            var sentAt = ParseTimestamp(
                item,
                DateTimeOffset.UtcNow,
                "sentAtUnixMs",
                "sent_at_unix_ms",
                "timestamp",
                "createdAt",
                "created_at",
                "sentAt",
                "sent_at");

            var isOwn = string.Equals(senderPeerId, selfPeerId, StringComparison.Ordinal);

            result.Add(new ChatMessageItem(
                localId: messageId ?? clientRequestId ?? Guid.NewGuid().ToString("N"),
                messageId: messageId,
                conversationId: conversationId,
                senderPeerId: senderPeerId,
                text: text,
                sentAtUtc: sentAt,
                isOwn: isOwn,
                status: isOwn ? ChatDeliveryState.Sent : ChatDeliveryState.Received,
                clientRequestId: clientRequestId,
                isDirect: isDirect,
                targetId: targetId));
        }

        return result
            .OrderBy(x => x.SentAtUtc)
            .ThenBy(x => x.LocalId, StringComparer.Ordinal)
            .ToList();
    }

    private static DateTimeOffset ParseTimestamp(
        JsonElement element,
        DateTimeOffset fallback,
        params string[] names)
    {
        if (element.GetInt64(names) is long unixMs)
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs);

        var raw = element.GetString(names);
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        if (DateTimeOffset.TryParse(raw, out var parsed))
            return parsed;

        if (long.TryParse(raw, out var rawUnix))
            return DateTimeOffset.FromUnixTimeMilliseconds(rawUnix);

        return fallback;
    }
}
