using MeetSpace.Client.Contracts.Conference;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Realtime.Abstractions;

namespace MeetSpace.Client.App.Conference;

public sealed class ConferenceInboundRouter : IDisposable
{
    private readonly IRealtimeGateway _gateway;
    private readonly ConferenceStore _store;

    public ConferenceInboundRouter(IRealtimeGateway gateway, ConferenceStore store)
    {
        _gateway = gateway;
        _store = store;
        _gateway.EnvelopeReceived += OnEnvelopeReceived;
    }

    private void OnEnvelopeReceived(object? sender, FeatureResponseEnvelope envelope)
    {
        if (!string.Equals(envelope.Object, ConferenceProtocol.Object, StringComparison.Ordinal))
            return;

        if (envelope.Ok == false)
        {
            _store.Update(state => state with
            {
                IsBusy = false,
                LastError = envelope.Message
            });
            return;
        }

        if (string.Equals(envelope.Type, ProtocolMessageTypes.DispatchResult, StringComparison.Ordinal) ||
            string.Equals(envelope.Type, ProtocolMessageTypes.ConferenceUpdated, StringComparison.Ordinal) ||
            string.Equals(envelope.Type, ProtocolMessageTypes.ConferenceMembersUpdated, StringComparison.Ordinal))
        {
            var parsed = ConferenceFeatureClient.ParseConferenceDetails(
                envelope,
                _store.Current.ActiveConferenceId ?? string.Empty);

            if (parsed.IsSuccess)
            {
                var details = parsed.Value!;
                _store.Update(state => state with
                {
                    IsBusy = false,
                    LastError = null,
                    ActiveConferenceId = details.ConferenceId,
                    ActiveConference = details
                });
            }
        }
    }

    public void Dispose()
    {
        _gateway.EnvelopeReceived -= OnEnvelopeReceived;
    }
}