using MeetSpace.Client.App.Abstractions.Auth;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Realtime.Abstractions;

namespace MeetSpace.Client.App.Session;

public sealed class PeerAssignedRouter : IDisposable
{
    private readonly IRealtimeGateway _gateway;
    private readonly IAuthSessionService _sessionService;

    public PeerAssignedRouter(IRealtimeGateway gateway, IAuthSessionService sessionService)
    {
        _gateway = gateway;
        _sessionService = sessionService;
        _gateway.EnvelopeReceived += OnEnvelopeReceived;
    }

    private async void OnEnvelopeReceived(object? sender, FeatureResponseEnvelope envelope)
    {
        if (!string.Equals(envelope.Type, ProtocolMessageTypes.PeerAssigned, StringComparison.Ordinal))
            return;

        if (!string.IsNullOrWhiteSpace(envelope.Peer))
            await _sessionService.SetTrustedPeerAsync(envelope.Peer!).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _gateway.EnvelopeReceived -= OnEnvelopeReceived;
    }
}