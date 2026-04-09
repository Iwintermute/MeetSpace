using System;

namespace MeetSpace.Client.Domain.Chat
{
    public sealed class ChatDialogItem
    {
        public string ConversationId { get; set; }
        public string PeerId { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public string LastMessagePreview { get; set; }
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
                if (LastActivityUtc == default(DateTimeOffset))
                    return string.Empty;

                var now = DateTimeOffset.UtcNow;
                var local = LastActivityUtc.ToLocalTime();

                if (local.Date == now.ToLocalTime().Date)
                    return local.ToString("HH:mm");

                return local.ToString("dd.MM");
            }
        }
    }
}