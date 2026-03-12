using MeetSpace.Client.App.Session;
using Microsoft.Extensions.Hosting;

namespace MeetSpace.Client.Bootstrap;

public sealed class BootstrapWarmupService : IHostedService
{
    private readonly PeerAssignedRouter _peerAssignedRouter;
    private readonly ConnectionStateRouter _connectionStateRouter;

    public BootstrapWarmupService(
        PeerAssignedRouter peerAssignedRouter,
        ConnectionStateRouter connectionStateRouter)
    {
        _peerAssignedRouter = peerAssignedRouter;
        _connectionStateRouter = connectionStateRouter;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = _peerAssignedRouter;
        _ = _connectionStateRouter;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}