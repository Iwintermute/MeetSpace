using MeetSpace.Client.Contracts.Chats;
using MeetSpace.Client.Domain.Chat;
using MeetSpace.Client.Realtime.Rpc;
using MeetSpace.Client.Shared.Results;
using MeetSpace.Client.Shared.Utilities;

namespace MeetSpace.Client.App.Chat;

public sealed class ConferenceChatFeatureClient : IConferenceChatFeatureClient
{
    private readonly IRealtimeRpcClient _rpcClient;

    public ConferenceChatFeatureClient(IRealtimeRpcClient rpcClient)
    {
        _rpcClient = rpcClient;
    }

    public async Task<Result<ChatSendAck>> SendMessageAsync(
        string conferenceId,
        string text,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default)
    {
        conferenceId = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId));
        text = Guard.NotNullOrWhiteSpace(text, nameof(text));

        var requestId = string.IsNullOrWhiteSpace(clientRequestId)
            ? Guid.NewGuid().ToString("N")
            : clientRequestId;

        var response = await _rpcClient.DispatchFirstAsync(
            ConferenceChatProtocol.Object,
            ConferenceChatProtocol.Agents.Messaging,
            ConferenceChatProtocol.SendMessageActions,
            new Dictionary<string, object?>
            {
                ["conferenceId"] = conferenceId,
                ["text"] = text,
                ["clientRequestId"] = requestId
            },
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<ChatSendAck>.Failure(response.Error!);

        return Result<ChatSendAck>.Success(ChatPayloadParser.ParseAck(response.Value!, requestId));
    }

    public async Task<Result<IReadOnlyList<ChatMessageItem>>> GetHistoryAsync(
        string selfPeerId,
        string conferenceId,
        int limit = 100,
        string? beforeCreatedAt = null,
        CancellationToken cancellationToken = default)
    {
        selfPeerId = Guard.NotNullOrWhiteSpace(selfPeerId, nameof(selfPeerId));
        conferenceId = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId));

        var ctx = new Dictionary<string, object?>
        {
            ["conferenceId"] = conferenceId,
            ["limit"] = limit < 1 ? 100 : limit
        };

        if (!string.IsNullOrWhiteSpace(beforeCreatedAt))
            ctx["beforeCreatedAt"] = beforeCreatedAt;

        var response = await _rpcClient.DispatchFirstAsync(
            ConferenceChatProtocol.Object,
            ConferenceChatProtocol.Agents.Sync,
            ConferenceChatProtocol.GetHistoryActions,
            ctx,
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
            return Result<IReadOnlyList<ChatMessageItem>>.Failure(response.Error!);

        return Result<IReadOnlyList<ChatMessageItem>>.Success(
            ChatPayloadParser.ParseMessages(
                response.Value!,
                selfPeerId,
                isDirect: false,
                fallbackConversationId: conferenceId));
    }

    public async Task<Result> AckMessagesAsync(
        string conferenceId,
        IReadOnlyList<string> messageIds,
        bool markRead,
        CancellationToken cancellationToken = default)
    {
        conferenceId = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId));
        messageIds ??= Array.Empty<string>();

        var response = await _rpcClient.DispatchFirstAsync(
            ConferenceChatProtocol.Object,
            ConferenceChatProtocol.Agents.Sync,
            ConferenceChatProtocol.AckMessagesActions,
            new Dictionary<string, object?>
            {
                ["conferenceId"] = conferenceId,
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
