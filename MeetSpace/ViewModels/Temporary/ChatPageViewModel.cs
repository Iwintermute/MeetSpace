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
using Windows.UI.Core;
using Windows.UI.Xaml;

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

    private CoreDispatcher? _dispatcher;
    private bool _isActive;
    private string? _dialogsSearchQuery;
    private ChatDialogItem? _selectedDialog;
    private string? _incomingCallId;
    private string? _incomingCallerUserId;
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
                UserSearchResults.Add(user);

            UserSearchStatusText = UserSearchResults.Count == 0
                ? "Ничего не найдено."
                : "Найдено: " + UserSearchResults.Count;
        }).ConfigureAwait(false);
    }

    public async Task OpenSearchResultAsync(DirectUserSearchItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.UserId))
            return;

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
            var provisional = CreateProvisionalDialog(peerId);
            _dialogMap[provisional.ConversationId] = provisional;

            SetSelectedDialog(provisional);
            RebuildDialogCollections();
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
        ClearIncomingCall();

        if (!string.IsNullOrWhiteSpace(callerUserId))
            await OpenDirectDialogAsync(callerUserId).ConfigureAwait(false);

        var result = await _callCoordinator.AcceptAndJoinDirectCallAsync(callId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            await RunOnUiThreadAsync(() =>
            {
                ApplyError(result.Error?.Message ?? "Не удалось принять звонок.");
            }).ConfigureAwait(false);
            return;
        }

        if (withVideo)
            await ToggleCameraAsync(cancellationToken).ConfigureAwait(false);
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
        if (SelectedDialog == null || string.IsNullOrWhiteSpace(SelectedDialog.PeerId))
            return;

        var targetUserId = SelectedDialog.PeerId!;
        var createResult = await _callCoordinator.StartDirectCallAsync(targetUserId, mode, cancellationToken).ConfigureAwait(false);
        if (createResult.IsFailure)
        {
            await RunOnUiThreadAsync(() =>
            {
                ApplyError(createResult.Error?.Message ?? "Не удалось начать звонок.");
            }).ConfigureAwait(false);
            return;
        }

        var callId = createResult.Value!;
        var joinResult = await _callCoordinator.JoinDirectCallMediaAsync(callId, cancellationToken).ConfigureAwait(false);
        if (joinResult.IsFailure)
        {
            await RunOnUiThreadAsync(() =>
            {
                ApplyError(joinResult.Error?.Message ?? "Не удалось подключить медиа.");
            }).ConfigureAwait(false);
            return;
        }

        if (startVideoAfterJoin)
            await ToggleCameraAsync(cancellationToken).ConfigureAwait(false);
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
                _incomingCallId = callId;
                _incomingCallerUserId = callerUserId;
                IncomingCallText = string.IsNullOrWhiteSpace(callerUserId)
                    ? "Входящий звонок"
                    : "Входящий звонок от " + callerUserId;
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

        CallParticipants.Clear();
        foreach (var participant in state.Participants.OrderBy(x => x.PeerId, StringComparer.Ordinal))
        {
            CallParticipants.Add(new DirectCallParticipantViewItem(
                string.IsNullOrWhiteSpace(participant.UserId) ? participant.PeerId : participant.UserId!,
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

    private void UpdateCallActionAvailability()
    {
        var hasDialog = SelectedDialog != null && !string.IsNullOrWhiteSpace(SelectedDialog.PeerId);
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

    private void ClearIncomingCall()
    {
        _incomingCallId = null;
        _incomingCallerUserId = null;
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
}
