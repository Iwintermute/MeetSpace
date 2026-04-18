using MeetSpace.Client.Domain.Chat;

namespace MeetSpace.Client.App.Chat;

public sealed record ChatViewState(
    bool IsBusy,
    string? ActiveConversationId,
    IReadOnlyList<ChatDialogItem> Dialogs,
    IReadOnlyList<ChatMessageItem> Messages,
    string? LastError)
{
    public static ChatViewState Empty { get; } = new(
        false,
        null,
        Array.Empty<ChatDialogItem>(),
        Array.Empty<ChatMessageItem>(),
        null);
}