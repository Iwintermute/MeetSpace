using MeetSpace.Client.Domain.Session;
using MeetSpace.Client.Shared.Stores;

namespace MeetSpace.Client.App.Session;

public sealed class SessionStore : StoreBase<SessionState>
{
    public SessionStore() : base(SessionState.Empty)
    {
    }

    public void SetBindContext(string? userId, string? selfPeerId, string? deviceId)
    {
        Update(state => state with
        {
            UserId = string.IsNullOrWhiteSpace(userId) ? state.UserId : userId,
            SelfPeerId = string.IsNullOrWhiteSpace(selfPeerId) ? state.SelfPeerId : selfPeerId,
            DeviceId = string.IsNullOrWhiteSpace(deviceId) ? state.DeviceId : deviceId
        });
    }

    public void SetSelfPeerId(string selfPeerId)
    {
        Update(state => state with { SelfPeerId = selfPeerId });
    }

    public void SetTrustedPeerId(string selfPeerId)
    {
        SetSelfPeerId(selfPeerId);
    }

    public void SetDeviceId(string? deviceId)
    {
        Update(state => state with { DeviceId = deviceId });
    }

    public void Reset()
    {
        Set(SessionState.Empty);
    }

    public void SetConnectionState(ConnectionState state, DateTimeOffset? connectedAtUtc = null)
    {
        Update(current => current with
        {
            ConnectionState = state,
            ConnectedAtUtc = state == ConnectionState.Connected
                ? (connectedAtUtc ?? current.ConnectedAtUtc ?? DateTimeOffset.UtcNow)
                : null
        });
    }
}