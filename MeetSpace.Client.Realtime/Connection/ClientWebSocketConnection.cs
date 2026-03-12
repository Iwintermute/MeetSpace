using System.Net.WebSockets;
using System.Text;
using MeetSpace.Client.Realtime.Abstractions;

namespace MeetSpace.Client.Realtime.Connection;

public sealed class ClientWebSocketConnection : IRealtimeConnection, IDisposable
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoopTask;
    private bool _disposed;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<string>? MessageReceived;

    public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (IsConnected)
            return;

        _socket?.Dispose();
        _socket = new ClientWebSocket();

        await _socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);

        _receiveCts = new CancellationTokenSource();
        _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), CancellationToken.None);

        Connected?.Invoke(this, EventArgs.Empty);
    }
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_socket is null)
            return;

        try
        {
            _receiveCts?.Cancel();

            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
        }
        finally
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }
    public async Task SendAsync(string payload, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_socket is null || _socket.State != WebSocketState.Open)
            throw new InvalidOperationException("Realtime connection is not established.");

        var bytes = Encoding.UTF8.GetBytes(payload);
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        if (_socket is null)
            return;

        var buffer = new byte[8192];

        try
        {
            while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Disconnected?.Invoke(this, EventArgs.Empty);
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                var payload = Encoding.UTF8.GetString(ms.ToArray());
                MessageReceived?.Invoke(this, payload);
            }
        }
        catch
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ClientWebSocketConnection));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _receiveCts?.Cancel();
        _socket?.Dispose();
        _receiveCts?.Dispose();
        _sendLock.Dispose();
    }
}