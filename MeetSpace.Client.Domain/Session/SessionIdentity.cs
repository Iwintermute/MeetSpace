using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Domain.Session;

public sealed record SessionIdentity(
    string? UserId,
    string? TrustedPeer,
    ConnectionState ConnectionState,
    DateTimeOffset? ConnectedAtUtc = null);