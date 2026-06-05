namespace MeetSpace.Client.Realtime.Abstractions;
/// <summary>
/// Defines the low-level realtime transport contract used by higher gateway/RPC layers.
/// </summary>

public interface IRealtimeConnection
{
    /// <summary>
    /// Gets whether the underlying websocket transport is currently open.
    /// </summary>
    bool IsConnected { get; }
    /// <summary>
    /// Raised after a transport connection is established.
    /// </summary>

    event EventHandler? Connected;
    /// <summary>
    /// Raised when the transport is closed or drops.
    /// </summary>
    event EventHandler? Disconnected;
    /// <summary>
    /// Raised when a raw UTF-8 text frame is received from realtime transport.
    /// </summary>
    event EventHandler<string>? MessageReceived;
    /// <summary>
    /// Opens a transport connection to the specified realtime endpoint.
    /// </summary>
    /// <param name="endpoint">Absolute websocket endpoint URI.</param>
    /// <param name="cancellationToken">Cancellation token for the connection operation.</param>
    /// <returns>A task that completes when the connection is established.</returns>

    Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default);
    /// <summary>
    /// Closes the active transport connection.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the disconnect operation.</param>
    /// <returns>A task that completes when disconnect processing is done.</returns>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// Sends a raw UTF-8 text payload through the active connection.
    /// </summary>
    /// <param name="payload">Serialized payload ready for transport.</param>
    /// <param name="cancellationToken">Cancellation token for the send operation.</param>
    /// <returns>A task that completes when the payload is handed off to transport.</returns>
    Task SendAsync(string payload, CancellationToken cancellationToken = default);
}