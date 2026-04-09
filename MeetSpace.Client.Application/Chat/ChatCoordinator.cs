using MeetSpace.Client.App.Session;
using MeetSpace.Client.Domain.Chat;
using MeetSpace.Client.Domain.Session;
using MeetSpace.Client.Shared.Abstractions;
using MeetSpace.Client.Shared.Results;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MeetSpace.Client.App.Chat;

public sealed class ChatCoordinator
{
    private readonly IChatFeatureClient _client;
    private readonly ChatStore _store;
    private readonly SessionStore _sessionStore;
    private readonly IClock _clock;

    public ChatCoordinator(
        IChatFeatureClient client,
        ChatStore store,
        SessionStore sessionStore,
        IClock clock)
    {
        _client = client;
        _store = store;
        _sessionStore = sessionStore;
        _clock = clock;
    }

    public async Task<Result> SendMessageAsync(
     string conferenceId,
     string text,
     string? targetPeerId = null,
     CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conferenceId))
            return Result.Failure(new Error("chat.invalid_conference", "Conference ID must not be empty."));

        if (string.IsNullOrWhiteSpace(text))
            return Result.Failure(new Error("chat.invalid_text", "Text must not be empty."));

        var session = _sessionStore.Current;
        if (session.ConnectionState != ConnectionState.Connected)
            return Result.Failure(new Error("chat.not_connected", "Realtime connection is not established."));

        var clientRequestId = Guid.NewGuid().ToString("N");
        var senderPeer = session.TrustedPeer ?? "me";

        var localMessage = new ChatMessageItem(
            localId: clientRequestId,
            messageId: null,
            conferenceId: conferenceId,
            senderPeerId: senderPeer,
            text: text.Trim(),
            sentAtUtc: _clock.UtcNow,
            isOwn: true,
            status: ChatDeliveryState.Pending,
            clientRequestId: clientRequestId,
            targetPeerId: targetPeerId);

        // Для ChatPage активный диалог нужен только у direct 1-to-1.
        if (!string.IsNullOrWhiteSpace(targetPeerId))
            _store.SetActiveConference(conferenceId);

        _store.UpsertMessage(localMessage);

        try
        {
            await _client.SendMessageAsync(
                conferenceId,
                text.Trim(),
                targetPeerId,
                clientRequestId,
                cancellationToken).ConfigureAwait(false);

            _store.MarkLocalDelivered(clientRequestId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _store.MarkLocalFailed(clientRequestId, ex.Message);
            return Result.Failure(new Error("chat.send_failed", ex.Message));
        }
    }
}