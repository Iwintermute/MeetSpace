using MeetSpace.Client.Domain.Session;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Application.Session;

public sealed record SessionState(
    string? UserId,
    string? TrustedPeer,
    ConnectionState ConnectionState,
    DateTimeOffset? ConnectedAtUtc)
{
    public static SessionState Empty { get; } = new(null, null, ConnectionState.Disconnected, null);
}