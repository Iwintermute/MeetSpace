using MeetSpace.Client.App.Auth;
using MeetSpace.Client.App.Chat;
using MeetSpace.Client.App.Conference;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Bootstrap;
using MeetSpace.Client.Domain.Chat;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using System.Globalization;

namespace MeetSpace.Views.Temporary
{
    /// <summary>
    /// Пустая страница, которую можно использовать саму по себе или для перехода внутри фрейма.
    /// </summary>
    public sealed partial class ChatPage : Page
    {
        private readonly ConferenceCoordinator _conferenceCoordinator;
        private readonly ChatCoordinator _chatCoordinator;
        private readonly ChatStore _chatStore;
        private readonly ConferenceStore _conferenceStore;
        private readonly SessionStore _sessionStore;
        private readonly AuthSessionStore _authStore;
        private readonly RealtimeStartupService _realtimeStartupService;
        private readonly RealtimeAuthBinder _realtimeAuthBinder;
        private ChatDialogItem _selectedDialog;

        public ObservableCollection<ChatDialogItem> Dialogs { get; } =
       new ObservableCollection<ChatDialogItem>();

        public ObservableCollection<ChatDialogItem> FilteredDialogs { get; } =
            new ObservableCollection<ChatDialogItem>();

        public ObservableCollection<ChatMessageItem> Messages { get; } =
            new ObservableCollection<ChatMessageItem>();


        public ChatPage()
        {
            this.InitializeComponent();

            var services = App.Current.Services;

            _conferenceCoordinator = services.GetRequiredService<ConferenceCoordinator>();
            _chatCoordinator = services.GetRequiredService<ChatCoordinator>();
            _chatStore = services.GetRequiredService<ChatStore>();
            _conferenceStore = services.GetRequiredService<ConferenceStore>();
            _sessionStore = services.GetRequiredService<SessionStore>();

            _authStore = services.GetRequiredService<AuthSessionStore>();
            _realtimeStartupService = services.GetRequiredService<RealtimeStartupService>();
            _realtimeAuthBinder = services.GetRequiredService<RealtimeAuthBinder>();

            Loaded += ChatPage_Loaded;
            Unloaded += ChatPage_Unloaded;
        }
        private static bool ContainsIgnoreCase(string source, string query)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(query))
                return false;

            // Use CompareInfo.IndexOf to avoid overload resolution issues with ReadOnlySpan<char>
            return CultureInfo.InvariantCulture.CompareInfo.IndexOf(source, query, CompareOptions.IgnoreCase) >= 0;
        }

        private static bool MatchesDialog(ChatDialogItem dialog, string query)
        {
            if (dialog == null)
                return false;

            if (string.IsNullOrWhiteSpace(query))
                return true;

            return
                ContainsIgnoreCase(dialog.Title, query) ||
                ContainsIgnoreCase(dialog.Subtitle, query) ||
                ContainsIgnoreCase(dialog.PeerId, query) ||
                ContainsIgnoreCase(dialog.ConversationId, query) ||
                ContainsIgnoreCase(dialog.LastMessagePreview, query);
        }

        private void ApplyDialogsFilter(string query)
        {
            var filtered = string.IsNullOrWhiteSpace(query)
                ? Dialogs.OrderByDescending(x => x.LastActivityUtc).ToList()
                : Dialogs
                    .Where(x => MatchesDialog(x, query))
                    .OrderByDescending(x => x.LastActivityUtc)
                    .ToList();

            FilteredDialogs.Clear();
            foreach (var dialog in filtered)
                FilteredDialogs.Add(dialog);

            if (_selectedDialog != null)
            {
                var selected = FilteredDialogs.FirstOrDefault(x => x.ConversationId == _selectedDialog.ConversationId);
                if (selected != null)
                    DialogsList.SelectedItem = selected;
            }
        }

        private IReadOnlyList<ChatDialogItem> BuildSuggestions(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Dialogs
                    .OrderByDescending(x => x.LastActivityUtc)
                    .Take(8)
                    .ToList();

            return Dialogs
                .Where(x => MatchesDialog(x, query))
                .OrderByDescending(x => x.LastActivityUtc)
                .Take(8)
                .ToList();
        }

        private void SelectDialog(ChatDialogItem dialog)
        {
            if (dialog == null)
                return;

            var actualDialog = Dialogs.FirstOrDefault(x => x.ConversationId == dialog.ConversationId) ?? dialog;

            _selectedDialog = actualDialog;
            _chatStore.SetActiveConference(actualDialog.ConversationId);

            ApplySelectedDialog();
            ApplyMessages(_chatStore.Current);

            var inFiltered = FilteredDialogs.FirstOrDefault(x => x.ConversationId == actualDialog.ConversationId);
            if (inFiltered != null)
                DialogsList.SelectedItem = inFiltered;
        }
        private async void ChatPage_Loaded(object sender, RoutedEventArgs e)
        {
            _chatStore.StateChanged += ChatStore_StateChanged;
            _conferenceStore.StateChanged += ConferenceStore_StateChanged;
            _sessionStore.StateChanged += SessionStore_StateChanged;
            _authStore.StateChanged += AuthStore_StateChanged;

            var ok = await EnsureAuthorizedAsync();
            if (!ok)
                return;

            ApplyChatState(_chatStore.Current);
            ApplyConferenceState(_conferenceStore.Current);
            ApplySessionState(_sessionStore.Current);
            ApplyAuthState(_authStore.Current);
        }

        private void ChatPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _chatStore.StateChanged -= ChatStore_StateChanged;
            _conferenceStore.StateChanged -= ConferenceStore_StateChanged;
            _sessionStore.StateChanged -= SessionStore_StateChanged;
            _authStore.StateChanged -= AuthStore_StateChanged;
        }

        private async void AuthStore_StateChanged(object sender, AuthSessionState state)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                ApplyAuthState(state);
            });
        }

        private void ApplyAuthState(AuthSessionState state)
        {
            if (!state.IsAuthenticated)
            {
                Frame?.Navigate(typeof(LoginPage));
                return;
            }

            if (_selectedDialog == null)
            {
                ActiveDialogTitleTextBlock.Text = "Личные сообщения";
                ActiveDialogSubtitleTextBlock.Text = state.Email ?? "Авторизованный пользователь";
                ActiveDialogAvatarTextBlock.Text = string.IsNullOrWhiteSpace(state.Email)
                    ? "?"
                    : state.Email.Substring(0, 1).ToUpperInvariant();
            }
        }

        private async Task<bool> EnsureAuthorizedAsync()
        {
            var auth = _authStore.Current;

            if (!auth.IsAuthenticated || string.IsNullOrWhiteSpace(auth.UserId))
            {
                Frame?.Navigate(typeof(LoginPage));
                return false;
            }

            try
            {
                var result = await _realtimeStartupService.EnsureConnectedAsync("ws://127.0.0.1:9002");
                if (result.IsSuccess)
                {
                    await _realtimeAuthBinder.BindAsync();
                }
                else
                {
                    ApplyError(result.Error?.Message ?? "Не удалось подключить realtime.");
                }
            }
            catch (Exception ex)
            {
                ApplyError(ex.Message);
            }

            return true;
        }

        private async void ChatStore_StateChanged(object sender, ChatViewState state)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, delegate
            {
                ApplyChatState(state);
            });
        }

        private async void ConferenceStore_StateChanged(object sender, ConferenceViewState state)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, delegate
            {
                ApplyConferenceState(state);
            });
        }

        private async void SessionStore_StateChanged(object sender, SessionState state)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, delegate
            {
                ApplySessionState(state);
            });
        }

        private void ApplyChatState(ChatViewState state)
        {
            SyncDialogs(state);

            if (_selectedDialog == null && state.Dialogs.Count > 0)
            {
                var activeDialog = state.Dialogs.FirstOrDefault(x => x.ConversationId == state.ActiveConversationId);
                _selectedDialog = activeDialog ?? state.Dialogs[0];
            }
            else if (_selectedDialog != null)
            {
                _selectedDialog = state.Dialogs.FirstOrDefault(x => x.ConversationId == _selectedDialog.ConversationId);
            }

            ApplySelectedDialog();
            ApplyMessages(state);
            ApplyError(state.LastError);
        }

        private void SyncDialogs(ChatViewState state)
        {
            Dialogs.Clear();

            foreach (var dialog in state.Dialogs.OrderByDescending(x => x.LastActivityUtc))
                Dialogs.Add(dialog);

            ApplyDialogsFilter(DialogsSearchBox != null ? DialogsSearchBox.Text : null);
        }

        private void ApplySelectedDialog()
        {
            if (_selectedDialog == null)
            {
                ActiveDialogTitleTextBlock.Text = "Выберите чат";
                ActiveDialogSubtitleTextBlock.Text = "Слева выберите диалог";
                ActiveDialogAvatarTextBlock.Text = "?";

                EmptyStateGrid.Visibility = Visibility.Visible;
                MessagesList.Visibility = Visibility.Collapsed;
                ComposerBorder.Visibility = Visibility.Collapsed;
                return;
            }

            ActiveDialogTitleTextBlock.Text = string.IsNullOrWhiteSpace(_selectedDialog.Title)
                ? "Без названия"
                : _selectedDialog.Title;

            ActiveDialogSubtitleTextBlock.Text = string.IsNullOrWhiteSpace(_selectedDialog.Subtitle)
                ? "Личный чат"
                : _selectedDialog.Subtitle;

            ActiveDialogAvatarTextBlock.Text = _selectedDialog.AvatarText;

            EmptyStateGrid.Visibility = Visibility.Collapsed;
            MessagesList.Visibility = Visibility.Visible;
            ComposerBorder.Visibility = Visibility.Visible;
        }

        private void ApplyMessages(ChatViewState state)
        {
            Messages.Clear();

            if (_selectedDialog == null)
                return;

            var items = state.Messages
                .Where(x =>
                    x.ConferenceId == _selectedDialog.ConversationId &&
                    !string.IsNullOrWhiteSpace(x.TargetPeerId))
                .OrderBy(x => x.SentAtUtc)
                .ToList();

            foreach (var item in items)
                Messages.Add(item);

            if (Messages.Count > 0)
                MessagesList.ScrollIntoView(Messages[Messages.Count - 1]);
        }

        private void ApplyConferenceState(ConferenceViewState state)
        {
            if (!string.IsNullOrWhiteSpace(state.LastError))
                ApplyError(state.LastError);
        }

        private void ApplySessionState(SessionState state)
        {
            ConnectionTextBlock.Text = "Connection: " + state.ConnectionState;
            PeerTextBlock.Text = string.IsNullOrWhiteSpace(state.TrustedPeer)
                ? string.Empty
                : "Peer: " + state.TrustedPeer;
        }

        private void ApplyError(string error)
        {
            ErrorTextBlock.Text = error ?? string.Empty;
        }

        private void DialogsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = DialogsList.SelectedItem as ChatDialogItem;
            if (selected == null)
                return;

            _selectedDialog = selected;
            _chatStore.SetActiveConference(selected.ConversationId);
            ApplySelectedDialog();
            ApplyMessages(_chatStore.Current);
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            var result = await _conferenceCoordinator.ConnectAsync(EndpointTextBox.Text);
            if (result.IsFailure)
                ApplyError(result.Error != null ? result.Error.Message : "Connection failed.");
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            await _conferenceCoordinator.DisconnectAsync();
        }

        private async void CreateConferenceButton_Click(object sender, RoutedEventArgs e)
        {
            var conversationId = string.IsNullOrWhiteSpace(ConferenceIdTextBox.Text)
                ? "dm-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                : ConferenceIdTextBox.Text.Trim();

            var result = await _conferenceCoordinator.CreateConferenceAsync(conversationId);
            if (result.IsFailure)
                ApplyError(result.Error != null ? result.Error.Message : "Create conversation failed.");
        }

        private async void JoinConferenceButton_Click(object sender, RoutedEventArgs e)
        {
            var conversationId = ConferenceIdTextBox.Text != null
                ? ConferenceIdTextBox.Text.Trim()
                : null;

            if (string.IsNullOrWhiteSpace(conversationId))
                return;

            var result = await _conferenceCoordinator.JoinConferenceAsync(conversationId);
            if (result.IsFailure)
                ApplyError(result.Error != null ? result.Error.Message : "Join conversation failed.");
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDialog == null)
                return;

            var text = MessageTextBox.Text != null
                ? MessageTextBox.Text.Trim()
                : null;

            if (string.IsNullOrWhiteSpace(text))
                return;

            var result = await _chatCoordinator.SendMessageAsync(
                _selectedDialog.ConversationId,
                text,
                _selectedDialog.PeerId);

            if (result.IsSuccess)
                MessageTextBox.Text = string.Empty;
            else
                ApplyError(result.Error != null ? result.Error.Message : "Send failed.");
        }
    }
}