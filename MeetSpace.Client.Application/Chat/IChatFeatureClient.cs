namespace MeetSpace.Client.App.Chat;

public interface IChatFeatureClient
{
    Task SendMessageAsync(
        string conferenceId,
        string text,
        string? targetPeerId = null,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default);
}