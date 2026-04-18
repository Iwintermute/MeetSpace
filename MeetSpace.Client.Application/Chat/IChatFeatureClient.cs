using MeetSpace.Client.Shared.Results;

namespace MeetSpace.Client.App.Chat;

public interface IChatFeatureClient
{
    Task<Result<ChatSendAck>> SendMessageAsync(
        string conferenceId,
        string text,
        string? targetPeerId = null,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default);
}