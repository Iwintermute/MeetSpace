using MeetSpace.Client.Contracts.Conference;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Domain.Conference;
using MeetSpace.Client.Realtime.Abstractions;
using System.Linq;

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
        if (string.Equals(envelope.Type, ProtocolMessageTypes.ConferenceMemberJoined, StringComparison.Ordinal) ||
            string.Equals(envelope.Type, ProtocolMessageTypes.ConferenceMemberLeft, StringComparison.Ordinal))
        {
            ApplyMembershipEvent(
                envelope,
                string.Equals(envelope.Type, ProtocolMessageTypes.ConferenceMemberJoined, StringComparison.Ordinal));
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

    private void ApplyMembershipEvent(FeatureResponseEnvelope envelope, bool joined)
    {
        var current = _store.Current.ActiveConference;
        if (current == null)
            return;

        var conferenceId =
            envelope.GetString("conferenceId") ??
            envelope.GetString("conferencePublicId");

        if (string.IsNullOrWhiteSpace(conferenceId) ||
            !string.Equals(conferenceId, current.ConferenceId, StringComparison.Ordinal))
        {
            return;
        }

        var peerId = envelope.GetString("peerId");
        var userId = envelope.GetString("userId");
        var resolvedPeerId = !string.IsNullOrWhiteSpace(peerId) ? peerId : userId;
        if (string.IsNullOrWhiteSpace(resolvedPeerId))
            return;

        var members = current.Members.ToList();
        var memberIndex = members.FindIndex(member =>
            string.Equals(member.PeerId, resolvedPeerId, StringComparison.Ordinal) ||
            (!string.IsNullOrWhiteSpace(userId) && string.Equals(member.UserId, userId, StringComparison.Ordinal)));

        if (joined)
        {
            if (memberIndex >= 0)
            {
                var existing = members[memberIndex];
                members[memberIndex] = existing with
                {
                    MembershipStatus = "joined",
                    JoinedAtUtc = existing.JoinedAtUtc ?? DateTimeOffset.UtcNow,
                    LeftAtUtc = null,
                    UserId = string.IsNullOrWhiteSpace(existing.UserId) ? userId : existing.UserId
                };
            }
            else
            {
                members.Add(new ConferenceMember(
                    resolvedPeerId,
                    resolvedPeerId,
                    false,
                    userId,
                    string.Empty,
                    "joined",
                    DateTimeOffset.UtcNow));
            }
        }
        else if (memberIndex >= 0)
        {
            var existing = members[memberIndex];
            members[memberIndex] = existing with
            {
                MembershipStatus = "left",
                LeftAtUtc = existing.LeftAtUtc ?? DateTimeOffset.UtcNow
            };
        }

        var activePeerIds = (current.ActivePeerIds ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (joined)
        {
            if (!activePeerIds.Any(id => string.Equals(id, resolvedPeerId, StringComparison.Ordinal)))
                activePeerIds.Add(resolvedPeerId);
        }
        else
        {
            activePeerIds.RemoveAll(id => string.Equals(id, resolvedPeerId, StringComparison.Ordinal));
        }

        var updatedConference = current with
        {
            Members = members,
            ActivePeerIds = activePeerIds
        };

        _store.Update(state => state with
        {
            IsBusy = false,
            LastError = null,
            ActiveConferenceId = updatedConference.ConferenceId,
            ActiveConference = updatedConference
        });
    }

    public void Dispose()
    {
        _gateway.EnvelopeReceived -= OnEnvelopeReceived;
    }
}