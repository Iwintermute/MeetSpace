using MeetSpace.Client.Contracts.Protocol;

namespace MeetSpace.Client.Realtime.Abstractions;
/// <summary>
/// Defines a protocol-aware gateway that maps typed envelopes onto realtime transport.
/// </summary>

public interface IRealtimeGateway
{
    /// <summary>
    /// Gets whether the underlying realtime transport is connected.
    /// </summary>
    bool IsConnected { get; }
    /// <summary>
    /// Raised when a protocol response envelope is received from transport.
    /// </summary>

    event EventHandler<FeatureResponseEnvelope>? EnvelopeReceived;
    /// <summary>
    /// Raised after gateway transport becomes connected.
    /// </summary>
    event EventHandler? Connected;
    /// <summary>
    /// Raised after gateway transport disconnects.
    /// </summary>
    event EventHandler? Disconnected;
    /// <summary>
    /// Connects gateway transport to a realtime endpoint.
    /// </summary>
    /// <param name="endpoint">Absolute websocket endpoint URI.</param>
    /// <param name="cancellationToken">Cancellation token for connection.</param>
    /// <returns>A task that completes when transport is connected.</returns>

    Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default);
    /// <summary>
    /// Disconnects gateway transport.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for disconnect.</param>
    /// <returns>A task that completes when transport is disconnected.</returns>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// Serializes and sends a request envelope to realtime transport.
    /// </summary>
    /// <param name="envelope">Protocol request envelope to send.</param>
    /// <param name="cancellationToken">Cancellation token for send operation.</param>
    /// <returns>A task that completes when envelope payload is sent.</returns>
    Task SendAsync(FeatureRequestEnvelope envelope, CancellationToken cancellationToken = default);
}