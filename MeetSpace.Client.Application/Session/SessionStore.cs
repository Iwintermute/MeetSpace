using MeetSpace.Client.Domain.Session;
using MeetSpace.Client.Shared.Stores;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Application.Session;

public sealed class SessionStore : StoreBase<SessionState>
{
    public SessionStore() : base(SessionState.Empty)
    {
    }

    public void SetTrustedPeer(string peer)
    {
        Update(state => state with { TrustedPeer = peer });
    }

    public void SetConnectionState(ConnectionState connectionState, DateTimeOffset? connectedAtUtc = null)
    {
        Update(state => state with
        {
            ConnectionState = connectionState,
            ConnectedAtUtc = connectedAtUtc ?? state.ConnectedAtUtc
        });
    }
}