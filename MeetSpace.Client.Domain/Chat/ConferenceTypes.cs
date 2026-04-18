namespace MeetSpace.Client.Domain.Chat;

public enum ChatDeliveryState
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Received = 3
}

public enum ChatDialogKind
{
    Direct = 0,
    Conference = 1
}

public sealed class ChatDialogItem
{
    public string ConversationId { get; set; } = string.Empty;
    public ChatDialogKind Kind { get; set; }
    public string? PeerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string LastMessagePreview { get; set; } = string.Empty;
    public DateTimeOffset LastActivityUtc { get; set; }
    public int UnreadCount { get; set; }
    public bool IsPinned { get; set; }

    public string AvatarText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Title))
                return "?";

            return Title.Substring(0, 1).ToUpperInvariant();
        }
    }

    public string DisplayTime
    {
        get
        {
            if (LastActivityUtc == default)
                return string.Empty;

            var now = DateTimeOffset.UtcNow.ToLocalTime();
            var local = LastActivityUtc.ToLocalTime();

            if (local.Date == now.Date)
                return local.ToString("HH:mm");

            return local.ToString("dd.MM");
        }
    }
}

public sealed class ChatMessageItem
{
    public string LocalId { get; }
    public string? MessageId { get; }
    public string ConversationId { get; }
    public string SenderPeerId { get; }
    public string Text { get; }
    public DateTimeOffset SentAtUtc { get; }
    public bool IsOwn { get; }
    public ChatDeliveryState Status { get; }
    public string? ClientRequestId { get; }
    public bool IsDirect { get; }
    public string? TargetId { get; }

    public string ConferenceId => ConversationId;
    public string? TargetPeerId => IsDirect ? TargetId : null;

    public ChatMessageItem(
        string localId,
        string? messageId,
        string conversationId,
        string senderPeerId,
        string text,
        DateTimeOffset sentAtUtc,
        bool isOwn,
        ChatDeliveryState status,
        string? clientRequestId = null,
        bool isDirect = false,
        string? targetId = null)
    {
        if (string.IsNullOrWhiteSpace(localId))
            throw new ArgumentException("LocalId must not be empty.", nameof(localId));

        if (string.IsNullOrWhiteSpace(conversationId))
            throw new ArgumentException("ConversationId must not be empty.", nameof(conversationId));

        if (string.IsNullOrWhiteSpace(senderPeerId))
            throw new ArgumentException("SenderPeerId must not be empty.", nameof(senderPeerId));

        if (text == null)
            throw new ArgumentNullException(nameof(text));

        LocalId = localId;
        MessageId = messageId;
        ConversationId = conversationId;
        SenderPeerId = senderPeerId;
        Text = text;
        SentAtUtc = sentAtUtc;
        IsOwn = isOwn;
        Status = status;
        ClientRequestId = clientRequestId;
        IsDirect = isDirect;
        TargetId = targetId;
    }

    public string DisplayStatus
    {
        get
        {
            return Status switch
            {
                ChatDeliveryState.Pending => "Отправляется",
                ChatDeliveryState.Sent => "Отправлено",
                ChatDeliveryState.Failed => "Ошибка",
                ChatDeliveryState.Received => "Получено",
                _ => string.Empty
            };
        }
    }

    public string DisplayTime => SentAtUtc == default ? string.Empty : SentAtUtc.ToLocalTime().ToString("HH:mm");
}