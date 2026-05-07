using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MeetSpace.Client.Realtime.Abstractions;

namespace MeetSpace.Client.Realtime.Connection;

public sealed class ClientWebSocketConnection : IRealtimeConnection, IDisposable
{
    private static readonly TimeSpan CertificateBootstrapStepTimeout = TimeSpan.FromSeconds(6);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoopTask;

    private bool _disposed;
    private int _disconnectRaised;
    private string? _pinnedCertThumbprint;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<string>? MessageReceived;

    public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (endpoint == null)
            throw new ArgumentNullException(nameof(endpoint));

        if (IsConnected)
            return;

        await DisposeSocketAsync().ConfigureAwait(false);

        _disconnectRaised = 0;
        _socket = new ClientWebSocket();
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

        if (string.Equals(endpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
        {
            _pinnedCertThumbprint = await TryBootstrapAndGetThumbprintAsync(endpoint, cancellationToken).ConfigureAwait(false);
            ApplyPinnedCertValidation();
        }

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
                await _socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Client disconnect",
                    cancellationToken).ConfigureAwait(false);
            }

            if (_receiveLoopTask is not null)
            {
                try
                {
                    await _receiveLoopTask.ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        finally
        {
            RaiseDisconnectedOnce();
            await DisposeSocketAsync().ConfigureAwait(false);
        }
    }

    public async Task SendAsync(string payload, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_socket is null || _socket.State != WebSocketState.Open)
            throw new InvalidOperationException("Realtime connection is not established.");

        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        var bytes = Encoding.UTF8.GetBytes(payload);

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var socket = _socket;
        if (socket is null)
            return;

        var buffer = new byte[16 * 1024];

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cancellationToken).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        RaiseDisconnectedOnce();
                        return;
                    }

                    if (result.Count > 0)
                        ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (ms.Length == 0)
                    continue;

                var payload = Encoding.UTF8.GetString(ms.ToArray());
                MessageReceived?.Invoke(this, payload);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            RaiseDisconnectedOnce();
        }
    }

    private void RaiseDisconnectedOnce()
    {
        if (Interlocked.Exchange(ref _disconnectRaised, 1) == 0)
            Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private Task DisposeSocketAsync()
    {
        if (_receiveCts is not null)
        {
            try
            {
                _receiveCts.Cancel();
            }
            catch
            {
            }
        }

        if (_socket is not null)
        {
            try
            {
                _socket.Dispose();
            }
            catch
            {
            }

            _socket = null;
        }

        if (_receiveCts is not null)
        {
            try
            {
                _receiveCts.Dispose();
            }
            catch
            {
            }

            _receiveCts = null;
        }

        _receiveLoopTask = null;
        return Task.CompletedTask;
    }

    private static async Task<string?> TryBootstrapAndGetThumbprintAsync(
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        if (endpoint == null || !endpoint.IsAbsoluteUri)
            return null;
        if (!string.Equals(endpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.IsNullOrWhiteSpace(endpoint.Host))
            return null;

        var remotePort = endpoint.IsDefaultPort ? 443 : endpoint.Port;
        X509Certificate2 remoteCertificate = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var tcpClient = new TcpClient();
            var connectTask = tcpClient.ConnectAsync(endpoint.Host, remotePort);
            await WaitWithTimeoutAsync(
                connectTask,
                CertificateBootstrapStepTimeout,
                cancellationToken,
                "tcp_connect").ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            using var tlsStream = new SslStream(
                tcpClient.GetStream(),
                false,
                (sender, certificate, chain, errors) =>
                {
                    if (certificate == null)
                        return false;

                    try
                    {
                        remoteCertificate = new X509Certificate2(certificate);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                });

            var authenticateTask = tlsStream.AuthenticateAsClientAsync(endpoint.Host);
            await WaitWithTimeoutAsync(
                authenticateTask,
                CertificateBootstrapStepTimeout,
                cancellationToken,
                "tls_handshake").ConfigureAwait(false);
        }
        catch
        {
            remoteCertificate?.Dispose();
            return null;
        }

        if (remoteCertificate == null)
            return null;

        string thumbprint;
        try
        {
            thumbprint = remoteCertificate.Thumbprint;

            if (IsCertificateAlreadyTrusted(remoteCertificate))
                return thumbprint;

            TryAddCertificateToStore(remoteCertificate, StoreName.Root, StoreLocation.CurrentUser);
            TryAddCertificateToStore(remoteCertificate, StoreName.TrustedPeople, StoreLocation.CurrentUser);
        }
        finally
        {
            remoteCertificate.Dispose();
        }

        return thumbprint;
    }

    private void ApplyPinnedCertValidation()
    {
        if (string.IsNullOrWhiteSpace(_pinnedCertThumbprint))
            return;

        try
        {
            var pinnedThumbprint = _pinnedCertThumbprint;
            ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
            {
                if (sslPolicyErrors == SslPolicyErrors.None)
                    return true;

                if (certificate != null)
                {
                    try
                    {
                        using var wrappedCert = new X509Certificate2(certificate);
                        if (string.Equals(wrappedCert.Thumbprint, pinnedThumbprint, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch
                    {
                    }
                }

                return false;
            };
        }
        catch
        {
        }
    }

    private static bool IsCertificateAlreadyTrusted(X509Certificate2 certificate)
    {
        try
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(certificate);
        }
        catch
        {
            return false;
        }
    }

    private static void TryAddCertificateToStore(
        X509Certificate2 certificate,
        StoreName storeName,
        StoreLocation storeLocation)
    {
        if (certificate == null)
            return;

        try
        {
            using var store = new X509Store(storeName, storeLocation);
            store.Open(OpenFlags.ReadWrite);
            var existingCertificates = store.Certificates.Find(
                X509FindType.FindByThumbprint,
                certificate.Thumbprint,
                false);
            if (existingCertificates == null || existingCertificates.Count == 0)
                store.Add(certificate);
        }
        catch
        {
        }
    }

    private static async Task WaitWithTimeoutAsync(
        Task operationTask,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string operationName)
    {
        var timeoutTask = Task.Delay(timeout, cancellationToken);
        var completedTask = await Task.WhenAny(operationTask, timeoutTask).ConfigureAwait(false);
        if (!ReferenceEquals(completedTask, operationTask))
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"Certificate bootstrap step timed out: {operationName}.");
        }

        await operationTask.ConfigureAwait(false);
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

        try
        {
            _receiveCts?.Cancel();
        }
        catch
        {
        }

        try
        {
            _socket?.Dispose();
        }
        catch
        {
        }

        try
        {
            _receiveCts?.Dispose();
        }
        catch
        {
        }

        _sendLock.Dispose();
    }
}