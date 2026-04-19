using MeetSpace.Client.Domain.Chat;
using MeetSpace.Client.Shared.Results;

namespace MeetSpace.Client.App.Chat;

public interface IDirectChatFeatureClient
{
    Task<Result<ChatSendAck>> SendMessageAsync(
        string targetUserId,
        string text,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ChatDialogItem>>> ListDialogsAsync(
        string selfPeerId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ChatMessageItem>>> GetHistoryAsync(
        string selfPeerId,
        string targetUserId,
        string? threadId = null,
        int limit = 50,
        string? beforeCreatedAt = null,
        CancellationToken cancellationToken = default);

    Task<Result> AckMessagesAsync(
        string targetUserId,
        string? threadId,
        IReadOnlyList<string> messageIds,
        bool markRead,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<DirectUserSearchItem>>> SearchUsersByEmailAsync(
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default);
}