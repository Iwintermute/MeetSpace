using MeetSpace.Client.App.Abstractions.Auth;
using MeetSpace.Client.Domain.Session;
using MeetSpace.Client.Realtime.Abstractions;

namespace MeetSpace.Client.App.Session;

public sealed class ConnectionStateRouter : IDisposable
{
    private readonly IRealtimeGateway _gateway;
    private readonly IAuthSessionService _sessionService;

    public ConnectionStateRouter(IRealtimeGateway gateway, IAuthSessionService sessionService)
    {
        _gateway = gateway;
        _sessionService = sessionService;

        _gateway.Connected += OnConnected;
        _gateway.Disconnected += OnDisconnected;
    }

    private async void OnConnected(object? sender, EventArgs e)
    {
        await _sessionService.SetConnectionStateAsync(ConnectionState.Connected).ConfigureAwait(false);
    }

    private async void OnDisconnected(object? sender, EventArgs e)
    {
        await _sessionService.SetConnectionStateAsync(ConnectionState.Disconnected).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _gateway.Connected -= OnConnected;
        _gateway.Disconnected -= OnDisconnected;
    }
}