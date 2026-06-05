using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Realtime.Abstractions;

namespace MeetSpace.Client.Realtime.Gateway;

/// <summary>
/// Protocol gateway that bridges typed envelopes and low-level realtime transport payloads.
/// </summary>
public sealed class RealtimeGateway : IRealtimeGateway, IDisposable
{
    private readonly IRealtimeConnection _connection;
    private readonly ProtocolJsonSerializer _serializer;
    private bool _disposed;

    /// <summary>
    /// Creates gateway and wires transport event forwarding.
    /// </summary>
    /// <param name="connection">Realtime transport abstraction.</param>
    /// <param name="serializer">Serializer used for protocol envelopes.</param>
    public RealtimeGateway(IRealtimeConnection connection, ProtocolJsonSerializer serializer)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));

        _connection.Connected += Connection_Connected;
        _connection.Disconnected += Connection_Disconnected;
        _connection.MessageReceived += Connection_MessageReceived;
    }

    /// <summary>
    /// Gets whether underlying transport connection is currently open.
    /// </summary>
    public bool IsConnected => _connection.IsConnected;

    /// <summary>
    /// Raised when a deserialized protocol response envelope is received.
    /// </summary>
    public event EventHandler<FeatureResponseEnvelope>? EnvelopeReceived;
    /// <summary>
    /// Raised when transport reports successful connection.
    /// </summary>
    public event EventHandler? Connected;
    /// <summary>
    /// Raised when transport reports disconnection.
    /// </summary>
    public event EventHandler? Disconnected;

    /// <summary>
    /// Connects gateway to a realtime endpoint.
    /// </summary>
    /// <param name="endpoint">Absolute websocket endpoint URI.</param>
    /// <param name="cancellationToken">Cancellation token for connect operation.</param>
    /// <returns>A task that completes when transport connection is established.</returns>
    public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _connection.ConnectAsync(endpoint, cancellationToken);
    }

    /// <summary>
    /// Disconnects gateway transport.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for disconnect operation.</param>
    /// <returns>A task that completes when transport disconnect is complete.</returns>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _connection.DisconnectAsync(cancellationToken);
    }

    /// <summary>
    /// Serializes and sends a request envelope over transport.
    /// </summary>
    /// <param name="envelope">Request envelope payload.</param>
    /// <param name="cancellationToken">Cancellation token for send operation.</param>
    /// <returns>A task that completes when payload is sent to transport.</returns>
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

    /// <summary>
    /// Unsubscribes from transport events and marks this gateway as disposed.
    /// </summary>
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