namespace MeetSpace.Client.Domain.Chat;

public enum ChatDeliveryState
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Received = 3
}