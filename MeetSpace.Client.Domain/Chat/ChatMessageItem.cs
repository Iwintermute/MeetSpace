using System;

namespace MeetSpace.Client.Domain.Chat
{
    public sealed class ChatMessageItem
    {
        public string LocalId { get; }
        public string? MessageId { get; }
        public string ConferenceId { get; }
        public string SenderPeerId { get; }
        public string Text { get; }
        public DateTimeOffset SentAtUtc { get; }
        public bool IsOwn { get; }
        public ChatDeliveryState Status { get; }
        public string? ClientRequestId { get; }
        public string? TargetPeerId { get; }

        public string DisplayStatus
        {
            get
            {
                switch (Status)
                {
                    case ChatDeliveryState.Pending:
                        return "Отправляется";
                    case ChatDeliveryState.Sent:
                        return "Отправлено";
                    case ChatDeliveryState.Failed:
                        return "Ошибка";
                    case ChatDeliveryState.Received:
                        return "Получено";
                    default:
                        return string.Empty;
                }
            }
        }

        public string DisplayTime
        {
            get
            {
                if (SentAtUtc == default(DateTimeOffset))
                    return string.Empty;

                return SentAtUtc.ToLocalTime().ToString("HH:mm");
            }
        }
        public ChatMessageItem(
            string localId,
            string? messageId,
            string conferenceId,
            string senderPeerId,
            string text,
            DateTimeOffset sentAtUtc,
            bool isOwn,
            ChatDeliveryState status,
            string? clientRequestId = null,
            string? targetPeerId = null)
        {
            if (string.IsNullOrWhiteSpace(localId))
                throw new ArgumentException("LocalId must not be empty.", nameof(localId));

            if (string.IsNullOrWhiteSpace(conferenceId))
                throw new ArgumentException("ConferenceId must not be empty.", nameof(conferenceId));

            if (string.IsNullOrWhiteSpace(senderPeerId))
                throw new ArgumentException("SenderPeerId must not be empty.", nameof(senderPeerId));

            if (text == null)
                throw new ArgumentNullException(nameof(text));

            LocalId = localId;
            MessageId = messageId;
            ConferenceId = conferenceId;
            SenderPeerId = senderPeerId;
            Text = text;
            SentAtUtc = sentAtUtc;
            IsOwn = isOwn;
            Status = status;
            ClientRequestId = clientRequestId;
            TargetPeerId = targetPeerId;
        }
    }
}