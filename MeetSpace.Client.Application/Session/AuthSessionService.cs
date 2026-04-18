using MeetSpace.Client.App.Abstractions.Auth;
using MeetSpace.Client.Domain.Session;
using MeetSpace.Client.Shared.Abstractions;

namespace MeetSpace.Client.App.Session;

public sealed class AuthSessionService : IAuthSessionService
{
    private readonly SessionStore _store;
    private readonly IClock _clock;

    public AuthSessionService(SessionStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }

    public Task<SessionIdentity> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var state = _store.Current;
        return Task.FromResult(new SessionIdentity(
            state.UserId,
            state.SelfPeerId,
            state.DeviceId,
            state.ConnectionState,
            state.ConnectedAtUtc));
    }

    public Task SetIdentityAsync(string? userId, string? selfPeerId, CancellationToken cancellationToken = default)
    {
        _store.SetBindContext(userId, selfPeerId, null);
        return Task.CompletedTask;
    }

    public Task SetSelfPeerAsync(string selfPeerId, CancellationToken cancellationToken = default)
    {
        _store.SetSelfPeerId(selfPeerId);
        return Task.CompletedTask;
    }

    public Task SetTrustedPeerAsync(string selfPeerId, CancellationToken cancellationToken = default)
    {
        _store.SetSelfPeerId(selfPeerId);
        return Task.CompletedTask;
    }

    public Task SetDeviceIdAsync(string? deviceId, CancellationToken cancellationToken = default)
    {
        _store.SetDeviceId(deviceId);
        return Task.CompletedTask;
    }

    public Task SetConnectionStateAsync(ConnectionState state, CancellationToken cancellationToken = default)
    {
        _store.SetConnectionState(state, state == ConnectionState.Connected ? _clock.UtcNow : null);
        return Task.CompletedTask;
    }

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        _store.Reset();
        return Task.CompletedTask;
    }
}