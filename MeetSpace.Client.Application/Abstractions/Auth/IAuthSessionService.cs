using MeetSpace.Client.Domain.Session;
using System.Data;

namespace MeetSpace.Client.Application.Abstractions.Auth;

public interface IAuthSessionService
{
    Task<SessionIdentity> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task SetTrustedPeerAsync(string trustedPeer, CancellationToken cancellationToken = default);
    Task SetConnectionStateAsync(ConnectionState state, CancellationToken cancellationToken = default);
}