using MeetSpace.Client.Domain.Session;
namespace MeetSpace.Client.App.Abstractions.Auth;

public interface IAuthSessionService
{
    Task<SessionIdentity> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task SetTrustedPeerAsync(string trustedPeer, CancellationToken cancellationToken = default);
    Task SetConnectionStateAsync(ConnectionState state, CancellationToken cancellationToken = default);
}