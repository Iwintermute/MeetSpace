using MeetSpace.Client.Domain.Session;
using System.Threading;
using System.Threading.Tasks;
namespace MeetSpace.Client.App.Abstractions.Auth;

public interface IAuthSessionService
{
    Task<SessionIdentity> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task SetTrustedPeerAsync(string trustedPeer, CancellationToken cancellationToken = default);
    Task SetConnectionStateAsync(ConnectionState state, CancellationToken cancellationToken = default);
}