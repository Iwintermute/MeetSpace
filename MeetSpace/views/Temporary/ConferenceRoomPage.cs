namespace MeetSpace.Views.Temporary
{
    public sealed class ConferenceChatMessageViewItem
    {
        public string SenderDisplayName { get; }
        public string Text { get; }
        public string DisplayTime { get; }
        public bool IsOwn { get; }

        public ConferenceChatMessageViewItem(
            string senderDisplayName,
            string text,
            string displayTime,
            bool isOwn)
        {
            SenderDisplayName = senderDisplayName;
            Text = text;
            DisplayTime = displayTime;
            IsOwn = isOwn;
        }
    }
}