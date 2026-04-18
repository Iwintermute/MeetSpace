using MeetSpace.Client.Shared.Results;

namespace MeetSpace.Client.App.Session;

public interface IRealtimeSessionService
{
    Task<Result> EnsureConnectedAsync(string endpoint, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}