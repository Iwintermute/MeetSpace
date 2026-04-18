using MeetSpace.Client.Contracts.Chats;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Domain.Chat;
using MeetSpace.Client.Realtime.Rpc;
using MeetSpace.Client.Shared.Results;
using MeetSpace.Client.Shared.Utilities;

namespace MeetSpace.Client.App.Chat;

public sealed class DirectChatFeatureClient : IDirectChatFeatureClient
{
    private readonly IRealtimeRpcClient _rpcClient;

    public DirectChatFeatureClient(IRealtimeRpcClient rpcClient)
    {
        _rpcClient = rpcClient;
    }

    public async Task<Result<ChatSendAck>> SendMessageAsync(
        string targetUserId,
        string text,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default)
    {
        targetUserId = Guard.NotNullOrWhiteSpace(targetUserId, nameof(targetUserId));
        text = Guard.NotNullOrWhiteSpace(text, nameof(text));

        var requestId = string.IsNullOrWhiteSpace(clientRequestId)
            ? Guid.NewGuid().ToString("N")
            : clientRequestId;

        var response = await _rpcClient.DispatchFirstAsync(
            DirectChatProtocol.Object,
            DirectChatProtocol.Agents.Messaging,
            DirectChatProtocol.SendMessageActions,
            new Dictionary<string, object?>
            {
                ["targetUserId"] = targetUserId,
                ["targetPeerId"] = targetUserId,
                ["text"] = text,
                ["clientRequestId"] = requestId
            },
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<ChatSendAck>.Failure(response.Error!);

        return Result<ChatSendAck>.Success(ChatPayloadParser.ParseAck(response.Value!, requestId));
    }

    public async Task<Result<IReadOnlyList<ChatDialogItem>>> ListDialogsAsync(
        string selfPeerId,
        CancellationToken cancellationToken = default)
    {
        selfPeerId = Guard.NotNullOrWhiteSpace(selfPeerId, nameof(selfPeerId));

        var response = await _rpcClient.DispatchFirstAsync(
            DirectChatProtocol.Object,
            DirectChatProtocol.Agents.Sync,
            DirectChatProtocol.ListDialogsActions,
            new Dictionary<string, object?>
            {
                ["limit"] = 200
            },
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<IReadOnlyList<ChatDialogItem>>.Failure(response.Error!);

        return Result<IReadOnlyList<ChatDialogItem>>.Success(
            ChatPayloadParser.ParseDirectDialogs(response.Value!, selfPeerId));
    }

    public async Task<Result<IReadOnlyList<ChatMessageItem>>> GetHistoryAsync(
        string selfPeerId,
        string targetUserId,
        string? threadId = null,
        int limit = 50,
        string? beforeCreatedAt = null,
        CancellationToken cancellationToken = default)
    {
        selfPeerId = Guard.NotNullOrWhiteSpace(selfPeerId, nameof(selfPeerId));
        targetUserId = Guard.NotNullOrWhiteSpace(targetUserId, nameof(targetUserId));

        var ctx = new Dictionary<string, object?>
        {
            ["targetUserId"] = targetUserId,
            ["targetPeerId"] = targetUserId,
            ["limit"] = limit < 1 ? 50 : limit
        };

        if (!string.IsNullOrWhiteSpace(threadId))
            ctx["threadId"] = threadId;

        if (!string.IsNullOrWhiteSpace(beforeCreatedAt))
            ctx["beforeCreatedAt"] = beforeCreatedAt;

        var response = await _rpcClient.DispatchFirstAsync(
            DirectChatProtocol.Object,
            DirectChatProtocol.Agents.Sync,
            DirectChatProtocol.GetHistoryActions,
            ctx,
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<IReadOnlyList<ChatMessageItem>>.Failure(response.Error!);

        var conversationId =
            response.Value!.GetString("threadId")
            ?? threadId
            ?? ConversationKeys.BuildDirectDialogId(selfPeerId, targetUserId);

        return Result<IReadOnlyList<ChatMessageItem>>.Success(
            ChatPayloadParser.ParseMessages(
                response.Value!,
                selfPeerId,
                isDirect: true,
                fallbackConversationId: conversationId,
                fallbackTargetId: targetUserId));
    }

    public async Task<Result> AckMessagesAsync(
        string targetUserId,
        string? threadId,
        IReadOnlyList<string> messageIds,
        bool markRead,
        CancellationToken cancellationToken = default)
    {
        targetUserId = Guard.NotNullOrWhiteSpace(targetUserId, nameof(targetUserId));
        messageIds ??= Array.Empty<string>();

        var response = await _rpcClient.DispatchFirstAsync(
            DirectChatProtocol.Object,
            DirectChatProtocol.Agents.Sync,
            DirectChatProtocol.AckMessagesActions,
            new Dictionary<string, object?>
            {
                ["targetUserId"] = targetUserId,
                ["targetPeerId"] = targetUserId,
                ["threadId"] = threadId,
                ["markRead"] = markRead,
                ["messageIds"] = messageIds.Where(static x => !string.IsNullOrWhiteSpace(x)).ToArray()
            },
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        return response.IsSuccess
            ? Result.Success()
            : Result.Failure(response.Error!);
    }
}
