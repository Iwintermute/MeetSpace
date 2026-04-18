namespace MeetSpace.Client.Domain.Session;

public enum ConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Faulted = 4
}

public sealed record SessionIdentity(
    string? UserId,
    string? SelfPeerId,
    string? DeviceId,
    ConnectionState ConnectionState,
    DateTimeOffset? ConnectedAtUtc = null);