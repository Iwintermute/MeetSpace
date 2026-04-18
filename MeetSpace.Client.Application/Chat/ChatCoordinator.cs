using MeetSpace.Client.App.Session;
using MeetSpace.Client.Domain.Chat;
using MeetSpace.Client.Domain.Session;
using MeetSpace.Client.Shared.Abstractions;
using MeetSpace.Client.Shared.Results;
using MeetSpace.Client.Shared.Utilities;

namespace MeetSpace.Client.App.Chat;

public sealed class ChatCoordinator
{
    private readonly IDirectChatFeatureClient _directChatClient;
    private readonly IConferenceChatFeatureClient _conferenceChatClient;
    private readonly ChatStore _store;
    private readonly SessionStore _sessionStore;
    private readonly IClock _clock;

    public ChatCoordinator(
        IDirectChatFeatureClient directChatClient,
        IConferenceChatFeatureClient conferenceChatClient,
        ChatStore store,
        SessionStore sessionStore,
        IClock clock)
    {
        _directChatClient = directChatClient;
        _conferenceChatClient = conferenceChatClient;
        _store = store;
        _sessionStore = sessionStore;
        _clock = clock;
    }

    public async Task<Result> SyncDirectDialogsAsync(CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.Current;
        if (string.IsNullOrWhiteSpace(session.SelfPeerId))
            return Result.Failure(new Error("chat.self_peer_missing", "Self peer is not assigned."));

        _store.SetBusy(true);

        try
        {
            var result = await _directChatClient.ListDialogsAsync(session.SelfPeerId!, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                _store.SetLastError(result.Error!.Message);
                return Result.Failure(result.Error!);
            }

            _store.ReplaceDialogs(result.Value!);
            return Result.Success();
        }
        finally
        {
            _store.SetBusy(false);
        }
    }

    public async Task<Result> LoadDirectConversationAsync(
        string peerId,
        string? threadId = null,
        CancellationToken cancellationToken = default)
    {
        peerId = Guard.NotNullOrWhiteSpace(peerId, nameof(peerId));

        var session = _sessionStore.Current;
        if (string.IsNullOrWhiteSpace(session.SelfPeerId))
            return Result.Failure(new Error("chat.self_peer_missing", "Self peer is not assigned."));

        _store.SetBusy(true);

        try
        {
            var result = await _directChatClient.GetHistoryAsync(
                session.SelfPeerId!,
                peerId,
                threadId: threadId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                _store.SetLastError(result.Error!.Message);
                return Result.Failure(result.Error!);
            }

            var loadedMessages = result.Value!;
            var canonicalConversationId = loadedMessages
                .Select(x => x.ConversationId)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                ?? threadId
                ?? ConversationKeys.BuildDirectDialogId(session.SelfPeerId!, peerId);

            var provisionalConversationId = ConversationKeys.BuildDirectDialogId(session.SelfPeerId!, peerId);
            if (!string.Equals(provisionalConversationId, canonicalConversationId, StringComparison.Ordinal))
                _store.RebindConversation(provisionalConversationId, canonicalConversationId);

            _store.ReplaceConversationMessages(canonicalConversationId, loadedMessages);
            _store.SetActiveConversation(canonicalConversationId);

            return Result.Success();
        }
        finally
        {
            _store.SetBusy(false);
        }
    }

    public async Task<Result> LoadConferenceConversationAsync(
        string conferenceId,
        CancellationToken cancellationToken = default)
    {
        conferenceId = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId));

        var session = _sessionStore.Current;
        if (string.IsNullOrWhiteSpace(session.SelfPeerId))
            return Result.Failure(new Error("chat.self_peer_missing", "Self peer is not assigned."));

        _store.SetBusy(true);

        try
        {
            var result = await _conferenceChatClient.GetHistoryAsync(
                session.SelfPeerId!,
                conferenceId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                _store.SetLastError(result.Error!.Message);
                return Result.Failure(result.Error!);
            }

            _store.ReplaceConversationMessages(conferenceId, result.Value!);
            _store.SetActiveConversation(conferenceId);

            return Result.Success();
        }
        finally
        {
            _store.SetBusy(false);
        }
    }

    public async Task<Result> SendDirectMessageAsync(
        string peerId,
        string text,
        CancellationToken cancellationToken = default)
    {
        peerId = Guard.NotNullOrWhiteSpace(peerId, nameof(peerId));
        text = Guard.NotNullOrWhiteSpace(text, nameof(text));

        var session = _sessionStore.Current;
        if (session.ConnectionState != ConnectionState.Connected)
            return Result.Failure(new Error("chat.not_connected", "Realtime connection is not established."));

        if (string.IsNullOrWhiteSpace(session.SelfPeerId))
            return Result.Failure(new Error("chat.self_peer_missing", "Self peer is not assigned."));

        var clientRequestId = Guid.NewGuid().ToString("N");
        var provisionalConversationId = ConversationKeys.BuildDirectDialogId(session.SelfPeerId!, peerId);

        var localMessage = new ChatMessageItem(
            localId: clientRequestId,
            messageId: null,
            conversationId: provisionalConversationId,
            senderPeerId: session.SelfPeerId!,
            text: text.Trim(),
            sentAtUtc: _clock.UtcNow,
            isOwn: true,
            status: ChatDeliveryState.Pending,
            clientRequestId: clientRequestId,
            isDirect: true,
            targetId: peerId);

        _store.UpsertMessage(localMessage);
        _store.SetActiveConversation(provisionalConversationId);

        var result = await _directChatClient.SendMessageAsync(
            peerId,
            text.Trim(),
            clientRequestId,
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            _store.MarkLocalFailed(clientRequestId, result.Error!.Message);
            return Result.Failure(result.Error!);
        }

        _store.MarkLocalDelivered(
            clientRequestId,
            result.Value!.MessageId,
            result.Value.SentAtUtc,
            result.Value.ConversationId);

        if (!string.IsNullOrWhiteSpace(result.Value.ConversationId))
            _store.SetActiveConversation(result.Value.ConversationId!);

        return Result.Success();
    }

    public async Task<Result> SendConferenceMessageAsync(
        string conferenceId,
        string text,
        CancellationToken cancellationToken = default)
    {
        conferenceId = Guard.NotNullOrWhiteSpace(conferenceId, nameof(conferenceId));
        text = Guard.NotNullOrWhiteSpace(text, nameof(text));

        var session = _sessionStore.Current;
        if (session.ConnectionState != ConnectionState.Connected)
            return Result.Failure(new Error("chat.not_connected", "Realtime connection is not established."));

        if (string.IsNullOrWhiteSpace(session.SelfPeerId))
            return Result.Failure(new Error("chat.self_peer_missing", "Self peer is not assigned."));

        var clientRequestId = Guid.NewGuid().ToString("N");

        var localMessage = new ChatMessageItem(
            localId: clientRequestId,
            messageId: null,
            conversationId: conferenceId,
            senderPeerId: session.SelfPeerId!,
            text: text.Trim(),
            sentAtUtc: _clock.UtcNow,
            isOwn: true,
            status: ChatDeliveryState.Pending,
            clientRequestId: clientRequestId,
            isDirect: false,
            targetId: conferenceId);

        _store.UpsertMessage(localMessage);
        _store.SetActiveConversation(conferenceId);

        var result = await _conferenceChatClient.SendMessageAsync(
            conferenceId,
            text.Trim(),
            clientRequestId,
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            _store.MarkLocalFailed(clientRequestId, result.Error!.Message);
            return Result.Failure(result.Error!);
        }

        _store.MarkLocalDelivered(
            clientRequestId,
            result.Value!.MessageId,
            result.Value.SentAtUtc,
            result.Value.ConversationId);

        return Result.Success();
    }

    public Task<Result> SendMessageAsync(
        string conversationId,
        string text,
        string? targetPeerId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(targetPeerId))
            return SendDirectMessageAsync(targetPeerId!, text, cancellationToken);

        return SendConferenceMessageAsync(conversationId, text, cancellationToken);
    }
}