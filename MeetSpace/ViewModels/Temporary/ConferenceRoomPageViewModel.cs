using CommunityToolkit.Mvvm.ComponentModel;
using MeetSpace.Client.App.Auth;
using MeetSpace.Client.App.Calls;
using MeetSpace.Client.App.Chat;
using MeetSpace.Client.App.Conference;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Domain.Calls;
using MeetSpace.Client.Domain.Chat;
using MeetSpace.Client.Shared.Configuration;
using MeetSpace.Views.Temporary;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace MeetSpace.ViewModels.Temporary;

public sealed class ConferenceRoomPageViewModel : ObservableObject
{
    private readonly ChatCoordinator _chatCoordinator;
    private readonly ChatStore _chatStore;
    private readonly ConferenceCoordinator _conferenceCoordinator;
    private readonly CallCoordinator _callCoordinator;
    private readonly CallStore _callStore;
    private readonly AuthSessionStore _authStore;
    private readonly SessionStore _sessionStore;
    private readonly RealtimeStartupService _realtimeStartupService;
    private readonly ClientRuntimeOptions _options;

    private CoreDispatcher? _dispatcher;
    private bool _isActive;
    private string _conferenceId = string.Empty;

    private string _authorizedUserText = "Вы";
    private string _authorizedUserMetaText = "Защищенное соединение";
    private string _callStatusText = string.Empty;
    private string _participantsSummaryText = "В звонке: только вы";
    private string _microphoneButtonContent = "Подключить аудио";
    private string _cameraButtonContent = "Камера выкл";
    private string _screenShareButtonContent = "Экран выкл";
    private bool _isMicrophoneButtonEnabled = true;
    private bool _isCameraButtonEnabled = true;
    private bool _isScreenShareButtonEnabled = true;
    private Visibility _chatPanelVisibility = Visibility.Collapsed;
    private GridLength _chatPanelWidth = new GridLength(0);

    public ConferenceRoomPageViewModel(
        ChatCoordinator chatCoordinator,
        ChatStore chatStore,
        ConferenceCoordinator conferenceCoordinator,
        CallCoordinator callCoordinator,
        CallStore callStore,
        AuthSessionStore authStore,
        SessionStore sessionStore,
        RealtimeStartupService realtimeStartupService,
        ClientRuntimeOptions options)
    {
        _chatCoordinator = chatCoordinator;
        _chatStore = chatStore;
        _conferenceCoordinator = conferenceCoordinator;
        _callCoordinator = callCoordinator;
        _callStore = callStore;
        _authStore = authStore;
        _sessionStore = sessionStore;
        _realtimeStartupService = realtimeStartupService;
        _options = options;
    }


    public ObservableCollection<ConferenceChatMessageViewItem> Messages { get; } = new();
    public ObservableCollection<ConferenceParticipantTileViewItem> Participants { get; } = new();

    public string AuthorizedUserText
    {
        get => _authorizedUserText;
        private set => SetProperty(ref _authorizedUserText, value);
    }

    public string AuthorizedUserMetaText
    {
        get => _authorizedUserMetaText;
        private set => SetProperty(ref _authorizedUserMetaText, value);
    }

    public string CallStatusText
    {
        get => _callStatusText;
        private set => SetProperty(ref _callStatusText, value);
    }
    public string ParticipantsSummaryText
    {
        get => _participantsSummaryText;
        private set => SetProperty(ref _participantsSummaryText, value);
    }

    public string MicrophoneButtonContent
    {
        get => _microphoneButtonContent;
        private set => SetProperty(ref _microphoneButtonContent, value);
    }

    public string CameraButtonContent
    {
        get => _cameraButtonContent;
        private set => SetProperty(ref _cameraButtonContent, value);
    }

    public string ScreenShareButtonContent
    {
        get => _screenShareButtonContent;
        private set => SetProperty(ref _screenShareButtonContent, value);
    }

    public bool IsMicrophoneButtonEnabled
    {
        get => _isMicrophoneButtonEnabled;
        private set => SetProperty(ref _isMicrophoneButtonEnabled, value);
    }

    public bool IsCameraButtonEnabled
    {
        get => _isCameraButtonEnabled;
        private set => SetProperty(ref _isCameraButtonEnabled, value);
    }

    public bool IsScreenShareButtonEnabled
    {
        get => _isScreenShareButtonEnabled;
        private set => SetProperty(ref _isScreenShareButtonEnabled, value);
    }

    public Visibility ChatPanelVisibility
    {
        get => _chatPanelVisibility;
        private set => SetProperty(ref _chatPanelVisibility, value);
    }

    public GridLength ChatPanelWidth
    {
        get => _chatPanelWidth;
        private set => SetProperty(ref _chatPanelWidth, value);
    }

    public event EventHandler? NavigateToLoginRequested;
    public event EventHandler? NavigateBackRequested;

    public async Task ActivateAsync(string conferenceId, CoreDispatcher dispatcher, CancellationToken cancellationToken)
    {
        if (_isActive)
            return;

        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _conferenceId = conferenceId ?? string.Empty;
        _isActive = true;

        _chatStore.StateChanged += ChatStore_StateChanged;
        _authStore.StateChanged += AuthStore_StateChanged;
        _sessionStore.StateChanged += SessionStore_StateChanged;
        _callStore.StateChanged += CallStore_StateChanged;

        await RunOnUiThreadAsync(() =>
        {
            CloseChatPanel();
            ApplyIdentity(_authStore.Current, _sessionStore.Current);
            ApplyChatState(_chatStore.Current);
            ApplyCallState(_callStore.Current);
        }).ConfigureAwait(false);

        var authorized = await EnsureAuthorizedAsync().ConfigureAwait(false);
        if (!authorized)
        {
            await RunOnUiThreadAsync(RaiseNavigateToLogin).ConfigureAwait(false);
            return;
        }

        var peerReady = await WaitForSelfPeerAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        if (!peerReady || cancellationToken.IsCancellationRequested)
        {
            await RunOnUiThreadAsync(() =>
            {
                SetStatusMessage("Не удалось подготовить защищённую сессию.");
            }).ConfigureAwait(false);

            return;
        }

        var conferenceReady = await EnsureConferenceContextAsync(cancellationToken).ConfigureAwait(false);
        if (!conferenceReady || cancellationToken.IsCancellationRequested)
            return;

        await RunOnUiThreadAsync(() =>
        {
            ApplyIdentity(_authStore.Current, _sessionStore.Current);
            ApplyChatState(_chatStore.Current);
            ApplyCallState(_callStore.Current);
        }).ConfigureAwait(false);
    }

    public async Task EnsureConferenceAudioStartedAsync(CancellationToken cancellationToken)
    {
        var connected = await EnsureConferenceCallJoinedAsync(cancellationToken).ConfigureAwait(false);
        if (connected || cancellationToken.IsCancellationRequested)
            return;

        await RunOnUiThreadAsync(() =>
        {
            SetStatusMessage("Не удалось подключить аудио. Попробуйте ещё раз.");
        }).ConfigureAwait(false);
    }

    public async Task DeactivateAsync()
    {
        if (!_isActive)
            return;

        _isActive = false;

        _chatStore.StateChanged -= ChatStore_StateChanged;
        _authStore.StateChanged -= AuthStore_StateChanged;
        _sessionStore.StateChanged -= SessionStore_StateChanged;
        _callStore.StateChanged -= CallStore_StateChanged;

        _dispatcher = null;

        try
        {
            await _callCoordinator.LeaveAudioAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    public async Task AttachAudioHostAsync(IAudioBridgeHost host, CancellationToken cancellationToken)
    {
        await _callCoordinator.AttachHostAsync(host, cancellationToken).ConfigureAwait(false);

        await RunOnUiThreadAsync(() =>
        {
            ApplyCallState(_callStore.Current);
        }).ConfigureAwait(false);
    }

    public void OpenChatPanel()
    {
        ChatPanelVisibility = Visibility.Visible;
        ChatPanelWidth = new GridLength(360);
    }

    public void CloseChatPanel()
    {
        ChatPanelVisibility = Visibility.Collapsed;
        ChatPanelWidth = new GridLength(0);
    }

    public async Task<bool> SendMessageAsync(string? rawText)
    {
        var text = rawText?.Trim();
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(_conferenceId))
            return false;

        var result = await _chatCoordinator
            .SendConferenceMessageAsync(_conferenceId, text)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            await RunOnUiThreadAsync(() =>
            {
                SetStatusMessage(result.Error?.Message ?? "Не удалось отправить сообщение");
            }).ConfigureAwait(false);

            return false;
        }

        return true;
    }

    public async Task HandleMicrophoneAsync(CancellationToken cancellationToken)
    {
        var connected = await EnsureConferenceCallJoinedAsync(cancellationToken).ConfigureAwait(false);
        if (!connected)
            return;

        var toggleResult = await _callCoordinator.ToggleMicrophoneAsync(cancellationToken).ConfigureAwait(false);
        if (toggleResult.IsFailure && !cancellationToken.IsCancellationRequested)
        {
            await RunOnUiThreadAsync(() =>
            {
                SetStatusMessage(toggleResult.Error?.Message ?? "mic toggle failed");
            }).ConfigureAwait(false);
        }
    }

    public async Task HandleCameraAsync(CancellationToken cancellationToken)
    {
        var connected = await EnsureConferenceCallJoinedAsync(cancellationToken).ConfigureAwait(false);
        if (!connected)
            return;

        var toggleResult = await _callCoordinator.ToggleCameraAsync(cancellationToken).ConfigureAwait(false);
        if (toggleResult.IsFailure && !cancellationToken.IsCancellationRequested)
        {
            await RunOnUiThreadAsync(() =>
            {
                SetStatusMessage(toggleResult.Error?.Message ?? "camera toggle failed");
            }).ConfigureAwait(false);
        }
    }

    public async Task HandleScreenShareAsync(CancellationToken cancellationToken)
    {
        var connected = await EnsureConferenceCallJoinedAsync(cancellationToken).ConfigureAwait(false);
        if (!connected)
            return;

        var toggleResult = await _callCoordinator.ToggleScreenShareAsync(cancellationToken).ConfigureAwait(false);
        if (toggleResult.IsFailure && !cancellationToken.IsCancellationRequested)
        {
            await RunOnUiThreadAsync(() =>
            {
                SetStatusMessage(toggleResult.Error?.Message ?? "screen toggle failed");
            }).ConfigureAwait(false);
        }
    }

    public async Task LeaveConferenceAsync()
    {
        var conferenceId = _conferenceId;

        try
        {
            await _callCoordinator.LeaveAudioAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }

        _chatStore.ResetConference(conferenceId);

        if (!string.IsNullOrWhiteSpace(conferenceId))
            _ = _conferenceCoordinator.LeaveConferenceAsync(conferenceId);

        await RunOnUiThreadAsync(() =>
        {
            NavigateBackRequested?.Invoke(this, EventArgs.Empty);
        }).ConfigureAwait(false);
    }

    public void SetStatusMessage(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            AuthorizedUserMetaText = NormalizeUserFacingStatus(message!);
    }

    private async Task<bool> EnsureConferenceCallJoinedAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_conferenceId))
            return false;

        var stage = _callStore.Current.Stage;
        if (stage == CallConnectionStage.Connected)
            return true;

        if (stage == CallConnectionStage.JoiningRoom ||
            stage == CallConnectionStage.TransportOpening ||
            stage == CallConnectionStage.Negotiating ||
            stage == CallConnectionStage.Publishing)
        {
            return false;
        }

        _ = _conferenceCoordinator.ListMembersAsync(_conferenceId, cancellationToken);
        var joinResult = await _callCoordinator.JoinAudioAsync(_conferenceId, cancellationToken).ConfigureAwait(false);
        if (joinResult.IsFailure && !cancellationToken.IsCancellationRequested)
        {
            await RunOnUiThreadAsync(() =>
            {
                SetStatusMessage(joinResult.Error?.Message ?? "audio start failed");
            }).ConfigureAwait(false);
            return false;
        }

        return joinResult.IsSuccess;
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
                    SetStatusMessage(result.Error?.Message ?? "Не удалось подключить realtime.");
                }).ConfigureAwait(false);
                return false;
            }
        }
        catch (Exception ex)
        {
            await RunOnUiThreadAsync(() =>
            {
                SetStatusMessage(ex.Message);
            }).ConfigureAwait(false);
            return false;
        }

        return true;
    }

    private async Task<bool> EnsureConferenceContextAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_conferenceId))
            return false;

        var details = await _conferenceCoordinator.GetConferenceAsync(_conferenceId, cancellationToken).ConfigureAwait(false);
        if (details.IsFailure)
        {
            details = await _conferenceCoordinator.JoinConferenceAsync(_conferenceId, cancellationToken).ConfigureAwait(false);
            if (details.IsFailure)
            {
                await RunOnUiThreadAsync(() =>
                {
                    SetStatusMessage(details.Error?.Message ?? "Не удалось открыть встречу");
                }).ConfigureAwait(false);

                return false;
            }
        }

        _ = _conferenceCoordinator.ListMembersAsync(_conferenceId, cancellationToken);

        var chatResult = await _chatCoordinator
            .LoadConferenceConversationAsync(_conferenceId, cancellationToken)
            .ConfigureAwait(false);

        if (chatResult.IsFailure)
        {
            await RunOnUiThreadAsync(() =>
            {
                SetStatusMessage(chatResult.Error?.Message ?? "Не удалось загрузить чат встречи");
            }).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<bool> WaitForSelfPeerAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;

        while (string.IsNullOrWhiteSpace(_sessionStore.Current.SelfPeerId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DateTimeOffset.UtcNow - startedAt >= timeout)
                return false;

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private async void AuthStore_StateChanged(object sender, AuthSessionState state)
    {
        if (!_isActive)
            return;

        await RunOnUiThreadAsync(() =>
        {
            if (!state.IsAuthenticated)
            {
                RaiseNavigateToLogin();
                return;
            }

            ApplyIdentity(state, _sessionStore.Current);
        }).ConfigureAwait(false);
    }

    private async void SessionStore_StateChanged(object sender, SessionState state)
    {
        if (!_isActive)
            return;

        await RunOnUiThreadAsync(() =>
        {
            ApplyIdentity(_authStore.Current, state);
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

    private async void ChatStore_StateChanged(object sender, ChatViewState state)
    {
        if (!_isActive)
            return;

        await RunOnUiThreadAsync(() =>
        {
            ApplyChatState(state);
        }).ConfigureAwait(false);
    }

    private void ApplyIdentity(AuthSessionState auth, SessionState session)
    {
        var displayName = !string.IsNullOrWhiteSpace(auth.Email)
            ? auth.Email
            : "Вы";

        AuthorizedUserText = displayName;
        AuthorizedUserMetaText = BuildConnectionHint(_callStore.Current.Stage, session);
    }

    private void ApplyCallState(CallSessionState state)
    {
        var joinInProgress =
            state.Stage == CallConnectionStage.JoiningRoom ||
            state.Stage == CallConnectionStage.TransportOpening ||
            state.Stage == CallConnectionStage.Negotiating ||
            state.Stage == CallConnectionStage.Publishing;

        if (joinInProgress)
        {
            IsMicrophoneButtonEnabled = false;
            IsCameraButtonEnabled = false;
            IsScreenShareButtonEnabled = false;
            MicrophoneButtonContent = "Подключение...";
            CameraButtonContent = "Подключение...";
            ScreenShareButtonContent = "Подключение...";
        }
        else if (state.Stage == CallConnectionStage.Connected)
        {
            IsMicrophoneButtonEnabled = true;
            IsCameraButtonEnabled = true;
            IsScreenShareButtonEnabled = true;

            MicrophoneButtonContent = state.LocalMedia.MicrophoneEnabled
                ? "Микрофон вкл"
                : "Микрофон выкл";
            CameraButtonContent = state.LocalMedia.CameraEnabled
                ? "Камера вкл"
                : "Камера выкл";
            ScreenShareButtonContent = state.LocalMedia.ScreenShareEnabled
                ? "Экран вкл"
                : "Экран выкл";
        }
        else
        {
            IsMicrophoneButtonEnabled = true;
            IsCameraButtonEnabled = true;
            IsScreenShareButtonEnabled = true;
            MicrophoneButtonContent = "Подключить аудио";
            CameraButtonContent = "Включить камеру";
            ScreenShareButtonContent = "Демонстрация экрана";
        }

        CallStatusText = state.Stage switch
        {
            CallConnectionStage.Idle => "Не подключено",
            CallConnectionStage.JoiningRoom => "Подключение к конференции…",
            CallConnectionStage.TransportOpening => "Открытие транспорта…",
            CallConnectionStage.Negotiating => "Согласование медиа…",
            CallConnectionStage.Publishing => "Публикация треков…",
            CallConnectionStage.Connected => "Вы в конференции",
            CallConnectionStage.Faulted => "Ошибка подключения",
            _ => state.Stage.ToString()
        };

        Participants.Clear();
        var genericParticipantIndex = 0;
        foreach (var participant in state.Participants.OrderBy(x => UserFacingIdentityFormatter.ResolveParticipantLabel(x.PeerId, x.UserId), StringComparer.OrdinalIgnoreCase))
        {
            var title = UserFacingIdentityFormatter.ResolveParticipantLabel(participant.PeerId, participant.UserId);
            if (string.Equals(title, "Участник", StringComparison.Ordinal))
            {
                genericParticipantIndex++;
                title = $"Участник {genericParticipantIndex}";
            }
            Participants.Add(new ConferenceParticipantTileViewItem(
                title,
                participant.HasAudio,
                participant.HasVideo,
                participant.HasScreenShare));
        }

        var remoteParticipants = state.Participants.Count;
        var totalParticipants = remoteParticipants + (state.Stage == CallConnectionStage.Connected ? 1 : 0);
        ParticipantsSummaryText = totalParticipants <= 1
            ? "В звонке: только вы"
            : $"В звонке: {totalParticipants} участника";

        ApplyIdentity(_authStore.Current, _sessionStore.Current);
    }

    private static string BuildConnectionHint(CallConnectionStage stage, SessionState session)
    {
        var sessionReady = !string.IsNullOrWhiteSpace(session.SelfPeerId);
        var stageLabel = stage switch
        {
            CallConnectionStage.Connected => "Конференция активна",
            CallConnectionStage.JoiningRoom => "Подключение к конференции",
            CallConnectionStage.TransportOpening => "Подготовка медиа-канала",
            CallConnectionStage.Negotiating => "Согласование параметров звонка",
            CallConnectionStage.Publishing => "Запуск аудио и видео",
            CallConnectionStage.Faulted => "Требуется переподключение",
            _ => "Ожидание подключения"
        };

        return sessionReady
            ? stageLabel
            : "Инициализация защищённого соединения";
    }

    private static string NormalizeUserFacingStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Защищенное соединение";

        var normalized = message.Trim();
        if (normalized.IndexOf("peer", StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalized.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0 ||
            normalized.IndexOf("transport", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Проверьте соединение и повторите попытку.";
        }

        return normalized;
    }

    private void ApplyChatState(ChatViewState state)
    {
        Messages.Clear();

        foreach (var item in state.Messages
            .Where(x => !x.IsDirect && string.Equals(x.ConversationId, _conferenceId, StringComparison.Ordinal))
            .OrderBy(x => x.SentAtUtc)
            .ThenBy(x => x.LocalId, StringComparer.Ordinal))
        {
            Messages.Add(new ConferenceChatMessageViewItem(
                ResolveSenderDisplayName(item),
                item.Text,
                item.DisplayTime,
                item.IsOwn));
        }
    }

    private string ResolveSenderDisplayName(ChatMessageItem item)
    {
        if (item.IsOwn)
        {
            if (!string.IsNullOrWhiteSpace(_authStore.Current.Email))
                return _authStore.Current.Email!;

            if (!string.IsNullOrWhiteSpace(_authStore.Current.UserId))
                return _authStore.Current.UserId!;

            return "Вы";
        }

        if (!string.IsNullOrWhiteSpace(item.SenderDisplayName) &&
            !UserFacingIdentityFormatter.LooksLikeTechnicalId(item.SenderDisplayName))
            return item.SenderDisplayName;

        if (!string.IsNullOrWhiteSpace(item.SenderEmail) && item.SenderEmail.Contains("@"))
            return item.SenderEmail;

        var participant = _callStore.Current.Participants.FirstOrDefault(x =>
            string.Equals(x.PeerId, item.SenderPeerId, StringComparison.Ordinal));

        if (participant != null)
            return UserFacingIdentityFormatter.ResolveParticipantLabel(participant.PeerId, participant.UserId);
        if (!string.IsNullOrWhiteSpace(item.SenderUserId))
        {
            participant = _callStore.Current.Participants.FirstOrDefault(x =>
                string.Equals(x.UserId, item.SenderUserId, StringComparison.Ordinal));

            if (participant != null)
                return UserFacingIdentityFormatter.ResolveParticipantLabel(participant.PeerId, participant.UserId);
        }

        return UserFacingIdentityFormatter.ResolveParticipantLabel(item.SenderPeerId, item.SenderUserId);
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
}

public sealed class ConferenceParticipantTileViewItem
{
    public ConferenceParticipantTileViewItem(
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
