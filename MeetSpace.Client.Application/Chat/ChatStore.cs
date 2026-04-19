using MeetSpace.Client.Domain.Chat;


namespace MeetSpace.Client.App.Chat;

public sealed class ChatStore : StoreBase<ChatViewState>
{
    public ChatStore() : base(ChatViewState.Empty)
    {
    }

    public void SetBusy(bool isBusy)
    {
        Update(state => state with { IsBusy = isBusy });
    }

    public void SetActiveConversation(string conversationId)
    {
        Update(state =>
        {
            var dialogs = state.Dialogs.Select(CloneDialog).ToList();
            var selected = dialogs.FirstOrDefault(x => x.ConversationId == conversationId);
            if (selected != null)
                selected.UnreadCount = 0;

            return state with
            {
                ActiveConversationId = conversationId,
                Dialogs = dialogs,
                LastError = null
            };
        });
    }

    public void SetActiveConference(string conversationId)
    {
        SetActiveConversation(conversationId);
    }

    public void ReplaceDialogs(IReadOnlyList<ChatDialogItem> dialogs)
    {
        dialogs ??= Array.Empty<ChatDialogItem>();

        Update(state =>
        {
            var cloned = dialogs.Select(CloneDialog).ToList();

            return state with
            {
                Dialogs = cloned
                    .OrderByDescending(x => x.IsPinned)
                    .ThenByDescending(x => x.LastActivityUtc)
                    .ToList(),
                ActiveConversationId = string.IsNullOrWhiteSpace(state.ActiveConversationId) && cloned.Count > 0
                    ? cloned[0].ConversationId
                    : state.ActiveConversationId,
                LastError = null
            };
        });
    }

    public void ReplaceConversationMessages(string conversationId, IReadOnlyList<ChatMessageItem> messages)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return;

        messages ??= Array.Empty<ChatMessageItem>();

        Update(state =>
        {
            var rest = state.Messages
                .Where(x => !string.Equals(x.ConversationId, conversationId, StringComparison.Ordinal))
                .Select(CloneMessage)
                .ToList();

            rest.AddRange(messages.Select(CloneMessage));

            return state with
            {
                Messages = rest
                    .OrderBy(x => x.SentAtUtc)
                    .ThenBy(x => x.LocalId, StringComparer.Ordinal)
                    .ToList(),
                LastError = null
            };
        });
    }

    public void RebindConversation(string oldConversationId, string newConversationId)
    {
        if (string.IsNullOrWhiteSpace(oldConversationId) ||
            string.IsNullOrWhiteSpace(newConversationId) ||
            string.Equals(oldConversationId, newConversationId, StringComparison.Ordinal))
        {
            return;
        }

        Update(state =>
        {
            var messages = state.Messages
                .Select(CloneMessage)
                .ToList();

            for (var i = 0; i < messages.Count; i++)
            {
                var current = messages[i];
                if (!string.Equals(current.ConversationId, oldConversationId, StringComparison.Ordinal))
                    continue;

                messages[i] = new ChatMessageItem(
                    current.LocalId,
                    current.MessageId,
                    newConversationId,
                    current.SenderPeerId,
                    current.Text,
                    current.SentAtUtc,
                    current.IsOwn,
                    current.Status,
                    current.ClientRequestId,
                    current.IsDirect,
                    current.TargetId);
            }

            var dialogs = state.Dialogs
                .Select(CloneDialog)
                .ToList();

            var oldDialog = dialogs.FirstOrDefault(x => x.ConversationId == oldConversationId);
            var newDialog = dialogs.FirstOrDefault(x => x.ConversationId == newConversationId);

            if (oldDialog != null && newDialog == null)
            {
                oldDialog.ConversationId = newConversationId;
            }
            else if (oldDialog != null && newDialog != null)
            {
                newDialog.LastActivityUtc = oldDialog.LastActivityUtc > newDialog.LastActivityUtc
                    ? oldDialog.LastActivityUtc
                    : newDialog.LastActivityUtc;

                if (!string.IsNullOrWhiteSpace(oldDialog.LastMessagePreview))
                    newDialog.LastMessagePreview = oldDialog.LastMessagePreview;

                newDialog.UnreadCount += oldDialog.UnreadCount;
                newDialog.IsPinned = newDialog.IsPinned || oldDialog.IsPinned;

                dialogs.Remove(oldDialog);
            }

            var activeConversationId = string.Equals(state.ActiveConversationId, oldConversationId, StringComparison.Ordinal)
                ? newConversationId
                : state.ActiveConversationId;

            return state with
            {
                ActiveConversationId = activeConversationId,
                Dialogs = dialogs
                    .OrderByDescending(x => x.IsPinned)
                    .ThenByDescending(x => x.LastActivityUtc)
                    .ToList(),
                Messages = messages
                    .OrderBy(x => x.SentAtUtc)
                    .ThenBy(x => x.LocalId, StringComparer.Ordinal)
                    .ToList()
            };
        });
    }

    public void UpsertMessage(ChatMessageItem message)
    {
        if (message == null)
            return;

        Update(state =>
        {
            var dialogs = state.Dialogs.Select(CloneDialog).ToList();
            var messages = state.Messages.Select(CloneMessage).ToList();

            if (message.IsDirect && !string.IsNullOrWhiteSpace(message.TargetId))
            {
                var existingDialogByPeer = dialogs.FirstOrDefault(x =>
                    x.Kind == ChatDialogKind.Direct &&
                    string.Equals(x.PeerId, message.TargetId, StringComparison.Ordinal) &&
                    !string.Equals(x.ConversationId, message.ConversationId, StringComparison.Ordinal));

                if (existingDialogByPeer != null)
                {
                    var oldConversationId = existingDialogByPeer.ConversationId;
                    var newConversationId = message.ConversationId;

                    for (var i = 0; i < messages.Count; i++)
                    {
                        var current = messages[i];
                        if (!string.Equals(current.ConversationId, oldConversationId, StringComparison.Ordinal))
                            continue;

                        messages[i] = new ChatMessageItem(
                            current.LocalId,
                            current.MessageId,
                            newConversationId,
                            current.SenderPeerId,
                            current.Text,
                            current.SentAtUtc,
                            current.IsOwn,
                            current.Status,
                            current.ClientRequestId,
                            current.IsDirect,
                            current.TargetId);
                    }

                    existingDialogByPeer.ConversationId = newConversationId;
                }
            }

            var existingIndex = messages.FindIndex(x =>
                (!string.IsNullOrWhiteSpace(message.MessageId) && x.MessageId == message.MessageId) ||
                (!string.IsNullOrWhiteSpace(message.ClientRequestId) && x.ClientRequestId == message.ClientRequestId) ||
                x.LocalId == message.LocalId);

            if (existingIndex >= 0)
                messages[existingIndex] = CloneMessage(message);
            else
                messages.Add(CloneMessage(message));

            messages = messages
                .OrderBy(x => x.SentAtUtc)
                .ThenBy(x => x.LocalId, StringComparer.Ordinal)
                .ToList();

            var dialog = dialogs.FirstOrDefault(x => x.ConversationId == message.ConversationId);

            if (message.IsDirect)
            {
                var otherPeerId = message.IsOwn ? message.TargetId : message.SenderPeerId;
                var title = ResolveDirectDialogTitle(otherPeerId, dialog?.Title);
                var subtitle = "Личный чат";

                if (dialog == null)
                {
                    dialog = new ChatDialogItem
                    {
                        ConversationId = message.ConversationId,
                        Kind = ChatDialogKind.Direct,
                        PeerId = otherPeerId,
                        Title = title,
                        Subtitle = subtitle,
                        LastMessagePreview = message.Text,
                        LastActivityUtc = message.SentAtUtc,
                        UnreadCount = (!message.IsOwn &&
                                       !string.Equals(state.ActiveConversationId, message.ConversationId, StringComparison.Ordinal))
                            ? 1
                            : 0,
                        IsPinned = false
                    };

                    dialogs.Add(dialog);
                }
                else
                {
                    dialog.PeerId = otherPeerId;
                    dialog.Title = title;
                    dialog.Subtitle = subtitle;
                    dialog.LastMessagePreview = message.Text;
                    dialog.LastActivityUtc = message.SentAtUtc;

                    if (!message.IsOwn &&
                        !string.Equals(state.ActiveConversationId, message.ConversationId, StringComparison.Ordinal))
                    {
                        dialog.UnreadCount++;
                    }
                }
            }
            else
            {
                if (dialog != null)
                {
                    dialog.LastMessagePreview = message.Text;
                    dialog.LastActivityUtc = message.SentAtUtc;
                }
            }

            return state with
            {
                Dialogs = dialogs
                    .OrderByDescending(x => x.IsPinned)
                    .ThenByDescending(x => x.LastActivityUtc)
                    .ToList(),
                Messages = messages,
                LastError = null
            };
        });
    }

    public void MarkLocalDelivered(
        string clientRequestId,
        string? messageId = null,
        DateTimeOffset? sentAtUtc = null,
        string? conversationId = null)
    {
        if (string.IsNullOrWhiteSpace(clientRequestId))
            return;

        Update(state =>
        {
            var messages = state.Messages.Select(CloneMessage).ToList();
            var dialogs = state.Dialogs.Select(CloneDialog).ToList();

            string? oldConversationId = null;

            for (var i = 0; i < messages.Count; i++)
            {
                var current = messages[i];
                if (current.ClientRequestId != clientRequestId)
                    continue;

                oldConversationId = current.ConversationId;
                var nextConversationId = string.IsNullOrWhiteSpace(conversationId)
                    ? current.ConversationId
                    : conversationId!;

                messages[i] = new ChatMessageItem(
                    current.LocalId,
                    messageId ?? current.MessageId,
                    nextConversationId,
                    current.SenderPeerId,
                    current.Text,
                    sentAtUtc ?? current.SentAtUtc,
                    current.IsOwn,
                    ChatDeliveryState.Sent,
                    current.ClientRequestId,
                    current.IsDirect,
                    current.TargetId);
            }

            if (!string.IsNullOrWhiteSpace(oldConversationId) &&
                !string.IsNullOrWhiteSpace(conversationId) &&
                !string.Equals(oldConversationId, conversationId, StringComparison.Ordinal))
            {
                for (var i = 0; i < messages.Count; i++)
                {
                    var current = messages[i];
                    if (!string.Equals(current.ConversationId, oldConversationId, StringComparison.Ordinal))
                        continue;

                    messages[i] = new ChatMessageItem(
                        current.LocalId,
                        current.MessageId,
                        conversationId!,
                        current.SenderPeerId,
                        current.Text,
                        current.SentAtUtc,
                        current.IsOwn,
                        current.Status,
                        current.ClientRequestId,
                        current.IsDirect,
                        current.TargetId);
                }

                var oldDialog = dialogs.FirstOrDefault(x => x.ConversationId == oldConversationId);
                var newDialog = dialogs.FirstOrDefault(x => x.ConversationId == conversationId);

                if (oldDialog != null && newDialog == null)
                {
                    oldDialog.ConversationId = conversationId!;
                }
                else if (oldDialog != null && newDialog != null)
                {
                    newDialog.LastActivityUtc = oldDialog.LastActivityUtc > newDialog.LastActivityUtc
                        ? oldDialog.LastActivityUtc
                        : newDialog.LastActivityUtc;

                    if (!string.IsNullOrWhiteSpace(oldDialog.LastMessagePreview))
                        newDialog.LastMessagePreview = oldDialog.LastMessagePreview;

                    newDialog.UnreadCount += oldDialog.UnreadCount;
                    newDialog.IsPinned = newDialog.IsPinned || oldDialog.IsPinned;
                    dialogs.Remove(oldDialog);
                }
            }

            var activeConversationId = state.ActiveConversationId;
            if (!string.IsNullOrWhiteSpace(oldConversationId) &&
                !string.IsNullOrWhiteSpace(conversationId) &&
                string.Equals(activeConversationId, oldConversationId, StringComparison.Ordinal))
            {
                activeConversationId = conversationId;
            }

            return state with
            {
                ActiveConversationId = activeConversationId,
                Dialogs = dialogs
                    .OrderByDescending(x => x.IsPinned)
                    .ThenByDescending(x => x.LastActivityUtc)
                    .ToList(),
                Messages = messages
                    .OrderBy(x => x.SentAtUtc)
                    .ThenBy(x => x.LocalId, StringComparer.Ordinal)
                    .ToList(),
                LastError = null
            };
        });
    }

    public void MarkLocalFailed(string clientRequestId, string error)
    {
        if (string.IsNullOrWhiteSpace(clientRequestId))
            return;

        Update(state =>
        {
            var messages = state.Messages
                .Select(x =>
                {
                    if (x.ClientRequestId != clientRequestId)
                        return CloneMessage(x);

                    return new ChatMessageItem(
                        x.LocalId,
                        x.MessageId,
                        x.ConversationId,
                        x.SenderPeerId,
                        x.Text,
                        x.SentAtUtc,
                        x.IsOwn,
                        ChatDeliveryState.Failed,
                        x.ClientRequestId,
                        x.IsDirect,
                        x.TargetId);
                })
                .ToList();

            return state with
            {
                Messages = messages,
                LastError = error
            };
        });
    }

    public void SetLastError(string error)
    {
        Update(state => state with { LastError = error });
    }

    public void ResetConference(string conferenceId)
    {
        Update(state =>
        {
            var messages = state.Messages
                .Where(x => !string.Equals(x.ConversationId, conferenceId, StringComparison.Ordinal))
                .Select(CloneMessage)
                .ToList();

            return state with
            {
                Messages = messages,
                LastError = null
            };
        });
    }

    private static string ResolveDirectDialogTitle(string? peerId, string? existingTitle)
    {
        if (!string.IsNullOrWhiteSpace(existingTitle) && !LooksLikeTechnicalId(existingTitle!))
            return existingTitle!;

        if (string.IsNullOrWhiteSpace(peerId))
            return "Пользователь";

        if (peerId.Contains("@"))
            return peerId;

        if (LooksLikeTechnicalId(peerId))
            return "Пользователь";

        return peerId;
    }

    private static bool LooksLikeTechnicalId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var normalized = value.Trim();
        if (normalized.Contains("@"))
            return false;

        if (normalized.StartsWith("peer_", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("user_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.Length >= 24)
            return true;

        if (normalized.Contains('-') && normalized.Length >= 16)
            return true;

        var digits = normalized.Count(char.IsDigit);
        return digits >= normalized.Length / 2 && normalized.Length >= 10;
    }

    private static ChatMessageItem CloneMessage(ChatMessageItem source)
        => new(
            source.LocalId,
            source.MessageId,
            source.ConversationId,
            source.SenderPeerId,
            source.Text,
            source.SentAtUtc,
            source.IsOwn,
            source.Status,
            source.ClientRequestId,
            source.IsDirect,
            source.TargetId);

    private static ChatDialogItem CloneDialog(ChatDialogItem source)
        => new()
        {
            ConversationId = source.ConversationId,
            Kind = source.Kind,
            PeerId = source.PeerId,
            Title = source.Title,
            Subtitle = source.Subtitle,
            LastMessagePreview = source.LastMessagePreview,
            LastActivityUtc = source.LastActivityUtc,
            UnreadCount = source.UnreadCount,
            IsPinned = source.IsPinned
        };
}