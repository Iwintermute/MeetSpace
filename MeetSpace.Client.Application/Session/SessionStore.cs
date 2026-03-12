using MeetSpace.Client.Domain.Session;
using MeetSpace.Client.Shared.Stores;


namespace MeetSpace.Client.App.Session;

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