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
    [ThreadStatic] private static Random? t_random;
    private static Random ThreadRandom => t_random ??= new Random(Environment.TickCount ^ Thread.CurrentThread.ManagedThreadId);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveLoopTask;

    private bool _disposed;
    private int _disconnectRaised;
    private string? _pinnedCertThumbprint;
    private Uri? _lastEndpoint;
    private bool _autoReconnectEnabled;
    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectTask;
    private int _reconnectAttempt;

    // Reconnect policy defaults (overridable via SetReconnectPolicy)
    private int _reconnectInitialDelayMs = 1000;
    private int _reconnectMaxDelayMs = 16000;
    private double _reconnectBackoffMultiplier = 2.0;
    private double _reconnectJitterFactor = 0.2;
    private int _reconnectMaxAttempts = 10;

    public bool IsConnected => _socket?.State == WebSocketState.Open;
    public int ReconnectAttempt => _reconnectAttempt;

    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<string>? MessageReceived;
    public event EventHandler<int>? Reconnecting;
    public event EventHandler? ReconnectFailed;

    public void SetReconnectPolicy(int initialDelayMs, int maxDelayMs, double backoffMultiplier, double jitterFactor, int maxAttempts)
    {
        _reconnectInitialDelayMs = Math.Max(100, Math.Min(30000, initialDelayMs));
        _reconnectMaxDelayMs = Math.Max(_reconnectInitialDelayMs, Math.Min(120000, maxDelayMs));
        _reconnectBackoffMultiplier = Math.Max(1.0, Math.Min(5.0, backoffMultiplier));
        _reconnectJitterFactor = Math.Max(0.0, Math.Min(1.0, jitterFactor));
        _reconnectMaxAttempts = Math.Max(0, Math.Min(100, maxAttempts));
    }

    public void EnableAutoReconnect(bool enabled = true)
    {
        _autoReconnectEnabled = enabled;
        if (!enabled)
            StopReconnectLoop();
    }

    public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (endpoint == null)
            throw new ArgumentNullException(nameof(endpoint));

        if (IsConnected)
            return;

        StopReconnectLoop();
        await DisposeSocketAsync().ConfigureAwait(false);

        _lastEndpoint = endpoint;
        _reconnectAttempt = 0;
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
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
            if (_autoReconnectEnabled && !_disposed && _lastEndpoint != null)
                StartReconnectLoop();
        }
    }

    private void StartReconnectLoop()
    {
        if (_reconnectTask != null) return;
        _reconnectCts = new CancellationTokenSource();
        var token = _reconnectCts.Token;
        var endpoint = _lastEndpoint!;
        _reconnectTask = Task.Run(async () =>
        {
            var delayMs = _reconnectInitialDelayMs;
            for (var attempt = 1; attempt <= _reconnectMaxAttempts && !token.IsCancellationRequested; attempt++)
            {
                _reconnectAttempt = attempt;
                Reconnecting?.Invoke(this, attempt);
                var jitter = (int)(delayMs * _reconnectJitterFactor * (ThreadRandom.NextDouble() - 0.5));
                var waitMs = Math.Max(100, delayMs + jitter);
                try { await Task.Delay(waitMs, token).ConfigureAwait(false); } catch { return; }
                try
                {
                    await DisposeSocketAsync().ConfigureAwait(false);
                    _disconnectRaised = 0;
                    _socket = new ClientWebSocket();
                    _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                    if (string.Equals(endpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
                    {
                        _pinnedCertThumbprint = await TryBootstrapAndGetThumbprintAsync(endpoint, token).ConfigureAwait(false);
                        ApplyPinnedCertValidation();
                    }
                    await _socket.ConnectAsync(endpoint, token).ConfigureAwait(false);
                    _receiveCts = new CancellationTokenSource();
                    _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), CancellationToken.None);
                    _reconnectAttempt = 0;
                    Connected?.Invoke(this, EventArgs.Empty);
                    return;
                }
                catch { }
                delayMs = (int)Math.Min(_reconnectMaxDelayMs, delayMs * _reconnectBackoffMultiplier);
            }
            ReconnectFailed?.Invoke(this, EventArgs.Empty);
        }, token);
    }

    private void StopReconnectLoop()
    {
        try { _reconnectCts?.Cancel(); } catch { }
        try { _reconnectCts?.Dispose(); } catch { }
        _reconnectCts = null;
        _reconnectTask = null;
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