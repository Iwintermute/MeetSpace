using MeetSpace.Client.Shared.Configuration;
using MeetSpace.Client.Shared.Results;

namespace MeetSpace.Client.App.Session;

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

    public Task<Result> EnsureConnectedAsync(string? endpoint = null, CancellationToken cancellationToken = default)
    {
        return _sessionService.EnsureConnectedAsync(endpoint ?? _defaultEndpoint, cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return _sessionService.DisconnectAsync(cancellationToken);
    }
}