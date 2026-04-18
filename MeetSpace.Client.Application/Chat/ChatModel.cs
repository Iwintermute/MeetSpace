namespace MeetSpace.Client.App.Chat;

public sealed record ChatSendAck(
    string ClientRequestId,
    string? MessageId,
    DateTimeOffset? SentAtUtc,
    string? ConversationId = null);