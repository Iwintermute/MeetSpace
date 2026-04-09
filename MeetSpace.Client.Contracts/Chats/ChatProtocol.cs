namespace MeetSpace.Client.Contracts.Chats;

public static class ChatProtocol
{
    public const string Object = "chat";
    public const string ChatMessageType = "chat_message";

    public static class Agents
    {
        public const string Messaging = "messaging";
    }

    public static class Actions
    {
        public const string SendMessage = "send_message";
    }
}