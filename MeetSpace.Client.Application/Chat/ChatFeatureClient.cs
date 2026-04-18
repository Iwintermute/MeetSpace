using System.Text.Json;
using MeetSpace.Client.Contracts.Chats;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Realtime.Rpc;
using MeetSpace.Client.Shared.Json;
using MeetSpace.Client.Shared.Results;
using MeetSpace.Client.Shared.Utilities;

namespace MeetSpace.Client.App.Chat;

public sealed class ChatFeatureClient : IChatFeatureClient
{
    private readonly IRealtimeRpcClient _rpcClient;

    public ChatFeatureClient(IRealtimeRpcClient rpcClient)
    {
        _rpcClient = rpcClient;
    }

    public async Task<Result<ChatSendAck>> SendMessageAsync(
        string conferenceId,
        string text,
        string? targetPeerId = null,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default)
    {
        conferenceId = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId));
        text = Guard.NotNullOrWhiteSpace(text, nameof(text));

        var requestId = string.IsNullOrWhiteSpace(clientRequestId)
            ? Guid.NewGuid().ToString("N")
            : clientRequestId;

        var ctx = new Dictionary<string, object?>
        {
            ["conferenceId"] = conferenceId,
            ["text"] = text,
            ["clientRequestId"] = requestId
        };

        if (!string.IsNullOrWhiteSpace(targetPeerId))
            ctx["targetPeerId"] = targetPeerId;

        var response = await _rpcClient.DispatchAsync(
            ChatProtocol.Object,
            ChatProtocol.Agents.Messaging,
            ChatProtocol.Actions.SendMessage,
            ctx,
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<ChatSendAck>.Failure(response.Error!);

        string? messageId = response.Value!.GetString("messageId");
        DateTimeOffset? sentAt = null;
        string? canonicalConversationId = response.Value.GetString("conversationId");

        if (response.Value.TryGetPayload(out var payload) && payload.ValueKind == JsonValueKind.Object)
        {
            messageId ??= payload.GetString("messageId", "message_id", "id");
            canonicalConversationId ??= payload.GetString("conversationId", "conversation_id", "dialogId", "dialog_id", "conferenceId", "conference_id");

            var unixMs = payload.GetInt64("sentAtUnixMs", "sent_at_unix_ms", "timestamp");
            if (unixMs.HasValue)
                sentAt = DateTimeOffset.FromUnixTimeMilliseconds(unixMs.Value);
        }

        return Result<ChatSendAck>.Success(new ChatSendAck(
            requestId,
            messageId,
            sentAt,
            canonicalConversationId));
    }
}