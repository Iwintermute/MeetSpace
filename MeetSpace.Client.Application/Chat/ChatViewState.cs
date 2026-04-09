using MeetSpace.Client.Domain.Chat;
using System;
using System.Collections.Generic;

namespace MeetSpace.Client.App.Chat
{
    public sealed class ChatViewState
    {
        public ChatViewState(
            bool isBusy,
            string activeConversationId,
            IReadOnlyList<ChatDialogItem> dialogs,
            IReadOnlyList<ChatMessageItem> messages,
            string lastError)
        {
            IsBusy = isBusy;
            ActiveConversationId = activeConversationId;
            Dialogs = dialogs ?? Array.Empty<ChatDialogItem>();
            Messages = messages ?? Array.Empty<ChatMessageItem>();
            LastError = lastError;
        }

        public bool IsBusy { get; }
        public string ActiveConversationId { get; }
        public IReadOnlyList<ChatDialogItem> Dialogs { get; }
        public IReadOnlyList<ChatMessageItem> Messages { get; }
        public string LastError { get; }

        public static ChatViewState Empty
        {
            get
            {
                return new ChatViewState(
                    false,
                    null,
                    Array.Empty<ChatDialogItem>(),
                    Array.Empty<ChatMessageItem>(),
                    null);
            }
        }

        public ChatViewState With(
            bool? isBusy = null,
            string activeConversationId = null,
            IReadOnlyList<ChatDialogItem> dialogs = null,
            IReadOnlyList<ChatMessageItem> messages = null,
            string lastError = null,
            bool replaceLastError = false)
        {
            return new ChatViewState(
                isBusy ?? IsBusy,
                activeConversationId ?? ActiveConversationId,
                dialogs ?? Dialogs,
                messages ?? Messages,
                replaceLastError ? lastError : (lastError ?? LastError));
        }
    }
}