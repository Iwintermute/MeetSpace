using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MeetSpace.Client.Realtime.Abstractions;

public interface IRealtimeConnection
{
    bool IsConnected { get; }

    event EventHandler? Connected;
    event EventHandler? Disconnected;
    event EventHandler<string>? MessageReceived;

    Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task SendAsync(string payload, CancellationToken cancellationToken = default);
}