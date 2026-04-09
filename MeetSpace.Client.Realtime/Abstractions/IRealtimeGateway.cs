using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using MeetSpace.Client.Contracts.Protocol;

namespace MeetSpace.Client.Realtime.Abstractions;

public interface IRealtimeGateway
{
    bool IsConnected { get; }

    event EventHandler<FeatureResponseEnvelope>? EnvelopeReceived;
    event EventHandler? Connected;
    event EventHandler? Disconnected;

    Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task SendAsync(FeatureRequestEnvelope envelope, CancellationToken cancellationToken = default);
}