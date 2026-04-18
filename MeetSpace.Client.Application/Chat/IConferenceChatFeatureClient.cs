using MeetSpace.Client.Domain.Chat;
using MeetSpace.Client.Shared.Results;

namespace MeetSpace.Client.App.Chat;

public interface IConferenceChatFeatureClient
{
    Task<Result<ChatSendAck>> SendMessageAsync(
        string conferenceId,
        string text,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ChatMessageItem>>> GetHistoryAsync(
        string selfPeerId,
        string conferenceId,
        int limit = 100,
        string? beforeCreatedAt = null,
        CancellationToken cancellationToken = default);

    Task<Result> AckMessagesAsync(
        string conferenceId,
        IReadOnlyList<string> messageIds,
        bool markRead,
        CancellationToken cancellationToken = default);
}