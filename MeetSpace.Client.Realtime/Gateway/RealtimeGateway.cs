using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Realtime.Abstractions;

namespace MeetSpace.Client.Realtime.Gateway;

public sealed class RealtimeGateway : IRealtimeGateway, IDisposable
{
    private readonly IRealtimeConnection _connection;
    private readonly ProtocolJsonSerializer _serializer;
    private bool _disposed;

    public RealtimeGateway(IRealtimeConnection connection, ProtocolJsonSerializer serializer)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));

        _connection.Connected += Connection_Connected;
        _connection.Disconnected += Connection_Disconnected;
        _connection.MessageReceived += Connection_MessageReceived;
    }

    public bool IsConnected => _connection.IsConnected;

    public event EventHandler<FeatureResponseEnvelope>? EnvelopeReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _connection.ConnectAsync(endpoint, cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _connection.DisconnectAsync(cancellationToken);
    }

    public Task SendAsync(FeatureRequestEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (envelope == null)
            throw new ArgumentNullException(nameof(envelope));

        var json = _serializer.SerializeRequest(envelope);
        return _connection.SendAsync(json, cancellationToken);
    }

    private void Connection_Connected(object? sender, EventArgs e)
    {
        Connected?.Invoke(this, EventArgs.Empty);
    }

    private void Connection_Disconnected(object? sender, EventArgs e)
    {
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void Connection_MessageReceived(object? sender, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return;

        var envelope = _serializer.DeserializeResponse(raw);
        if (envelope == null)
            return;

        EnvelopeReceived?.Invoke(this, envelope);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RealtimeGateway));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _connection.Connected -= Connection_Connected;
        _connection.Disconnected -= Connection_Disconnected;
        _connection.MessageReceived -= Connection_MessageReceived;
    }
}