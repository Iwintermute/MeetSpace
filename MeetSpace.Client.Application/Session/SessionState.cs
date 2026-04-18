using MeetSpace.Client.Domain.Session;

namespace MeetSpace.Client.App.Session;

public sealed record SessionState(
    string? UserId,
    string? SelfPeerId,
    string? DeviceId,
    ConnectionState ConnectionState,
    DateTimeOffset? ConnectedAtUtc)
{
    public string? TrustedPeer => SelfPeerId;

    public static SessionState Empty { get; } = new(
        null,
        null,
        null,
        ConnectionState.Disconnected,
        null);
}