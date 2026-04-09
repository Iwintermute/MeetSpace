using MeetSpace.Client.Contracts.Conference;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Realtime.Abstractions;
using MeetSpace.Client.Shared.Utilities;

namespace MeetSpace.Client.App.Conference;

public sealed class ConferenceFeatureClient : IConferenceFeatureClient
{
    private readonly IRealtimeGateway _gateway;

    public ConferenceFeatureClient(IRealtimeGateway gateway)
    {
        _gateway = gateway;
    }

    public Task CreateConferenceAsync(string conferenceId, string? clientRequestId = null, CancellationToken cancellationToken = default)
    {
        var envelope = new FeatureRequestEnvelope
        {
            Object = ConferenceProtocol.Object,
            Agent = ConferenceProtocol.Agents.Lifecycle,
            Action = ConferenceProtocol.Actions.CreateConference,
            Ctx = new Dictionary<string, object?>
            {
                ["conferenceId"] = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId)),
                ["clientRequestId"] = string.IsNullOrWhiteSpace(clientRequestId)
                    ? Guid.NewGuid().ToString("N")
                    : clientRequestId
            }
        };

        return _gateway.SendAsync(envelope, cancellationToken);
    }

    public Task GetConferenceAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        var envelope = new FeatureRequestEnvelope
        {
            Object = ConferenceProtocol.Object,
            Agent = ConferenceProtocol.Agents.Lifecycle,
            Action = ConferenceProtocol.Actions.GetConference,
            Ctx = new Dictionary<string, object?>
            {
                ["conferenceId"] = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId))
            }
        };

        return _gateway.SendAsync(envelope, cancellationToken);
    }

    public Task JoinConferenceAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        var envelope = new FeatureRequestEnvelope
        {
            Object = ConferenceProtocol.Object,
            Agent = ConferenceProtocol.Agents.Membership,
            Action = ConferenceProtocol.Actions.JoinConference,
            Ctx = new Dictionary<string, object?>
            {
                ["conferenceId"] = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId))
            }
        };

        return _gateway.SendAsync(envelope, cancellationToken);
    }

    public Task LeaveConferenceAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        var envelope = new FeatureRequestEnvelope
        {
            Object = ConferenceProtocol.Object,
            Agent = ConferenceProtocol.Agents.Membership,
            Action = ConferenceProtocol.Actions.LeaveConference,
            Ctx = new Dictionary<string, object?>
            {
                ["conferenceId"] = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId))
            }
        };

        return _gateway.SendAsync(envelope, cancellationToken);
    }

    public Task ListMembersAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        var envelope = new FeatureRequestEnvelope
        {
            Object = ConferenceProtocol.Object,
            Agent = ConferenceProtocol.Agents.Membership,
            Action = ConferenceProtocol.Actions.ListMembers,
            Ctx = new Dictionary<string, object?>
            {
                ["conferenceId"] = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId))
            }
        };

        return _gateway.SendAsync(envelope, cancellationToken);
    }
}