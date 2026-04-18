using CommunityToolkit.Mvvm.ComponentModel;
using MeetSpace.Client.App.Auth;
using MeetSpace.Client.App.Chat;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Domain.Chat;
using MeetSpace.Client.Shared.Configuration;
using MeetSpace.Client.Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace MeetSpace.ViewModels.Temporary;

public sealed class ChatPageViewModel : ObservableObject
{
    private readonly ChatCoordinator _chatCoordinator;
    private readonly ChatStore _chatStore;
    private readonly SessionStore _sessionStore;
    private readonly AuthSessionStore _authStore;
    private readonly RealtimeStartupService _realtimeStartupService;
    private readonly ClientRuntimeOptions _options;

    private readonly Dictionary<string, ChatDialogItem> _dialogMap = new(StringComparer.Ordinal);

    private CoreDispatcher? _dispatcher;
    private bool _isActive;
    private string? _searchQuery;

    private ChatDialogItem? _selectedDialog;
    private string _activeDialogTitle = "Выберите чат";
    private string _activeDialogSubtitle = "Слева выберите диалог";
    private string _activeDialogAvatarText = "?";
    private Visibility _emptyStateVisibility = Visibility.Visible;
    private Visibility _messagesVisibility = Visibility.Collapsed;
    private Visibility _composerVisibility = Visibility.Collapsed;
    private string _connectionText = string.Empty;
    private string _peerText = string.Empty;
    private string _errorText = string.Empty;

    public ChatPageViewModel(
        ChatCoordinator chatCoordinator,
        ChatStore chatStore,
        SessionStore sessionStore,
        AuthSessionStore authStore,
        RealtimeStartupService realtimeStartupService,
        ClientRuntimeOptions options)
    {
        _chatCoordinator = chatCoordinator;
        _chatStore = chatStore;
        _sessionStore = sessionStore;
        _authStore = authStore;
        _realtimeStartupService = realtimeStartupService;
        _options = options;
    }

    public ObservableCollection<ChatDialogItem> Dialogs { get; } = new();
    public ObservableCollection<ChatDialogItem> FilteredDialogs { get; } = new();
    public ObservableCollection<ChatMessageItem> Messages { get; } = new();

    public ChatDialogItem? SelectedDialog
    {
        get => _selectedDialog;
        private set
        {
            if (SetProperty(ref _selectedDialog, value))
                OnPropertyChanged(nameof(SelectedPeerId));
        }
    }

    public string? SelectedPeerId => SelectedDialog?.PeerId;

    public string ActiveDialogTitle
    {
        get => _activeDialogTitle;
        private set => SetProperty(ref _activeDialogTitle, value);
    }

    public string ActiveDialogSubtitle
    {
        get => _activeDialogSubtitle;
        private set => SetProperty(ref _activeDialogSubtitle, value);
    }

    public string ActiveDialogAvatarText
    {
        get => _activeDialogAvatarText;
        private set => SetProperty(ref _activeDialogAvatarText, value);
    }

    public Visibility EmptyStateVisibility
    {
        get => _emptyStateVisibility;
        private set => SetProperty(ref _emptyStateVisibility, value);
    }

    public Visibility MessagesVisibility
    {
        get => _messagesVisibility;
        private set => SetProperty(ref _messagesVisibility, value);
    }

    public Visibility ComposerVisibility
    {
        get => _composerVisibility;
        private set => SetProperty(ref _composerVisibility, value);
    }

    public string ConnectionText
    {
        get => _connectionText;
        private set => SetProperty(ref _connectionText, value);
    }

    public string PeerText
    {
        get => _peerText;
        private set => SetProperty(ref _peerText, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set => SetProperty(ref _errorText, value);
    }

    public event EventHandler? NavigateToLoginRequested;

    public async Task ActivateAsync(CoreDispatcher dispatcher)
    {
        if (_isActive)
            return;

        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _isActive = true;

        _chatStore.StateChanged += ChatStore_StateChanged;
        _sessionStore.StateChanged += SessionStore_StateChanged;
        _authStore.StateChanged += AuthStore_StateChanged;

        var authorized = await EnsureAuthorizedAsync().ConfigureAwait(false);
        if (!authorized)
        {
            await RunOnUiThreadAsync(RaiseNavigateToLogin).ConfigureAwait(false);
            return;
        }

        await RunOnUiThreadAsync(() =>
        {
            ApplyAuthState(_authStore.Current);
            ApplySessionState(_sessionStore.Current);
            ApplyChatState(_chatStore.Current);
        }).ConfigureAwait(false);

        var syncResult = await _chatCoordinator.SyncDirectDialogsAsync().ConfigureAwait(false);
        if (syncResult.IsFailure)
        {
            await RunOnUiThreadAsync(() =>
            {
                ApplyError(syncResult.Error?.Message);
            }).ConfigureAwait(false);
        }

        await RunOnUiThreadAsync(() =>
        {
            ApplyChatState(_chatStore.Current);
        }).ConfigureAwait(false);

        if (SelectedDialog == null && FilteredDialogs.Count > 0)
            await OpenDirectDialogAsync(FilteredDialogs[0].PeerId).ConfigureAwait(false);
    }

    public void Deactivate()
    {
        if (!_isActive)
            return;

        _isActive = false;

        _chatStore.StateChanged -= ChatStore_StateChanged;
        _sessionStore.StateChanged -= SessionStore_StateChanged;
        _authStore.StateChanged -= AuthStore_StateChanged;

        _dispatcher = null;
    }

    public async Task SelectDialogAsync(ChatDialogItem? selected)
    {
        if (selected == null)
            return;

        await RunOnUiThreadAsync(() =>
        {
            SetSelectedDialog(selected);
            RebuildMessages(_chatStore.Current);
            UpdateSelectedDialogPresentation();
        }).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(selected.PeerId))
            await OpenDirectDialogAsync(selected.PeerId).ConfigureAwait(false);
    }

    public async Task OpenDirectDialogAsync(string? peerId)
    {
        if (string.IsNullOrWhiteSpace(peerId))
            return;

        await RunOnUiThreadAsync(() =>
        {
            var provisional = CreateProvisionalDialog(peerId);
            _dialogMap[provisional.ConversationId] = provisional;

            SetSelectedDialog(provisional);
            RebuildDialogCollections();
            UpdateSelectedDialogPresentation();
            RebuildMessages(_chatStore.Current);
        }).ConfigureAwait(false);

        var result = await _chatCoordinator.LoadDirectConversationAsync(peerId).ConfigureAwait(false);
        if (result.IsFailure)
        {
            await RunOnUiThreadAsync(() =>
            {
                ApplyError(result.Error?.Message ?? "Не удалось загрузить диалог.");
            }).ConfigureAwait(false);
        }
    }

    public async Task<bool> SendMessageAsync(string? rawText)
    {
        if (SelectedDialog == null || string.IsNullOrWhiteSpace(SelectedDialog.PeerId))
            return false;

        var text = rawText?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var result = await _chatCoordinator
            .SendDirectMessageAsync(SelectedDialog.PeerId!, text)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            await RunOnUiThreadAsync(() =>
            {
                ApplyError(result.Error?.Message ?? "Send failed.");
            }).ConfigureAwait(false);

            return false;
        }

        return true;
    }

    public void ApplyDialogsFilter(string? query)
    {
        _searchQuery = query;

        var filtered = string.IsNullOrWhiteSpace(query)
            ? Dialogs.OrderByDescending(x => x.LastActivityUtc).ToList()
            : Dialogs.Where(x => MatchesDialog(x, query))
                .OrderByDescending(x => x.LastActivityUtc)
                .ToList();

        FilteredDialogs.Clear();
        foreach (var dialog in filtered)
            FilteredDialogs.Add(dialog);
    }

    public async Task ConnectAsync(string? endpoint)
    {
        try
        {
            var value = string.IsNullOrWhiteSpace(endpoint)
                ? _options.DefaultRealtimeEndpoint
                : endpoint.Trim();

            var result = await _realtimeStartupService.EnsureConnectedAsync(value).ConfigureAwait(false);
            if (result.IsFailure)
            {
                await RunOnUiThreadAsync(() =>
                {
                    ApplyError(result.Error?.Message ?? "Connection failed.");
                }).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() =>
            {
                ApplyError(ex.Message);
            }).ConfigureAwait(false);
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            await _realtimeStartupService.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() =>
            {
                ApplyError(ex.Message);
            }).ConfigureAwait(false);
        }
    }

    private async Task<bool> EnsureAuthorizedAsync()
    {
        var auth = _authStore.Current;
        if (!auth.IsAuthenticated || string.IsNullOrWhiteSpace(auth.UserId))
            return false;

        var result = await _realtimeStartupService
            .EnsureConnectedAsync(_options.DefaultRealtimeEndpoint)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            await RunOnUiThreadAsync(() =>
            {
                ApplyError(result.Error?.Message ?? "Не удалось подключить realtime.");
            }).ConfigureAwait(false);
        }

        return true;
    }

    private async void ChatStore_StateChanged(object sender, ChatViewState state)
    {
        if (!_isActive)
            return;

        await RunOnUiThreadAsync(() =>
        {
            ApplyChatState(state);
        }).ConfigureAwait(false);
    }

    private async void SessionStore_StateChanged(object sender, SessionState state)
    {
        if (!_isActive)
            return;

        await RunOnUiThreadAsync(() =>
        {
            ApplySessionState(state);
        }).ConfigureAwait(false);
    }

    private async void AuthStore_StateChanged(object sender, AuthSessionState state)
    {
        if (!_isActive)
            return;

        await RunOnUiThreadAsync(() =>
        {
            ApplyAuthState(state);
        }).ConfigureAwait(false);
    }

    private void ApplyAuthState(AuthSessionState state)
    {
        if (!state.IsAuthenticated)
        {
            RaiseNavigateToLogin();
            return;
        }

        if (SelectedDialog == null)
        {
            ActiveDialogTitle = "Личные сообщения";
            ActiveDialogSubtitle = state.Email ?? "Авторизованный пользователь";
            ActiveDialogAvatarText = string.IsNullOrWhiteSpace(state.Email)
                ? "?"
                : state.Email.Substring(0, 1).ToUpperInvariant();
        }
    }

    private void ApplySessionState(SessionState state)
    {
        ConnectionText = "Connection: " + state.ConnectionState;
        PeerText = string.IsNullOrWhiteSpace(state.SelfPeerId)
            ? string.Empty
            : "Peer: " + state.SelfPeerId;
    }

    private void ApplyChatState(ChatViewState state)
    {
        var selectedConversationId = SelectedDialog?.ConversationId;
        var selectedPeerId = SelectedDialog?.PeerId;

        var directDialogs = state.Dialogs
            .Where(x => x.Kind == ChatDialogKind.Direct)
            .Select(CloneDialog)
            .ToList();

        _dialogMap.Clear();
        foreach (var dialog in directDialogs)
            _dialogMap[dialog.ConversationId] = dialog;

        if (SelectedDialog != null)
        {
            ChatDialogItem? resolved = null;

            if (!string.IsNullOrWhiteSpace(selectedConversationId) &&
                _dialogMap.TryGetValue(selectedConversationId!, out var exact))
            {
                resolved = exact;
            }

            if (resolved == null && !string.IsNullOrWhiteSpace(selectedPeerId))
            {
                resolved = _dialogMap.Values.FirstOrDefault(x =>
                    string.Equals(x.PeerId, selectedPeerId, StringComparison.Ordinal));
            }

            if (resolved != null)
            {
                SetSelectedDialog(resolved);
            }
            else if (!string.IsNullOrWhiteSpace(selectedPeerId))
            {
                var provisional = CreateProvisionalDialog(selectedPeerId!);
                _dialogMap[provisional.ConversationId] = provisional;
                SetSelectedDialog(provisional);
            }
            else
            {
                SetSelectedDialog(null);
            }
        }

        RebuildDialogCollections();
        RebuildMessages(state);
        UpdateSelectedDialogPresentation();
    }

    private void RebuildDialogCollections()
    {
        var ordered = _dialogMap.Values
            .OrderByDescending(x => x.LastActivityUtc)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Dialogs.Clear();
        foreach (var dialog in ordered)
            Dialogs.Add(dialog);

        ApplyDialogsFilter(_searchQuery);
    }

    private void RebuildMessages(ChatViewState state)
    {
        Messages.Clear();

        if (SelectedDialog == null)
            return;

        var selectedConversationId = SelectedDialog.ConversationId;
        var selectedPeerId = SelectedDialog.PeerId;

        var items = state.Messages
            .Where(x =>
                x.IsDirect &&
                (
                    string.Equals(x.ConversationId, selectedConversationId, StringComparison.Ordinal) ||
                    (!string.IsNullOrWhiteSpace(selectedPeerId) &&
                     (
                        string.Equals(x.TargetId, selectedPeerId, StringComparison.Ordinal) ||
                        string.Equals(x.SenderPeerId, selectedPeerId, StringComparison.Ordinal)
                     ))
                ))
            .OrderBy(x => x.SentAtUtc)
            .ThenBy(x => x.LocalId, StringComparer.Ordinal)
            .ToList();

        foreach (var item in items)
            Messages.Add(item);
    }

    private void UpdateSelectedDialogPresentation()
    {
        if (SelectedDialog == null)
        {
            ActiveDialogTitle = "Выберите чат";
            ActiveDialogSubtitle = "Слева выберите диалог";
            ActiveDialogAvatarText = "?";

            EmptyStateVisibility = Visibility.Visible;
            MessagesVisibility = Visibility.Collapsed;
            ComposerVisibility = Visibility.Collapsed;
            return;
        }

        ActiveDialogTitle = string.IsNullOrWhiteSpace(SelectedDialog.Title)
            ? "Без названия"
            : SelectedDialog.Title;

        ActiveDialogSubtitle = string.IsNullOrWhiteSpace(SelectedDialog.Subtitle)
            ? "Личный чат"
            : SelectedDialog.Subtitle;

        ActiveDialogAvatarText = SelectedDialog.AvatarText;

        EmptyStateVisibility = Visibility.Collapsed;
        MessagesVisibility = Visibility.Visible;
        ComposerVisibility = Visibility.Visible;
    }

    private void SetSelectedDialog(ChatDialogItem? dialog)
    {
        SelectedDialog = dialog;
    }

    private ChatDialogItem CreateProvisionalDialog(string peerId)
    {
        var selfPeerId = _sessionStore.Current.SelfPeerId;
        var conversationId = !string.IsNullOrWhiteSpace(selfPeerId)
            ? ConversationKeys.BuildDirectDialogId(selfPeerId!, peerId)
            : "dm:" + peerId;

        return new ChatDialogItem
        {
            ConversationId = conversationId,
            Kind = ChatDialogKind.Direct,
            PeerId = peerId,
            Title = peerId,
            Subtitle = "Личный чат",
            LastMessagePreview = string.Empty,
            LastActivityUtc = DateTimeOffset.MinValue,
            UnreadCount = 0,
            IsPinned = false
        };
    }

    private void ApplyError(string? error)
    {
        ErrorText = error ?? string.Empty;
    }

    private void RaiseNavigateToLogin()
    {
        NavigateToLoginRequested?.Invoke(this, EventArgs.Empty);
    }

    private Task RunOnUiThreadAsync(Action action)
    {
        var dispatcher = _dispatcher;
        if (dispatcher == null)
            return Task.CompletedTask;

        if (dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<bool>();

        _ = dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            try
            {
                action();
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    private static bool ContainsIgnoreCase(string? source, string? query)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(query))
            return false;

        return source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool MatchesDialog(ChatDialogItem dialog, string query)
    {
        return ContainsIgnoreCase(dialog.Title, query) ||
               ContainsIgnoreCase(dialog.Subtitle, query) ||
               ContainsIgnoreCase(dialog.PeerId, query) ||
               ContainsIgnoreCase(dialog.ConversationId, query) ||
               ContainsIgnoreCase(dialog.LastMessagePreview, query);
    }

    private static ChatDialogItem CloneDialog(ChatDialogItem source)
    {
        return new ChatDialogItem
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
}
