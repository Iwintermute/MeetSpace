using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MeetSpace.Client.App.Auth;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Domain.Session;
using MeetSpace.Client.Realtime.Abstractions;
using MeetSpace.Client.Realtime.Rpc;
using MeetSpace.Client.Shared.Configuration;
using MeetSpace.Client.Shared.Json;
using MeetSpace.Client.Shared.Results;

namespace MeetSpace.Client.App.Session;

/// <summary>
/// Coordinates realtime connection, auth session binding, and synchronization of connection state.
/// </summary>
public sealed class RealtimeSessionService : IRealtimeSessionService, IDisposable
{
    private static readonly TimeSpan RealtimeConnectTimeout = TimeSpan.FromSeconds(20);
    private readonly IRealtimeGateway _gateway;
    private readonly IRealtimeRpcClient _rpcClient;
    private readonly AuthSessionStore _authStore;
    private readonly SessionStore _sessionStore;
    private readonly ClientRuntimeOptions _options;
    private readonly SemaphoreSlim _sync = new(1, 1);

    private string? _boundAccessToken;
    private bool _disposed;

    /// <summary>
    /// Initializes service dependencies used for transport connectivity, RPC bind flow, and session state updates.
    /// </summary>
    /// <param name="gateway">Realtime gateway used for connect/disconnect operations.</param>
    /// <param name="rpcClient">RPC client used for bind_session dispatch.</param>
    /// <param name="authStore">Authentication store providing current access token.</param>
    /// <param name="sessionStore">Session state store updated by connection lifecycle.</param>
    /// <param name="options">Runtime options including default device id and media auth token.</param>
    public RealtimeSessionService(
        IRealtimeGateway gateway,
        IRealtimeRpcClient rpcClient,
        AuthSessionStore authStore,
        SessionStore sessionStore,
        ClientRuntimeOptions options)
    {
        _gateway = gateway;
        _rpcClient = rpcClient;
        _authStore = authStore;
        _sessionStore = sessionStore;
        _options = options;

        _gateway.Disconnected += Gateway_Disconnected;
    }

    /// <summary>
    /// Ensures realtime transport is connected and binds authenticated session context when needed.
    /// </summary>
    /// <param name="endpoint">Absolute realtime endpoint string.</param>
    /// <param name="cancellationToken">Cancellation token for connect and bind operations.</param>
    /// <returns>Operation result with detailed connectivity or bind failure information.</returns>
    public async Task<Result> EnsureConnectedAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return Result.Failure(new Error("endpoint.invalid", "Endpoint URI is invalid."));
        var connectUri = NormalizeLoopbackIpv4Host(BuildConnectUri(uri));
        var endpointValidation = ValidateRealtimeEndpoint(connectUri);
        if (endpointValidation.IsFailure)
            return endpointValidation;

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_gateway.IsConnected)
            {
                _sessionStore.SetConnectionState(ConnectionState.Connecting);

                try
                {
                    using var connectTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    connectTimeoutCts.CancelAfter(RealtimeConnectTimeout);
                    await _gateway.ConnectAsync(connectUri, connectTimeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _sessionStore.SetConnectionState(ConnectionState.Faulted);
                    return Result.Failure(new Error(
                        "realtime.connect_timeout",
                        $"Timed out connecting to realtime endpoint after {(int)RealtimeConnectTimeout.TotalSeconds}s."));
                }
                catch (Exception ex)
                {
                    _sessionStore.SetConnectionState(ConnectionState.Faulted);
                    var endpointLabel = BuildEndpointLabel(connectUri);
                    var exceptionDetails = $"{ex.GetType().Name}: {ex.Message}";
                    return Result.Failure(new Error(
                        "realtime.connect_failed",
                        $"Failed to connect to realtime endpoint '{endpointLabel}': {exceptionDetails}"));
                }
            }

            var auth = _authStore.Current;
            if (!auth.IsAuthenticated)
            {
                _sessionStore.SetConnectionState(ConnectionState.Connected, DateTimeOffset.UtcNow);
                return Result.Success();
            }

            var accessToken = NormalizeAuthValue(auth.AccessToken);
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                _sessionStore.SetConnectionState(ConnectionState.Faulted);
                return Result.Failure(new Error("auth.invalid_access_token", "Auth access token is invalid."));
            }

            if (string.Equals(_boundAccessToken, accessToken, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(_sessionStore.Current.SelfPeerId))
            {
                return Result.Success();
            }

            var bindResult = await _rpcClient.DispatchAsync(
                "auth",
                "session",
                "bind_session",
                new Dictionary<string, object?>
                {
                    ["accessToken"] = accessToken,
                    ["deviceId"] = _options.DefaultDeviceId
                },
                TimeSpan.FromSeconds(15),
                cancellationToken).ConfigureAwait(false);

            if (bindResult.IsFailure)
            {
                _sessionStore.SetConnectionState(ConnectionState.Faulted);
                return Result.Failure(bindResult.Error!);
            }

            ApplyBindResponse(bindResult.Value!, auth.UserId);
            _boundAccessToken = accessToken;

            return Result.Success();
        }
        finally
        {
            _sync.Release();
        }
    }

    /// <summary>
    /// Raised after an auto-reconnect attempt succeeds following a connection drop.
    /// </summary>
    public event EventHandler? ReconnectedAfterDrop;

    /// <summary>
    /// Disconnects realtime transport and resets session bind context.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for disconnect operation.</param>
    /// <returns>A task that completes after disconnect and state reset.</returns>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _boundAccessToken = null;

        if (_gateway.IsConnected)
            await _gateway.DisconnectAsync(cancellationToken).ConfigureAwait(false);

        _sessionStore.Reset();
    }

    /// <summary>
    /// Attempts to restore connection using bounded retry delays and notifies listeners on success.
    /// </summary>
    /// <param name="endpoint">Realtime endpoint used for retry attempts.</param>
    /// <param name="cancellationToken">Cancellation token controlling the retry loop.</param>
    /// <returns>A task that completes when reconnect succeeds, is cancelled, or retries are exhausted.</returns>
    public async Task TryAutoReconnectAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var delays = new[] { 2000, 4000, 8000 };
        for (var attempt = 0; attempt < delays.Length; attempt++)
        {
            try
            {
                await Task.Delay(delays[attempt], cancellationToken).ConfigureAwait(false);
                var result = await EnsureConnectedAsync(endpoint, cancellationToken).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    ReconnectedAfterDrop?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Extracts bind payload fields and applies resulting identity context to session store.
    /// </summary>
    /// <param name="envelope">Dispatch response envelope from bind_session action.</param>
    /// <param name="fallbackUserId">Fallback user id from auth store when payload omits it.</param>
    private void ApplyBindResponse(FeatureResponseEnvelope envelope, string? fallbackUserId)
    {
        string? userId = fallbackUserId;
        string? selfPeerId = envelope.Peer;
        string? deviceId = _options.DefaultDeviceId;

        if (envelope.TryGetPayload(out var payload) && payload.ValueKind == JsonValueKind.Object)
        {
            userId = payload.GetString("userId", "user_id", "subject", "sub") ?? userId;
            selfPeerId = payload.GetString("peerId", "peer_id", "selfPeerId", "self_peer_id", "peer") ?? selfPeerId;
            deviceId = payload.GetString("deviceId", "device_id") ?? deviceId;
        }

        _sessionStore.SetBindContext(userId, selfPeerId, deviceId);
        _sessionStore.SetConnectionState(ConnectionState.Connected, DateTimeOffset.UtcNow);
    }

    private void Gateway_Disconnected(object? sender, EventArgs e)
    {
        _boundAccessToken = null;
    }

    private static string? NormalizeAuthValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        if (string.Equals(normalized, "null", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "undefined", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalized;
    }

    /// <summary>
    /// Adds media auth token to endpoint query when token exists and query does not already contain it.
    /// </summary>
    /// <param name="endpoint">Base realtime endpoint.</param>
    /// <returns>Endpoint URI with optional <c>auth</c> query parameter.</returns>
    private Uri BuildConnectUri(Uri endpoint)
    {
        var mediaAuthToken = NormalizeAuthValue(_options.MediaAuthToken);
        if (string.IsNullOrWhiteSpace(mediaAuthToken))
            return endpoint;
        if (HasQueryParameter(endpoint.Query, "auth"))
            return endpoint;

        var builder = new UriBuilder(endpoint);
        var existingQuery = builder.Query;
        if (!string.IsNullOrEmpty(existingQuery) && existingQuery.StartsWith("?", StringComparison.Ordinal))
            existingQuery = existingQuery.Substring(1);

        var escapedToken = Uri.EscapeDataString(mediaAuthToken);
        builder.Query = string.IsNullOrWhiteSpace(existingQuery)
            ? $"auth={escapedToken}"
            : $"{existingQuery}&auth={escapedToken}";
        return builder.Uri;
    }

    private static Uri NormalizeLoopbackIpv4Host(Uri endpoint)
    {
        if (endpoint == null || !endpoint.IsAbsoluteUri)
            return endpoint;

        if (!string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase) &&
            !IsIpv6LoopbackHost(endpoint.Host))
        {
            return endpoint;
        }

        var builder = new UriBuilder(endpoint)
        {
            Host = "127.0.0.1"
        };

        return builder.Uri;
    }

    private static string BuildEndpointLabel(Uri endpoint)
    {
        var portPart = endpoint.IsDefaultPort ? string.Empty : $":{endpoint.Port}";
        return $"{endpoint.Scheme}://{endpoint.Host}{portPart}";
    }

    private static bool HasQueryParameter(string query, string key)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(key))
            return false;

        var normalizedQuery = query.StartsWith("?", StringComparison.Ordinal)
            ? query.Substring(1)
            : query;
        var pairs = normalizedQuery.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var separatorIndex = pair.IndexOf('=');
            var currentKey = separatorIndex >= 0 ? pair.Substring(0, separatorIndex) : pair;
            if (string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Validates endpoint policy: remote endpoints must use <c>wss</c>; <c>ws</c> is allowed for loopback only.
    /// </summary>
    /// <param name="endpoint">Endpoint URI to validate.</param>
    /// <returns>Success for valid endpoint policy, otherwise failure with policy error.</returns>
    private static Result ValidateRealtimeEndpoint(Uri endpoint)
    {
        if (endpoint == null || !endpoint.IsAbsoluteUri)
            return Result.Failure(new Error("endpoint.invalid", "Endpoint URI must be absolute."));

        if (IsIpv6Host(endpoint.Host))
        {
            return Result.Failure(new Error(
                "endpoint.ipv6_not_supported",
                "Realtime endpoint must use IPv4 host (IPv6 is disabled)."));
        }
        if (string.Equals(endpoint.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
            return Result.Success();
        if (string.Equals(endpoint.Scheme, "ws", StringComparison.OrdinalIgnoreCase))
        {
            if (IsIpv4LoopbackHost(endpoint.Host))
                return Result.Success();

            return Result.Failure(new Error("endpoint.insecure_ws_remote", "Remote realtime endpoint must use wss://."));
        }
        return Result.Failure(new Error("endpoint.unsupported_scheme", "Realtime endpoint must use wss:// (or ws:// for localhost only)."));
    }

    private static bool IsIpv4LoopbackHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!IPAddress.TryParse(host, out var address))
            return false;

        return address.AddressFamily == AddressFamily.InterNetwork && IPAddress.IsLoopback(address);
    }

    private static bool IsIpv6Host(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        var normalizedHost = host.Trim('[', ']');
        if (!IPAddress.TryParse(normalizedHost, out var address))
            return false;

        return address.AddressFamily == AddressFamily.InterNetworkV6;
    }

    private static bool IsIpv6LoopbackHost(string host)
    {
        if (!IsIpv6Host(host))
            return false;

        var normalizedHost = host.Trim('[', ']');
        if (!IPAddress.TryParse(normalizedHost, out var address))
            return false;

        return IPAddress.IsLoopback(address);
    }

    /// <summary>
    /// Unsubscribes from gateway events and releases synchronization resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _gateway.Disconnected -= Gateway_Disconnected;
        _sync.Dispose();
    }
}