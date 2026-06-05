using MeetSpace.Client.Shared.Results;

namespace MeetSpace.Client.App.Session;
/// <summary>
/// Defines application-level lifecycle operations for realtime session connectivity.
/// </summary>

public interface IRealtimeSessionService
{
    /// <summary>
    /// Ensures realtime transport is connected and authenticated bind is completed when needed.
    /// </summary>
    /// <param name="endpoint">Realtime endpoint URI string.</param>
    /// <param name="cancellationToken">Cancellation token for connect/bind flow.</param>
    /// <returns>Operation result containing connectivity or bind errors.</returns>
    Task<Result> EnsureConnectedAsync(string endpoint, CancellationToken cancellationToken = default);
    /// <summary>
    /// Disconnects realtime session and resets runtime connection state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for disconnect flow.</param>
    /// <returns>A task that completes when disconnect is finished.</returns>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}