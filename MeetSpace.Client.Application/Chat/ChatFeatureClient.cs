
using MeetSpace.Client.Contracts.Chats;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Realtime.Abstractions;
using MeetSpace.Client.Shared.Utilities;

namespace MeetSpace.Client.App.Chat;

public sealed class ChatFeatureClient : IChatFeatureClient
{
    private readonly IRealtimeGateway _gateway;

    public ChatFeatureClient(IRealtimeGateway gateway)
    {
        _gateway = gateway;
    }

    public Task SendMessageAsync(
        string conferenceId,
        string text,
        string? targetPeerId = null,
        string? clientRequestId = null,
        CancellationToken cancellationToken = default)
    {
        conferenceId = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId));
        text = Guard.NotNullOrWhiteSpace(text, nameof(text));

        var ctx = new Dictionary<string, object?>
        {
            ["conferenceId"] = conferenceId,
            ["text"] = text,
            ["clientRequestId"] = string.IsNullOrWhiteSpace(clientRequestId)
                ? Guid.NewGuid().ToString("N")
                : clientRequestId
        };

        if (!string.IsNullOrWhiteSpace(targetPeerId))
            ctx["targetPeerId"] = targetPeerId;

        var envelope = new FeatureRequestEnvelope
        {
            Object = ChatProtocol.Object,
            Agent = ChatProtocol.Agents.Messaging,
            Action = ChatProtocol.Actions.SendMessage,
            Ctx = ctx
        };

        return _gateway.SendAsync(envelope, cancellationToken);
    }
}