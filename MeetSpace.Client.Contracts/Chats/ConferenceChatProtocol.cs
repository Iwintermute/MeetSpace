#nullable enable

namespace MeetSpace.Client.Contracts.Chats;

public static class ConferenceChatProtocol
{
    public const string Object = "chat";

    public static class Agents
    {
        public const string Messaging = "messaging";
        public const string Sync = "messaging";
    }
    public static class Actions
    {
        public const string SendMessage = "send_message";
        public const string SyncMessages = "sync_messages";
        public const string AckMessages = "ack_messages";
    }

    public static readonly string[] SendMessageActions =
    {
        Actions.SendMessage,
        "create_message",
        "post_message"
    };

    public static readonly string[] GetHistoryActions =
    {
        Actions.SyncMessages,
        "sync_messages",
        "get_history",
        "list_messages",
        "sync_history"
    };

    public static readonly string[] AckMessagesActions =
    {
        Actions.AckMessages,
        "ack_messages"
    };
}