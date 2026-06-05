using MeetSpace.Client.Shared.Configuration;
using MeetSpace.Client.Shared.Results;

namespace MeetSpace.Client.App.Session;

/// <summary>
/// Provides startup-level entry points for realtime connection lifecycle.
/// </summary>
public sealed class RealtimeStartupService
{
    private readonly IRealtimeSessionService _sessionService;
    private readonly string _defaultEndpoint;

    public RealtimeStartupService(
        IRealtimeSessionService sessionService,
        ClientRuntimeOptions options)
    {
        _sessionService = sessionService;
        _defaultEndpoint = options.DefaultRealtimeEndpoint;
    }

    /// <summary>
    /// Ensures realtime session connectivity using an override endpoint or default runtime endpoint.
    /// </summary>
    /// <param name="endpoint">Optional endpoint override.</param>
    /// <param name="cancellationToken">Cancellation token for connection workflow.</param>
    /// <returns>Success/failure result of realtime connect + bind flow.</returns>
    public Task<Result> EnsureConnectedAsync(string? endpoint = null, CancellationToken cancellationToken = default)
    {
        return _sessionService.EnsureConnectedAsync(endpoint ?? _defaultEndpoint, cancellationToken);
    }

    /// <summary>
    /// Disconnects realtime session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for disconnect workflow.</param>
    /// <returns>A task that completes when disconnect handling is finished.</returns>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return _sessionService.DisconnectAsync(cancellationToken);
    }
}