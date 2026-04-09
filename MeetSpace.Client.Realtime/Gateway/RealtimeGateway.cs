using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Realtime.Abstractions;
using MeetSpace.Client.Realtime.Serialization;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeetSpace.Client.Realtime.Gateway;

public sealed class RealtimeGateway : IRealtimeGateway
{
    private readonly IRealtimeConnection _connection;
    private readonly ProtocolJsonSerializer _serializer;

    public RealtimeGateway(IRealtimeConnection connection, ProtocolJsonSerializer serializer)
    {
        _connection = connection;
        _serializer = serializer;

        _connection.Connected += (_, _) => Connected?.Invoke(this, EventArgs.Empty);
        _connection.Disconnected += (_, _) => Disconnected?.Invoke(this, EventArgs.Empty);
        _connection.MessageReceived += OnMessageReceived;
    }

    public bool IsConnected => _connection.IsConnected;

    public event EventHandler<FeatureResponseEnvelope>? EnvelopeReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default)
        => _connection.ConnectAsync(endpoint, cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => _connection.DisconnectAsync(cancellationToken);

    public Task SendAsync(FeatureRequestEnvelope envelope, CancellationToken cancellationToken = default)
        => _connection.SendAsync(_serializer.SerializeRequest(envelope), cancellationToken);

    private void OnMessageReceived(object? sender, string raw)
    {
        var envelope = _serializer.DeserializeResponse(raw);
        if (envelope is not null)
            EnvelopeReceived?.Invoke(this, envelope);
    }
}