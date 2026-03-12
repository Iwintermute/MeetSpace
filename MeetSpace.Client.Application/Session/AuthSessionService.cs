using MeetSpace.Client.Application.Abstractions.Auth;
using MeetSpace.Client.Domain.Session;
using MeetSpace.Client.Shared.Abstractions;
using System.Data;

namespace MeetSpace.Client.Application.Session;

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
            state.TrustedPeer,
            state.ConnectionState,
            state.ConnectedAtUtc));
    }

    public Task SetTrustedPeerAsync(string trustedPeer, CancellationToken cancellationToken = default)
    {
        _store.SetTrustedPeer(trustedPeer);
        return Task.CompletedTask;
    }

    public Task SetConnectionStateAsync(ConnectionState state, CancellationToken cancellationToken = default)
    {
        _store.SetConnectionState(state, state == ConnectionState.Connected ? _clock.UtcNow : null);
        return Task.CompletedTask;
    }
}