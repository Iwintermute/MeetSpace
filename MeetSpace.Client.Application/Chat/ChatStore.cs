using MeetSpace.Client.Domain.Chat;
using MeetSpace.Client.Shared.Stores;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MeetSpace.Client.App.Chat
{
    public sealed class ChatStore : StoreBase<ChatViewState>
    {
        public ChatStore() : base(ChatViewState.Empty)
        {
        }

        public void SetActiveConference(string conversationId)
        {
            Update(state =>
            {
                var dialogs = state.Dialogs
                    .Select(CloneDialog)
                    .ToList();

                var selected = dialogs.FirstOrDefault(x => x.ConversationId == conversationId);
                if (selected != null)
                    selected.UnreadCount = 0;

                return new ChatViewState(
                    state.IsBusy,
                    conversationId,
                    dialogs,
                    state.Messages,
                    null);
            });
        }

        public void UpsertMessage(ChatMessageItem message)
        {
            if (message == null)
                return;

            Update(state =>
            {
                var messages = state.Messages
                    .Select(CloneMessage)
                    .ToList();

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

                var dialogs = state.Dialogs
                    .Select(CloneDialog)
                    .ToList();

                var isDirect = IsDirectMessage(message);
                var activeConversationId = state.ActiveConversationId;

                // В ChatPage попадают только direct 1-to-1 сообщения.
                // Сообщения конференции остаются только в общем message store,
                // чтобы ConferenceRoomPage могла их показать отдельно.
                if (isDirect)
                {
                    var dialogId = message.ConferenceId;
                    var dialog = dialogs.FirstOrDefault(x => x.ConversationId == dialogId);

                    var title = ResolveDialogTitle(message);
                    var subtitle = ResolveDialogSubtitle(message);
                    var peerId = ResolveDialogPeerId(message);

                    if (dialog == null)
                    {
                        dialog = new ChatDialogItem
                        {
                            ConversationId = dialogId,
                            PeerId = peerId,
                            Title = title,
                            Subtitle = subtitle,
                            LastMessagePreview = message.Text,
                            LastActivityUtc = message.SentAtUtc,
                            UnreadCount = (!message.IsOwn && state.ActiveConversationId != dialogId) ? 1 : 0,
                            IsPinned = false
                        };

                        dialogs.Add(dialog);
                    }
                    else
                    {
                        dialog.PeerId = peerId;
                        dialog.Title = title;
                        dialog.Subtitle = subtitle;
                        dialog.LastMessagePreview = message.Text;
                        dialog.LastActivityUtc = message.SentAtUtc;

                        if (!message.IsOwn && state.ActiveConversationId != dialogId)
                            dialog.UnreadCount++;
                    }

                    dialogs = dialogs
                        .OrderByDescending(x => x.IsPinned)
                        .ThenByDescending(x => x.LastActivityUtc)
                        .ToList();

                    if (string.IsNullOrWhiteSpace(activeConversationId))
                        activeConversationId = dialogId;
                }

                return new ChatViewState(
                    state.IsBusy,
                    activeConversationId,
                    dialogs,
                    messages,
                    null);
            });
        }

        public void UpsertDialog(ChatDialogItem dialogItem)
        {
            if (dialogItem == null || string.IsNullOrWhiteSpace(dialogItem.ConversationId))
                return;

            Update(state =>
            {
                var dialogs = state.Dialogs
                    .Select(CloneDialog)
                    .ToList();

                var existing = dialogs.FirstOrDefault(x => x.ConversationId == dialogItem.ConversationId);
                if (existing == null)
                {
                    dialogs.Add(CloneDialog(dialogItem));
                }
                else
                {
                    existing.PeerId = dialogItem.PeerId;
                    existing.Title = dialogItem.Title;
                    existing.Subtitle = dialogItem.Subtitle;
                    existing.LastMessagePreview = dialogItem.LastMessagePreview;
                    existing.LastActivityUtc = dialogItem.LastActivityUtc;
                    existing.UnreadCount = dialogItem.UnreadCount;
                    existing.IsPinned = dialogItem.IsPinned;
                }

                dialogs = dialogs
                    .OrderByDescending(x => x.IsPinned)
                    .ThenByDescending(x => x.LastActivityUtc)
                    .ToList();

                return new ChatViewState(
                    state.IsBusy,
                    state.ActiveConversationId,
                    dialogs,
                    state.Messages,
                    state.LastError);
            });
        }

        public void MarkLocalDelivered(string clientRequestId)
        {
            if (string.IsNullOrWhiteSpace(clientRequestId))
                return;

            Update(state =>
            {
                var messages = state.Messages
                    .Select(x =>
                    {
                        if (x.ClientRequestId == clientRequestId)
                        {
                            return new ChatMessageItem(
                                x.LocalId,
                                x.MessageId,
                                x.ConferenceId,
                                x.SenderPeerId,
                                x.Text,
                                x.SentAtUtc,
                                x.IsOwn,
                                ChatDeliveryState.Sent,
                                x.ClientRequestId,
                                x.TargetPeerId);
                        }

                        return new ChatMessageItem(
                            x.LocalId,
                            x.MessageId,
                            x.ConferenceId,
                            x.SenderPeerId,
                            x.Text,
                            x.SentAtUtc,
                            x.IsOwn,
                            x.Status,
                            x.ClientRequestId,
                            x.TargetPeerId);
                    })
                    .ToList();

                return new ChatViewState(
                    state.IsBusy,
                    state.ActiveConversationId,
                    state.Dialogs,
                    messages,
                    state.LastError);
            });
        }

        public void MarkLocalFailed(string clientRequestId, string error)
        {
            Update(state =>
            {
                var messages = state.Messages
                    .Select(x =>
                    {
                        if (x.ClientRequestId == clientRequestId)
                        {
                            return new ChatMessageItem(
                                x.LocalId,
                                x.MessageId,
                                x.ConferenceId,
                                x.SenderPeerId,
                                x.Text,
                                x.SentAtUtc,
                                x.IsOwn,
                                ChatDeliveryState.Failed,
                                x.ClientRequestId,
                                x.TargetPeerId);
                        }

                        return new ChatMessageItem(
                            x.LocalId,
                            x.MessageId,
                            x.ConferenceId,
                            x.SenderPeerId,
                            x.Text,
                            x.SentAtUtc,
                            x.IsOwn,
                            x.Status,
                            x.ClientRequestId,
                            x.TargetPeerId);
                    })
                    .ToList();

                return new ChatViewState(
                    state.IsBusy,
                    state.ActiveConversationId,
                    state.Dialogs,
                    messages,
                    error);
            });
        }

        public void SetLastError(string error)
        {
            Update(state =>
            {
                return new ChatViewState(
                    state.IsBusy,
                    state.ActiveConversationId,
                    state.Dialogs,
                    state.Messages,
                    error);
            });
        }

        private static ChatMessageItem CloneMessage(ChatMessageItem source)
        {
            return new ChatMessageItem(
                source.LocalId,
                source.MessageId,
                source.ConferenceId,
                source.SenderPeerId,
                source.Text,
                source.SentAtUtc,
                source.IsOwn,
                source.Status,
                source.ClientRequestId,
                source.TargetPeerId);
        }

        private static ChatDialogItem CloneDialog(ChatDialogItem source)
        {
            return new ChatDialogItem
            {
                ConversationId = source.ConversationId,
                PeerId = source.PeerId,
                Title = source.Title,
                Subtitle = source.Subtitle,
                LastMessagePreview = source.LastMessagePreview,
                LastActivityUtc = source.LastActivityUtc,
                UnreadCount = source.UnreadCount,
                IsPinned = source.IsPinned
            };
        }

        private static bool IsDirectMessage(ChatMessageItem message)
        {
            return !string.IsNullOrWhiteSpace(message.TargetPeerId);
        }

        private static string ResolveDialogPeerId(ChatMessageItem message)
        {
            if (message.IsOwn && !string.IsNullOrWhiteSpace(message.TargetPeerId))
                return message.TargetPeerId;

            return message.SenderPeerId;
        }

        private static string ResolveDialogTitle(ChatMessageItem message)
        {
            var peerId = ResolveDialogPeerId(message);

            if (!string.IsNullOrWhiteSpace(peerId))
                return peerId;

            return message.ConferenceId;
        }

        public void ResetConference(string conferenceId)
        {
            Update(state =>
            {
                // Убираем только групповой чат конференции.
                // Direct-сообщения в ChatPage не трогаем.
                var messages = state.Messages
                    .Where(x =>
                        !(string.Equals(x.ConferenceId, conferenceId, StringComparison.Ordinal) &&
                          !IsDirectMessage(x)))
                    .ToList();

                return new ChatViewState(
                    false,
                    state.ActiveConversationId,
                    state.Dialogs,
                    messages,
                    null);
            });
        }

        private static string ResolveDialogSubtitle(ChatMessageItem message)
        {
            return "Личный чат";
        }
    }
}