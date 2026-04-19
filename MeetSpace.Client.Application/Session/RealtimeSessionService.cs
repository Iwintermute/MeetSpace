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

public sealed class RealtimeSessionService : IRealtimeSessionService, IDisposable
{
    private readonly IRealtimeGateway _gateway;
    private readonly IRealtimeRpcClient _rpcClient;
    private readonly AuthSessionStore _authStore;
    private readonly SessionStore _sessionStore;
    private readonly ClientRuntimeOptions _options;
    private readonly SemaphoreSlim _sync = new(1, 1);

    private string? _boundAccessToken;
    private bool _disposed;

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

    public async Task<Result> EnsureConnectedAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return Result.Failure(new Error("endpoint.invalid", "Endpoint URI is invalid."));

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_gateway.IsConnected)
            {
                _sessionStore.SetConnectionState(ConnectionState.Connecting);

                try
                {
                    await _gateway.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _sessionStore.SetConnectionState(ConnectionState.Faulted);
                    return Result.Failure(new Error("realtime.connect_failed", ex.Message));
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

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _boundAccessToken = null;

        if (_gateway.IsConnected)
            await _gateway.DisconnectAsync(cancellationToken).ConfigureAwait(false);

        _sessionStore.Reset();
    }

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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _gateway.Disconnected -= Gateway_Disconnected;
        _sync.Dispose();
    }
}