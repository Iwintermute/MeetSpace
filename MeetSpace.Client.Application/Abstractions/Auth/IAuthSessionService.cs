using MeetSpace.Client.Domain.Session;

namespace MeetSpace.Client.App.Abstractions.Auth;

public interface IAuthSessionService
{
    Task<SessionIdentity> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task SetIdentityAsync(string? userId, string? selfPeerId, CancellationToken cancellationToken = default);
    Task SetSelfPeerAsync(string selfPeerId, CancellationToken cancellationToken = default);
    Task SetTrustedPeerAsync(string selfPeerId, CancellationToken cancellationToken = default);
    Task SetDeviceIdAsync(string? deviceId, CancellationToken cancellationToken = default);
    Task SetConnectionStateAsync(ConnectionState state, CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);
}