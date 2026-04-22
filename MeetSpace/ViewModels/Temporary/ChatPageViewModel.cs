using CommunityToolkit.Mvvm.ComponentModel;
using MeetSpace.Client.App.Auth;
using MeetSpace.Client.App.Calls;
using MeetSpace.Client.App.Chat;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Domain.Calls;
using MeetSpace.Client.Domain.Chat;
using MeetSpace.Client.Realtime.Abstractions;
using MeetSpace.Client.Shared.Configuration;
using MeetSpace.Client.Shared.Json;
using MeetSpace.Client.Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace MeetSpace.ViewModels.Temporary;

public sealed class ChatPageViewModel : ObservableObject
{
    private readonly ChatCoordinator _chatCoordinator;
    private readonly IDirectChatFeatureClient _directChatClient;
    private readonly ChatStore _chatStore;
    private readonly SessionStore _sessionStore;
    private readonly AuthSessionStore _authStore;
    private readonly RealtimeStartupService _realtimeStartupService;
    private readonly CallCoordinator _callCoordinator;
    private readonly CallStore _callStore;
    private readonly IRealtimeGateway _realtimeGateway;
    private readonly ClientRuntimeOptions _options;

    private readonly Dictionary<string, ChatDialogItem> _dialogMap = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _knownUserLabels = new(StringComparer.Ordinal);

    private CoreDispatcher? _dispatcher;
    private bool _isActive;
    private string? _dialogsSearchQuery;
    private ChatDialogItem? _selectedDialog;
    private string? _incomingCallId;
    private string? _incomingCallerUserId;
    private string? _incomingCallerDisplayName;
    private string? _incomingCallerEmail;
    private string _activeDialogTitle = "Выберите чат";
    private string _activeDialogSubtitle = "Слева выберите диалог";
    private string _activeDialogAvatarText = "?";
    private Visibility _emptyStateVisibility = Visibility.Visible;
    private Visibility _messagesVisibility = Visibility.Collapsed;
    private Visibility _composerVisibility = Visibility.Collapsed;
    private string _connectionText = string.Empty;
    private string _peerText = string.Empty;
    private string _errorText = string.Empty;
    private string _userSearchStatusText = string.Empty;
    private bool _isSearchingUsers;
    private bool _isStartAudioCallEnabled;
    private bool _isStartVideoCallEnabled;
    private bool _isEndCallEnabled;
    private bool _isMicrophoneToggleEnabled;
    private bool _isCameraToggleEnabled;
    private bool _isScreenShareToggleEnabled;
    private Visibility _incomingCallVisibility = Visibility.Collapsed;
    private string _incomingCallText = string.Empty;
    private Visibility _callControlsVisibility = Visibility.Collapsed;
    private Visibility _mediaHostVisibility = Visibility.Collapsed;
    private GridLength _mediaHostRowHeight = new GridLength(0);
    private string _callStatusText = string.Empty;
    private string _microphoneToggleButtonContent = "Микрофон";
    private string _cameraToggleButtonContent = "Камера";
    private string _screenShareToggleButtonContent = "Экран";

    public ChatPageViewModel(
        ChatCoordinator chatCoordinator,
        IDirectChatFeatureClient directChatClient,
        ChatStore chatStore,
        SessionStore sessionStore,
        AuthSessionStore authStore,
        RealtimeStartupService realtimeStartupService,
        CallCoordinator callCoordinator,
        CallStore callStore,
        IRealtimeGateway realtimeGateway,
        ClientRuntimeOptions options)
    {
        _chatCoordinator = chatCoordinator;
        _directChatClient = directChatClient;
        _chatStore = chatStore;
        _sessionStore = sessionStore;
        _authStore = authStore;
        _realtimeStartupService = realtimeStartupService;
        _callCoordinator = callCoordinator;
        _callStore = callStore;
        _realtimeGateway = realtimeGateway;
        _options = options;
    }

    public ObservableCollection<ChatDialogItem> Dialogs { get; } = new();
    public ObservableCollection<ChatDialogItem> FilteredDialogs { get; } = new();
    public ObservableCollection<ChatMessageItem> Messages { get; } = new();
    public ObservableCollection<DirectChatMessageViewItem> DisplayedMessages { get; } = new();
    public ObservableCollection<DirectUserSearchItem> UserSearchResults { get; } = new();
    public ObservableCollection<DirectCallParticipantViewItem> CallParticipants { get; } = new();

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

    public string UserSearchStatusText
    {
        get => _userSearchStatusText;
        private set => SetProperty(ref _userSearchStatusText, value);
    }

    public bool IsSearchingUsers
    {
        get => _isSearchingUsers;
        private set => SetProperty(ref _isSearchingUsers, value);
    }

    public bool IsStartAudioCallEnabled
    {
        get => _isStartAudioCallEnabled;
        private set => SetProperty(ref _isStartAudioCallEnabled, value);
    }

    public bool IsStartVideoCallEnabled
    {
        get => _isStartVideoCallEnabled;
        private set => SetProperty(ref _isStartVideoCallEnabled, value);
    }

    public bool IsEndCallEnabled
    {
        get => _isEndCallEnabled;
        private set => SetProperty(ref _isEndCallEnabled, value);
    }

    public bool IsMicrophoneToggleEnabled
    {
        get => _isMicrophoneToggleEnabled;
        private set => SetProperty(ref _isMicrophoneToggleEnabled, value);
    }

    public bool IsCameraToggleEnabled
    {
        get => _isCameraToggleEnabled;
        private set => SetProperty(ref _isCameraToggleEnabled, value);
    }

    public bool IsScreenShareToggleEnabled
    {
        get => _isScreenShareToggleEnabled;
        private set => SetProperty(ref _isScreenShareToggleEnabled, value);
    }

    public Visibility IncomingCallVisibility
    {
        get => _incomingCallVisibility;
        private set => SetProperty(ref _incomingCallVisibility, value);
    }

    public string IncomingCallText
    {
        get => _incomingCallText;
        private set => SetProperty(ref _incomingCallText, value);
    }

    public Visibility CallControlsVisibility
    {
        get => _callControlsVisibility;
        private set => SetProperty(ref _callControlsVisibility, value);
    }

    public Visibility MediaHostVisibility
    {
        get => _mediaHostVisibility;
        private set => SetProperty(ref _mediaHostVisibility, value);
    }

    public GridLength MediaHostRowHeight
    {
        get => _mediaHostRowHeight;
        private set => SetProperty(ref _mediaHostRowHeight, value);
    }

    public string CallStatusText
    {
        get => _callStatusText;
        private set => SetProperty(ref _callStatusText, value);
    }

    public string MicrophoneToggleButtonContent
    {
        get => _microphoneToggleButtonContent;
        private set => SetProperty(ref _microphoneToggleButtonContent, value);
    }

    public string CameraToggleButtonContent
    {
        get => _cameraToggleButtonContent;
        private set => SetProperty(ref _cameraToggleButtonContent, value);
    }

    public string ScreenShareToggleButtonContent
    {
        get => _screenShareToggleButtonContent;
        private set => SetProperty(ref _screenShareToggleButtonContent, value);
    }

    public event EventHandler? NavigateToLoginRequested;
    public event EventHandler<DirectCallNavigationRequest>? NavigateToDirectCallRequested;

    public async Task ActivateAsync(CoreDispatcher dispatcher)
    {
        if (_isActive)
            return;

        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _isActive = true;

        _chatStore.StateChanged += ChatStore_StateChanged;
        _sessionStore.StateChanged += SessionStore_StateChanged;
        _authStore.StateChanged += AuthStore_StateChanged;
        _callStore.StateChanged += CallStore_StateChanged;
        _realtimeGateway.EnvelopeReceived += Gateway_EnvelopeReceived;

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
            ApplyCallState(_callStore.Current);
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
            ApplyCallState(_callStore.Current);
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
        _callStore.StateChanged -= CallStore_StateChanged;
        _realtimeGateway.EnvelopeReceived -= Gateway_EnvelopeReceived;

        _dispatcher = null;
    }

    public async Task AttachAudioHostAsync(IAudioBridgeHost host, CancellationToken cancellationToken)
    {
        await _callCoordinator.AttachHostAsync(host, cancellationToken).ConfigureAwait(false);
        await RunOnUiThreadAsync(() => ApplyCallState(_callStore.Current)).ConfigureAwait(false);
    }

    public async Task SearchUsersByEmailAsync(string? rawQuery, CancellationToken cancellationToken = default)
    {
        var query = rawQuery?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            await RunOnUiThreadAsync(() =>
            {
                UserSearchResults.Clear();
                UserSearchStatusText = string.Empty;
                IsSearchingUsers = false;
            }).ConfigureAwait(false);
            return;
        }

        await RunOnUiThreadAsync(() =>
        {
            IsSearchingUsers = true;
            UserSearchStatusText = "Поиск…";
        }).ConfigureAwait(false);

        var result = await _directChatClient
            .SearchUsersByEmailAsync(query, 20, cancellationToken)
            .ConfigureAwait(false);

        await RunOnUiThreadAsync(() =>
        {
            IsSearchingUsers = false;
            UserSearchResults.Clear();

            if (result.IsFailure)
            {
                UserSearchStatusText = result.Error?.Message ?? "Не удалось выполнить поиск.";
                return;
            }

            foreach (var user in result.Value!)
            {
                RememberUserLabel(user.UserId, user.DisplayName, user.Email);
                UserSearchResults.Add(user);
            }

            UserSearchStatusText = UserSearchResults.Count == 0
                ? "Ничего не найдено."
                : "Найдено: " + UserSearchResults.Count;
        }).ConfigureAwait(false);
    }

    public async Task OpenSearchResultAsync(DirectUserSearchItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.UserId))
            return;
        RememberUserLabel(item.UserId, item.DisplayName, item.Email);

        await OpenDirectDialogAsync(item.UserId).ConfigureAwait(false);
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
            UpdateCallActionAvailability();
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
            var existing = _dialogMap.Values.FirstOrDefault(x =>
                string.Equals(x.PeerId, peerId, StringComparison.Ordinal));

            if (existing != null)
            {
                SetSelectedDialog(existing);
            }
            else
            {
                var provisional = CreateProvisionalDialog(peerId);
                _dialogMap[provisional.ConversationId] = provisional;
                SetSelectedDialog(provisional);
                RebuildDialogCollections();
            }
            UpdateSelectedDialogPresentation();
            RebuildMessages(_chatStore.Current);
            UpdateCallActionAvailability();
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

    public async Task StartDirectAudioCallAsync(CancellationToken cancellationToken = default)
    {
        await StartDirectCallAsync("audio", false, cancellationToken).ConfigureAwait(false);
    }

    public async Task StartDirectVideoCallAsync(CancellationToken cancellationToken = default)
    {
        await StartDirectCallAsync("video", true, cancellationToken).ConfigureAwait(false);
    }

    public async Task AcceptIncomingCallAsync(bool withVideo = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_incomingCallId))
            return;

        var callId = _incomingCallId!;
        var callerUserId = _incomingCallerUserId;
        var callerDisplayName = _incomingCallerDisplayName;
        var callerEmail = _incomingCallerEmail;
        var callerTitle = ResolveUserLabel(
            callerUserId,
            callerDisplayName ?? ResolveDialogTitleByPeer(callerUserId),
            callerEmail);
        ClearIncomingCall();

        if (!string.IsNullOrWhiteSpace(callerUserId))
            await OpenDirectDialogAsync(callerUserId).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
            return;

        await RunOnUiThreadAsync(() =>
        {
            NavigateToDirectCallRequested?.Invoke(
                this,
                new DirectCallNavigationRequest(
                    callId,
                    callerUserId,
                    callerTitle,
                    isIncoming: true,
                    autoEnableCamera: withVideo));
        }).ConfigureAwait(false);
    }

    public async Task DeclineIncomingCallAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_incomingCallId))
            return;

        var callId = _incomingCallId!;
        ClearIncomingCall();

        var result = await _callCoordinator.DeclineDirectCallAsync(callId, "declined", cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            await RunOnUiThreadAsync(() =>
            {
                ApplyError(result.Error?.Message ?? "Не удалось отклонить звонок.");
            }).ConfigureAwait(false);
        }
    }

    public async Task EndCurrentCallAsync(CancellationToken cancellationToken = default)
    {
        var state = _callStore.Current;
        if (state.Kind == CallKind.Direct && !string.IsNullOrWhiteSpace(state.SessionId))
        {
            var endResult = await _callCoordinator
                .EndDirectCallAsync(state.SessionId!, "client_hangup", cancellationToken)
                .ConfigureAwait(false);

            if (endResult.IsFailure)
            {
                await RunOnUiThreadAsync(() =>
                {
                    ApplyError(endResult.Error?.Message ?? "Не удалось завершить звонок.");
                }).ConfigureAwait(false);
            }

            return;
        }

        await _callCoordinator.LeaveAudioAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ToggleMicrophoneAsync(CancellationToken cancellationToken = default)
    {
        var result = await _callCoordinator.ToggleMicrophoneAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            await RunOnUiThreadAsync(() =>
            {
                ApplyError(result.Error?.Message ?? "Не удалось переключить микрофон.");
            }).ConfigureAwait(false);
        }
    }

    public async Task ToggleCameraAsync(CancellationToken cancellationToken = default)
    {
        var result = await _callCoordinator.ToggleCameraAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            await RunOnUiThreadAsync(() =>
            {
                ApplyError(result.Error?.Message ?? "Не удалось переключить камеру.");
            }).ConfigureAwait(false);
        }
    }

    public async Task ToggleScreenShareAsync(CancellationToken cancellationToken = default)
    {
        var result = await _callCoordinator.ToggleScreenShareAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            await RunOnUiThreadAsync(() =>
            {
                ApplyError(result.Error?.Message ?? "Не удалось переключить демонстрацию экрана.");
            }).ConfigureAwait(false);
        }
    }

    public void ApplyDialogsFilter(string? query)
    {
        _dialogsSearchQuery = query;

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

    private async Task StartDirectCallAsync(string mode, bool startVideoAfterJoin, CancellationToken cancellationToken)
    {
        var selectedDialog = SelectedDialog;
        if (selectedDialog == null)
            return;

        var targetUserId = ResolveTargetUserIdForSelectedDialog();
        if (string.IsNullOrWhiteSpace(targetUserId))
        {
            await RunOnUiThreadAsync(() =>
            {
                ApplyError("Не удалось определить пользователя для звонка.");
            }).ConfigureAwait(false);
            return;
        }

        var createResult = await _callCoordinator.StartDirectCallAsync(targetUserId, mode, cancellationToken).ConfigureAwait(false);
        if (createResult.IsFailure)
        {
            await RunOnUiThreadAsync(() =>
            {
                ApplyError(createResult.Error?.Message ?? "Не удалось начать звонок.");
            }).ConfigureAwait(false);
            return;
        }

        await RunOnUiThreadAsync(() =>
        {
            var counterpartTitle = ResolveUserLabel(
                targetUserId,
                selectedDialog.Title,
                selectedDialog.Subtitle);

            NavigateToDirectCallRequested?.Invoke(
                this,
                new DirectCallNavigationRequest(
                    createResult.Value!,
                    targetUserId,
                    counterpartTitle,
                    isIncoming: false,
                    autoEnableCamera: startVideoAfterJoin));
        }).ConfigureAwait(false);
    }

    private async Task<bool> EnsureAuthorizedAsync()
    {
        var auth = _authStore.Current;
        if (!auth.IsAuthenticated || string.IsNullOrWhiteSpace(auth.UserId))
            return false;

        try
        {
            var result = await _realtimeStartupService
                .EnsureConnectedAsync(_options.DefaultRealtimeEndpoint)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                await RunOnUiThreadAsync(() =>
                {
                    ApplyError(result.Error?.Message ?? "Не удалось подключить realtime.");
                }).ConfigureAwait(false);
                return false;
            }
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() =>
            {
                ApplyError(ex.Message);
            }).ConfigureAwait(false);
            return false;
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

    private async void CallStore_StateChanged(object sender, CallSessionState state)
    {
        if (!_isActive)
            return;

        await RunOnUiThreadAsync(() =>
        {
            ApplyCallState(state);
        }).ConfigureAwait(false);
    }

    private async void Gateway_EnvelopeReceived(object? sender, FeatureResponseEnvelope envelope)
    {
        if (!_isActive)
            return;

        if (string.Equals(envelope.Type, ProtocolMessageTypes.DirectCallInvite, StringComparison.Ordinal))
        {
            var payload = envelope.TryGetPayload(out var payloadNode) && payloadNode.ValueKind == System.Text.Json.JsonValueKind.Object
                ? payloadNode
                : (System.Text.Json.JsonElement?)null;

            var callId = envelope.GetString("callId")
                ?? payload?.GetString("callId", "call_id");
            var callerUserId = envelope.GetString("callerUserId")
                ?? payload?.GetString("callerUserId", "caller_user_id");
            var callerDisplayName = envelope.GetString("callerDisplayName")
                ?? payload?.GetString("callerDisplayName", "caller_display_name");
            var callerEmail = envelope.GetString("callerEmail")
                ?? payload?.GetString("callerEmail", "caller_email");
            var targetUserId = envelope.GetString("targetUserId")
                ?? payload?.GetString("targetUserId", "target_user_id");

            if (string.IsNullOrWhiteSpace(callId))
                return;

            var currentUserId = _authStore.Current.UserId;
            if (!string.IsNullOrWhiteSpace(targetUserId) &&
                !string.IsNullOrWhiteSpace(currentUserId) &&
                !string.Equals(targetUserId, currentUserId, StringComparison.Ordinal))
            {
                return;
            }

            await RunOnUiThreadAsync(() =>
            {
                var callerTitle = ResolveUserLabel(
                    callerUserId,
                    callerDisplayName ?? ResolveDialogTitleByPeer(callerUserId),
                    callerEmail);
                _incomingCallId = callId;
                _incomingCallerUserId = callerUserId;
                _incomingCallerDisplayName = callerDisplayName;
                _incomingCallerEmail = callerEmail;
                IncomingCallText = string.IsNullOrWhiteSpace(callerUserId)
                    ? "Входящий звонок"
                    : "Входящий звонок от " + callerTitle;
                IncomingCallVisibility = Visibility.Visible;
            }).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(callerUserId))
                await OpenDirectDialogAsync(callerUserId).ConfigureAwait(false);
            return;
        }

        if (string.Equals(envelope.Type, ProtocolMessageTypes.DirectCallEnded, StringComparison.Ordinal) ||
            string.Equals(envelope.Type, ProtocolMessageTypes.DirectCallDeclined, StringComparison.Ordinal))
        {
            var endedCallId = envelope.GetString("callId");
            if (!string.IsNullOrWhiteSpace(_incomingCallId) &&
                string.Equals(_incomingCallId, endedCallId, StringComparison.Ordinal))
            {
                await RunOnUiThreadAsync(ClearIncomingCall).ConfigureAwait(false);
            }
        }
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
        {
            dialog.Title = ResolveUserLabel(dialog.PeerId, dialog.Title);
            _dialogMap[dialog.ConversationId] = dialog;
            RememberUserLabel(dialog.PeerId, dialog.Title, dialog.Subtitle);
        }

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
        UpdateCallActionAvailability();
    }

    private void ApplyCallState(CallSessionState state)
    {
        var isDirect = state.Kind == CallKind.Direct && !string.IsNullOrWhiteSpace(state.SessionId);
        var canShowMedia = isDirect &&
                           state.Stage != CallConnectionStage.Idle &&
                           state.Stage != CallConnectionStage.Faulted;

        CallControlsVisibility = canShowMedia ? Visibility.Visible : Visibility.Collapsed;
        MediaHostVisibility = canShowMedia ? Visibility.Visible : Visibility.Collapsed;
        MediaHostRowHeight = canShowMedia ? new GridLength(320) : new GridLength(0);

        MicrophoneToggleButtonContent = state.LocalMedia.MicrophoneEnabled ? "Микрофон вкл" : "Микрофон выкл";
        CameraToggleButtonContent = state.LocalMedia.CameraEnabled ? "Камера вкл" : "Камера выкл";
        ScreenShareToggleButtonContent = state.LocalMedia.ScreenShareEnabled ? "Экран вкл" : "Экран выкл";

        var isConnected = state.Stage == CallConnectionStage.Connected;
        IsEndCallEnabled = canShowMedia;
        IsMicrophoneToggleEnabled = isConnected;
        IsCameraToggleEnabled = isConnected;
        IsScreenShareToggleEnabled = isConnected;

        CallStatusText = BuildCallStatusText(state);
        var selectedDialogFallbackTitle = ResolveUserLabel(
            ResolveTargetUserIdForSelectedDialog(),
            SelectedDialog?.Title,
            SelectedDialog?.Subtitle);

        CallParticipants.Clear();
        foreach (var participant in state.Participants
                     .OrderBy(
                         x => UserFacingIdentityFormatter.ResolveParticipantLabel(x.PeerId, x.UserId),
                         StringComparer.OrdinalIgnoreCase))
        {
            var participantTitle = ResolveUserLabel(participant.PeerId, participant.UserId);
            if ((UserFacingIdentityFormatter.LooksLikeTechnicalId(participantTitle) ||
                 string.Equals(participantTitle, "Пользователь", StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrWhiteSpace(selectedDialogFallbackTitle) &&
                !UserFacingIdentityFormatter.LooksLikeTechnicalId(selectedDialogFallbackTitle) &&
                !string.Equals(selectedDialogFallbackTitle, "Пользователь", StringComparison.OrdinalIgnoreCase))
            {
                participantTitle = selectedDialogFallbackTitle;
            }
            RememberUserLabel(participant.PeerId, participant.UserId, null);
            CallParticipants.Add(new DirectCallParticipantViewItem(
                participantTitle,
                participant.HasAudio,
                participant.HasVideo,
                participant.HasScreenShare));
        }

        if (!isDirect && state.Stage == CallConnectionStage.Idle)
            CallStatusText = string.Empty;

        UpdateCallActionAvailability();
    }

    private string BuildCallStatusText(CallSessionState state)
    {
        if (state.Kind != CallKind.Direct)
            return string.Empty;

        return state.Stage switch
        {
            CallConnectionStage.Idle => "Звонок не активен",
            CallConnectionStage.JoiningRoom => "Подключение к звонку…",
            CallConnectionStage.TransportOpening => "Открытие транспорта…",
            CallConnectionStage.Negotiating => "Согласование медиа…",
            CallConnectionStage.Publishing => "Публикация треков…",
            CallConnectionStage.Connected => "Вы в звонке",
            CallConnectionStage.Faulted => "Ошибка звонка",
            _ => state.Stage.ToString()
        };
    }

    private string? ResolveTargetUserIdForSelectedDialog()
    {
        if (SelectedDialog == null)
            return null;

        var selectedPeerId = SelectedDialog.PeerId;
        if (!string.IsNullOrWhiteSpace(selectedPeerId) && !LooksLikePeerSessionId(selectedPeerId))
            return selectedPeerId;

        var conversationId = SelectedDialog.ConversationId;
        var selfUserId = _authStore.Current.UserId;
        foreach (var message in _chatStore.Current.Messages
                     .Where(x => x.IsDirect && string.Equals(x.ConversationId, conversationId, StringComparison.Ordinal))
                     .OrderByDescending(x => x.SentAtUtc))
        {
            if (message.IsOwn)
            {
                if (!string.IsNullOrWhiteSpace(message.TargetId) && !LooksLikePeerSessionId(message.TargetId))
                    return message.TargetId;

                continue;
            }

            if (!string.IsNullOrWhiteSpace(message.SenderUserId))
                return message.SenderUserId;

            if (!string.IsNullOrWhiteSpace(message.SenderPeerId) &&
                !LooksLikePeerSessionId(message.SenderPeerId) &&
                !string.Equals(message.SenderPeerId, selfUserId, StringComparison.Ordinal))
            {
                return message.SenderPeerId;
            }
        }

        return selectedPeerId;
    }

    private static bool LooksLikePeerSessionId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return value.StartsWith("peer", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveDialogSubtitle(ChatDialogItem dialog)
    {
        if (!string.IsNullOrWhiteSpace(dialog.Subtitle) &&
            !string.Equals(dialog.Subtitle, "Личный чат", StringComparison.OrdinalIgnoreCase) &&
            !UserFacingIdentityFormatter.LooksLikeTechnicalId(dialog.Subtitle))
        {
            return dialog.Subtitle;
        }

        var targetUserId = ResolveTargetUserIdForSelectedDialog();
        if (!string.IsNullOrWhiteSpace(targetUserId))
        {
            var targetTitle = ResolveUserLabel(targetUserId, ResolveDialogTitleByPeer(targetUserId));
            if (!string.IsNullOrWhiteSpace(targetTitle) &&
                !UserFacingIdentityFormatter.LooksLikeTechnicalId(targetTitle) &&
                !string.Equals(targetTitle, "Пользователь", StringComparison.OrdinalIgnoreCase))
            {
                return targetTitle;
            }
        }

        if (!string.IsNullOrWhiteSpace(dialog.Title) &&
            !UserFacingIdentityFormatter.LooksLikeTechnicalId(dialog.Title))
        {
            return dialog.Title;
        }

        return "Личный чат";
    }

    private string? ResolveDialogTitleByPeer(string? peerId)
    {
        if (string.IsNullOrWhiteSpace(peerId))
            return null;

        var dialog = _dialogMap.Values.FirstOrDefault(x =>
            string.Equals(x.PeerId, peerId, StringComparison.Ordinal));

        if (dialog == null || string.IsNullOrWhiteSpace(dialog.Title))
            return null;

        return dialog.Title;
    }

    private DirectChatMessageViewItem CreateDisplayedMessageItem(ChatMessageItem item)
    {
        var senderTitle = ResolveMessageSenderTitle(item);
        var senderMeta = ResolveMessageSenderMeta(item, senderTitle);
        var statusText = item.IsOwn ? item.DisplayStatus : string.Empty;

        return new DirectChatMessageViewItem(
            senderTitle,
            senderMeta,
            item.Text,
            item.DisplayTime,
            statusText,
            item.IsOwn);
    }

    private string ResolveMessageSenderTitle(ChatMessageItem item)
    {
        if (item.IsOwn)
            return "Вы";

        if (!string.IsNullOrWhiteSpace(item.SenderDisplayName) &&
            !UserFacingIdentityFormatter.LooksLikeTechnicalId(item.SenderDisplayName))
            return item.SenderDisplayName;

        if (!string.IsNullOrWhiteSpace(item.SenderEmail) && item.SenderEmail.Contains("@"))
            return item.SenderEmail;

        if (!string.IsNullOrWhiteSpace(item.SenderUserId))
            return ResolveUserLabel(item.SenderUserId, ResolveDialogTitleByPeer(item.SenderUserId), item.SenderEmail);

        return ResolveUserLabel(item.SenderPeerId, ResolveDialogTitleByPeer(item.SenderPeerId), item.SenderEmail);
    }

    private string ResolveMessageSenderMeta(ChatMessageItem item, string senderTitle)
    {
        if (item.IsOwn)
        {
            if (!string.IsNullOrWhiteSpace(_authStore.Current.Email))
                return _authStore.Current.Email!;

            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(item.SenderEmail) &&
            !string.Equals(item.SenderEmail, senderTitle, StringComparison.OrdinalIgnoreCase))
        {
            return item.SenderEmail;
        }


        return string.Empty;
    }

    private void UpdateCallActionAvailability()
    {
        var hasDialog = !string.IsNullOrWhiteSpace(ResolveTargetUserIdForSelectedDialog());
        var callState = _callStore.Current;
        var hasActiveDirectCall = callState.Kind == CallKind.Direct &&
                                  callState.Stage != CallConnectionStage.Idle &&
                                  callState.Stage != CallConnectionStage.Faulted;

        IsStartAudioCallEnabled = hasDialog && !hasActiveDirectCall;
        IsStartVideoCallEnabled = hasDialog && !hasActiveDirectCall;
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

        ApplyDialogsFilter(_dialogsSearchQuery);
    }

    private void RebuildMessages(ChatViewState state)
    {
        Messages.Clear();
        DisplayedMessages.Clear();

        if (SelectedDialog == null)
            return;

        var selectedConversationId = SelectedDialog.ConversationId;
        var selectedPeerId = SelectedDialog.PeerId;
        var selectedTargetUserId = ResolveTargetUserIdForSelectedDialog();

        var items = state.Messages
            .Where(x =>
                x.IsDirect &&
                (
                    string.Equals(x.ConversationId, selectedConversationId, StringComparison.Ordinal) ||
                    (!string.IsNullOrWhiteSpace(selectedPeerId) &&
                     (
                        string.Equals(x.TargetId, selectedPeerId, StringComparison.Ordinal) ||
                        string.Equals(x.SenderPeerId, selectedPeerId, StringComparison.Ordinal) ||
                        string.Equals(x.SenderUserId, selectedPeerId, StringComparison.Ordinal)
                     )) ||
                    (!string.IsNullOrWhiteSpace(selectedTargetUserId) &&
                     (
                        string.Equals(x.TargetId, selectedTargetUserId, StringComparison.Ordinal) ||
                        string.Equals(x.SenderPeerId, selectedTargetUserId, StringComparison.Ordinal) ||
                        string.Equals(x.SenderUserId, selectedTargetUserId, StringComparison.Ordinal)
                     ))
                ))
            .OrderBy(x => x.SentAtUtc)
            .ThenBy(x => x.LocalId, StringComparer.Ordinal)
            .ToList();

        foreach (var item in items)
        {
            Messages.Add(item);
            DisplayedMessages.Add(CreateDisplayedMessageItem(item));
        }
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
        var resolvedSubtitle = ResolveDialogSubtitle(SelectedDialog);
        ActiveDialogSubtitle = string.IsNullOrWhiteSpace(resolvedSubtitle)
            ? "Личный чат"
            : resolvedSubtitle;

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
            Title = ResolveUserLabel(peerId, null),
            Subtitle = "Личный чат",
            LastMessagePreview = string.Empty,
            LastActivityUtc = DateTimeOffset.MinValue,
            UnreadCount = 0,
            IsPinned = false
        };
    }

    private void RememberUserLabel(string? userId, string? displayName, string? email)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        var normalized = ResolveUserLabel(userId, displayName, email);
        if (!string.IsNullOrWhiteSpace(normalized))
            _knownUserLabels[userId] = normalized;
    }

    private string ResolveUserLabel(string? userId, string? preferredLabel, string? fallbackEmail = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredLabel) &&
            !UserFacingIdentityFormatter.LooksLikeTechnicalId(preferredLabel))
        {
            return preferredLabel!;
        }

        if (!string.IsNullOrWhiteSpace(fallbackEmail) &&
            fallbackEmail!.Contains("@", StringComparison.Ordinal))
        {
            return fallbackEmail!;
        }

        if (!string.IsNullOrWhiteSpace(userId) &&
            _knownUserLabels.TryGetValue(userId, out var known) &&
            !string.IsNullOrWhiteSpace(known))
        {
            return known;
        }

        return UserFacingIdentityFormatter.ResolveUserLabel(userId, preferredLabel, fallbackEmail);
    }

    private void ApplyError(string? error)
    {
        ErrorText = error ?? string.Empty;
    }

    private void RaiseNavigateToLogin()
    {
        NavigateToLoginRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ClearIncomingCall()
    {
        _incomingCallId = null;
        _incomingCallerUserId = null;
        _incomingCallerDisplayName = null;
        _incomingCallerEmail = null;
        IncomingCallText = string.Empty;
        IncomingCallVisibility = Visibility.Collapsed;
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

public sealed class DirectCallNavigationRequest
{
    public DirectCallNavigationRequest(
        string callId,
        string? counterpartUserId,
        string counterpartTitle,
        bool isIncoming,
        bool autoEnableCamera)
    {
        CallId = callId;
        CounterpartUserId = counterpartUserId;
        CounterpartTitle = counterpartTitle;
        IsIncoming = isIncoming;
        AutoEnableCamera = autoEnableCamera;
    }

    public string CallId { get; }
    public string? CounterpartUserId { get; }
    public string CounterpartTitle { get; }
    public bool IsIncoming { get; }
    public bool AutoEnableCamera { get; }
}

public sealed class DirectChatMessageViewItem
{
    private static readonly SolidColorBrush OwnBubbleBrush = new(Color.FromArgb(0xFF, 0x2B, 0x52, 0x78));
    private static readonly SolidColorBrush RemoteBubbleBrush = new(Color.FromArgb(0xFF, 0x1E, 0x2C, 0x3A));

    public DirectChatMessageViewItem(
        string senderTitle,
        string senderMeta,
        string text,
        string timeText,
        string deliveryStatusText,
        bool isOwn)
    {
        SenderTitle = senderTitle;
        SenderMeta = senderMeta;
        Text = text;
        TimeText = timeText;
        DeliveryStatusText = deliveryStatusText;
        IsOwn = isOwn;
    }

    public string SenderTitle { get; }
    public string SenderMeta { get; }
    public string Text { get; }
    public string TimeText { get; }
    public string DeliveryStatusText { get; }
    public bool IsOwn { get; }

    public HorizontalAlignment BubbleAlignment => IsOwn ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    public SolidColorBrush BubbleBackground => IsOwn ? OwnBubbleBrush : RemoteBubbleBrush;
    public string FooterText => string.IsNullOrWhiteSpace(DeliveryStatusText)
        ? TimeText
        : TimeText + " • " + DeliveryStatusText;
}

public sealed class DirectCallParticipantViewItem
{
    public DirectCallParticipantViewItem(
        string title,
        bool hasAudio,
        bool hasVideo,
        bool hasScreenShare)
    {
        Title = title;
        HasAudio = hasAudio;
        HasVideo = hasVideo;
        HasScreenShare = hasScreenShare;
    }

    public string Title { get; }
    public bool HasAudio { get; }
    public bool HasVideo { get; }
    public bool HasScreenShare { get; }
    public string MediaSummary =>
        (HasAudio ? "🎤 " : string.Empty) +
        (HasVideo ? "📷 " : string.Empty) +
        (HasScreenShare ? "🖥 " : string.Empty);
}
