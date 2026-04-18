using System.Text.Json;
using MeetSpace.Client.App.Abstractions.Auth;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Domain.Session;
using MeetSpace.Client.Realtime.Abstractions;
using MeetSpace.Client.Shared.Json;

namespace MeetSpace.Client.App.Session;

public sealed class SessionInboundRouter : IDisposable
{
    private readonly IRealtimeGateway _gateway;
    private readonly IAuthSessionService _sessionService;

    public SessionInboundRouter(IRealtimeGateway gateway, IAuthSessionService sessionService)
    {
        _gateway = gateway;
        _sessionService = sessionService;

        _gateway.Connected += OnConnected;
        _gateway.Disconnected += OnDisconnected;
        _gateway.EnvelopeReceived += OnEnvelopeReceived;
    }

    private async void OnConnected(object? sender, EventArgs e)
    {
        await _sessionService.SetConnectionStateAsync(ConnectionState.Connected).ConfigureAwait(false);
    }

    private async void OnDisconnected(object? sender, EventArgs e)
    {
        await _sessionService.SetConnectionStateAsync(ConnectionState.Disconnected).ConfigureAwait(false);
    }

    private async void OnEnvelopeReceived(object? sender, FeatureResponseEnvelope envelope)
    {
        if (string.Equals(envelope.Type, ProtocolMessageTypes.PeerAssigned, StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(envelope.Peer))
                await _sessionService.SetSelfPeerAsync(envelope.Peer!).ConfigureAwait(false);

            return;
        }

        if (string.Equals(envelope.Type, ProtocolMessageTypes.DispatchResult, StringComparison.Ordinal) &&
            string.Equals(envelope.Object, "auth", StringComparison.Ordinal) &&
            string.Equals(envelope.Action, "bind_session", StringComparison.Ordinal) &&
            envelope.Ok == true)
        {
            string? userId = null;
            string? selfPeerId = envelope.Peer;
            string? deviceId = null;

            if (envelope.TryGetPayload(out var payload) && payload.ValueKind == JsonValueKind.Object)
            {
                userId = payload.GetString("userId", "user_id", "subject", "sub");
                selfPeerId ??= payload.GetString("peerId", "peer_id", "selfPeerId", "self_peer_id", "peer");
                deviceId = payload.GetString("deviceId", "device_id");
            }

            await _sessionService.SetIdentityAsync(userId, selfPeerId).ConfigureAwait(false);
            await _sessionService.SetDeviceIdAsync(deviceId).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _gateway.Connected -= OnConnected;
        _gateway.Disconnected -= OnDisconnected;
        _gateway.EnvelopeReceived -= OnEnvelopeReceived;
    }
}