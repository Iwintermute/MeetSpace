using MeetSpace.Client.Domain.Session;
using System;

namespace MeetSpace.Client.App.Session;

public sealed record SessionState(
    string? UserId,
    string? TrustedPeer,
    ConnectionState ConnectionState,
    DateTimeOffset? ConnectedAtUtc)
{
    public static SessionState Empty { get; } = new(null, null, ConnectionState.Disconnected, null);
}