using System.Text.Json;
using MeetSpace.Client.Contracts.Conference;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Domain.Conference;
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
        if (!string.Equals(envelope.Type, ProtocolMessageTypes.DispatchResult, StringComparison.Ordinal))
            return;

        if (!string.Equals(envelope.Object, ConferenceProtocol.Object, StringComparison.Ordinal))
            return;

        if (envelope.Ok != true || string.IsNullOrWhiteSpace(envelope.Message))
            return;

        if (!string.Equals(envelope.Action, ConferenceProtocol.Actions.ListMembers, StringComparison.Ordinal) &&
            !string.Equals(envelope.Action, ConferenceProtocol.Actions.GetConference, StringComparison.Ordinal))
            return;

        try
        {
            using var doc = JsonDocument.Parse(envelope.Message);
            var root = doc.RootElement;

            var conferenceId = root.TryGetProperty("conferenceId", out var idProp)
                ? idProp.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(conferenceId))
                return;

            var revision = root.TryGetProperty("revision", out var revisionProp) &&
                           revisionProp.TryGetUInt64(out var parsedRevision)
                ? parsedRevision
                : 0UL;

            var isClosed = root.TryGetProperty("isClosed", out var closedProp) &&
                           closedProp.ValueKind == JsonValueKind.True;

            var ownerPeerId = root.TryGetProperty("ownerPeerId", out var ownerProp)
                ? (ownerProp.GetString() ?? string.Empty)
                : string.Empty;

            var members = new List<ConferenceMember>();
            if (root.TryGetProperty("members", out var membersProp) &&
                membersProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in membersProp.EnumerateArray())
                {
                    var peerId = item.TryGetProperty("peerId", out var peerProp)
                        ? peerProp.GetString()
                        : null;

                    if (string.IsNullOrWhiteSpace(peerId))
                        continue;

                    var sessionId = item.TryGetProperty("sessionId", out var sessionProp)
                        ? (sessionProp.GetString() ?? peerId)
                        : peerId;

                    var isOwner = item.TryGetProperty("isOwner", out var ownerFlagProp) &&
                                  ownerFlagProp.ValueKind == JsonValueKind.True;

                    if (isOwner && string.IsNullOrWhiteSpace(ownerPeerId))
                        ownerPeerId = peerId;

                    members.Add(new ConferenceMember(peerId, sessionId, isOwner));
                }
            }

            _store.Update(state => state with
            {
                ActiveConferenceId = conferenceId,
                ActiveConference = new ConferenceDetails(
                    conferenceId,
                    ownerPeerId,
                    isClosed,
                    revision,
                    members)
            });
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _gateway.EnvelopeReceived -= OnEnvelopeReceived;
    }
}