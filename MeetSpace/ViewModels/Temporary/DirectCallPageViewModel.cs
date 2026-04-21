using CommunityToolkit.Mvvm.ComponentModel;
using MeetSpace.Client.App.Auth;
using MeetSpace.Client.App.Calls;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Domain.Calls;
using MeetSpace.Client.Shared.Configuration;
using MeetSpace.Client.Shared.Results;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Core;

namespace MeetSpace.ViewModels.Temporary;

public sealed class DirectCallPageViewModel : ObservableObject
{
    private readonly CallCoordinator _callCoordinator;
    private readonly CallStore _callStore;
    private readonly AuthSessionStore _authStore;
    private readonly SessionStore _sessionStore;
    private readonly RealtimeStartupService _realtimeStartupService;
    private readonly ClientRuntimeOptions _options;

    private CoreDispatcher? _dispatcher;
    private bool _isActive;
    private string _callId = string.Empty;
    private string? _counterpartUserId;
    private string _counterpartTitle = "Пользователь";
    private bool _incomingCallMode;
    private bool _autoEnableCamera;
    private bool _startAttempted;
    private bool _callJoinSucceeded;
    private bool _isEndingCall;
    private bool _hasNavigatedBackAfterEnd;

    private string _callTitle = "Личный звонок";
    private string _callSubtitle = string.Empty;
    private string _callStatusText = "Ожидание подключения…";
    private string _statusDetailsText = "Защищенное соединение";
    private string _microphoneButtonContent = "Микрофон выкл";
    private string _cameraButtonContent = "Камера выкл";
    private string _screenShareButtonContent = "Экран выкл";
    private bool _isMicrophoneButtonEnabled;
    private bool _isCameraButtonEnabled;
    private bool _isScreenShareButtonEnabled;
    private bool _isEndCallEnabled;

    public DirectCallPageViewModel(
        CallCoordinator callCoordinator,
        CallStore callStore,
        AuthSessionStore authStore,
        SessionStore sessionStore,
        RealtimeStartupService realtimeStartupService,
        ClientRuntimeOptions options)
    {
        _callCoordinator = callCoordinator;
        _callStore = callStore;
        _authStore = authStore;
        _sessionStore = sessionStore;
        _realtimeStartupService = realtimeStartupService;
        _options = options;
    }

    public ObservableCollection<DirectCallParticipantViewItem> Participants { get; } = new();

    public string CallTitle
    {
        get => _callTitle;
        private set => SetProperty(ref _callTitle, value);
    }

    public string CallSubtitle
    {
        get => _callSubtitle;
        private set => SetProperty(ref _callSubtitle, value);
    }

    public string CallStatusText
    {
        get => _callStatusText;
        private set => SetProperty(ref _callStatusText, value);
    }

    public string StatusDetailsText
    {
        get => _statusDetailsText;
        private set => SetProperty(ref _statusDetailsText, value);
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

    public bool IsEndCallEnabled
    {
        get => _isEndCallEnabled;
        private set => SetProperty(ref _isEndCallEnabled, value);
    }

    public event EventHandler? NavigateBackRequested;
    public event EventHandler? NavigateToLoginRequested;

    public async Task ActivateAsync(DirectCallNavigationRequest request, CoreDispatcher dispatcher)
    {
        if (_isActive)
            return;

        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _isActive = true;

        _callId = request.CallId ?? string.Empty;
        _counterpartUserId = request.CounterpartUserId;
        _counterpartTitle = string.IsNullOrWhiteSpace(request.CounterpartTitle)
            ? "Пользователь"
            : request.CounterpartTitle;
        _incomingCallMode = request.IsIncoming;
        _autoEnableCamera = request.AutoEnableCamera;
        _startAttempted = false;
        _callJoinSucceeded = false;
        _isEndingCall = false;
        _hasNavigatedBackAfterEnd = false;

        _callStore.StateChanged += CallStore_StateChanged;
        _authStore.StateChanged += AuthStore_StateChanged;
        _sessionStore.StateChanged += SessionStore_StateChanged;

        await RunOnUiThreadAsync(() =>
        {
            ApplyIdentity();
            ApplyCallState(_callStore.Current);
        }).ConfigureAwait(false);

        var authorized = await EnsureAuthorizedAsync().ConfigureAwait(false);
        if (!authorized)
            await RunOnUiThreadAsync(RaiseNavigateToLogin).ConfigureAwait(false);
    }

    public async Task DeactivateAsync()
    {
        if (!_isActive)
            return;

        _isActive = false;

        _callStore.StateChanged -= CallStore_StateChanged;
        _authStore.StateChanged -= AuthStore_StateChanged;
        _sessionStore.StateChanged -= SessionStore_StateChanged;
        _dispatcher = null;

        if (_isEndingCall || string.IsNullOrWhiteSpace(_callId))
            return;

        try
        {
            var state = _callStore.Current;
            if (state.Kind == CallKind.Direct &&
                string.Equals(state.SessionId, _callId, StringComparison.Ordinal) &&
                state.Stage != CallConnectionStage.Idle)
            {
                await _callCoordinator.EndDirectCallAsync(_callId, "page_unload", CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
        }
    }

    public async Task AttachAudioHostAsync(IAudioBridgeHost host, CancellationToken cancellationToken)
    {
        await _callCoordinator.AttachHostAsync(host, cancellationToken).ConfigureAwait(false);
        await RunOnUiThreadAsync(() => ApplyCallState(_callStore.Current)).ConfigureAwait(false);
    }

    public async Task EnsureCallStartedAsync(CancellationToken cancellationToken)
    {
        if (_startAttempted)
            return;

        _startAttempted = true;
        if (string.IsNullOrWhiteSpace(_callId))
        {
            await RunOnUiThreadAsync(() =>
            {
                CallStatusText = "Ошибка звонка";
                StatusDetailsText = "Идентификатор звонка отсутствует.";
            }).ConfigureAwait(false);
            return;
        }
        Result result;

        if (_incomingCallMode)
            result = await _callCoordinator.AcceptAndJoinDirectCallAsync(_callId, cancellationToken).ConfigureAwait(false);
        else
            result = await _callCoordinator.JoinDirectCallMediaAsync(_callId, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            await RunOnUiThreadAsync(() =>
            {
                StatusDetailsText = result.Error?.Message ?? "Не удалось подключиться к звонку.";
                CallStatusText = "Ошибка подключения";
            }).ConfigureAwait(false);
            return;
        }

        _callJoinSucceeded = true;

        if (_autoEnableCamera)
        {
            var toggleCameraResult = await _callCoordinator.ToggleCameraAsync(cancellationToken).ConfigureAwait(false);
            if (toggleCameraResult.IsFailure && !cancellationToken.IsCancellationRequested)
            {
                await RunOnUiThreadAsync(() =>
                {
                    StatusDetailsText = toggleCameraResult.Error?.Message ?? "Не удалось включить камеру.";
                }).ConfigureAwait(false);
            }
        }
    }

    public async Task ToggleMicrophoneAsync(CancellationToken cancellationToken = default)
    {
        var result = await _callCoordinator.ToggleMicrophoneAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsFailure && !cancellationToken.IsCancellationRequested)
        {
            await RunOnUiThreadAsync(() =>
            {
                StatusDetailsText = result.Error?.Message ?? "Не удалось переключить микрофон.";
            }).ConfigureAwait(false);
        }
    }

    public async Task ToggleCameraAsync(CancellationToken cancellationToken = default)
    {
        var result = await _callCoordinator.ToggleCameraAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsFailure && !cancellationToken.IsCancellationRequested)
        {
            await RunOnUiThreadAsync(() =>
            {
                StatusDetailsText = result.Error?.Message ?? "Не удалось переключить камеру.";
            }).ConfigureAwait(false);
        }
    }

    public async Task ToggleScreenShareAsync(CancellationToken cancellationToken = default)
    {
        var result = await _callCoordinator.ToggleScreenShareAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsFailure && !cancellationToken.IsCancellationRequested)
        {
            await RunOnUiThreadAsync(() =>
            {
                StatusDetailsText = result.Error?.Message ?? "Не удалось переключить демонстрацию экрана.";
            }).ConfigureAwait(false);
        }
    }

    public async Task EndCallAsync(CancellationToken cancellationToken = default)
    {
        _isEndingCall = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(_callId))
            {
                var endResult = await _callCoordinator.EndDirectCallAsync(_callId, "client_hangup", cancellationToken).ConfigureAwait(false);
                if (endResult.IsFailure && !cancellationToken.IsCancellationRequested)
                {
                    await RunOnUiThreadAsync(() =>
                    {
                        StatusDetailsText = endResult.Error?.Message ?? "Не удалось завершить звонок.";
                    }).ConfigureAwait(false);
                }
            }
            else
            {
                await _callCoordinator.LeaveAudioAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await RunOnUiThreadAsync(() =>
            {
                if (!_hasNavigatedBackAfterEnd)
                {
                    _hasNavigatedBackAfterEnd = true;
                    NavigateBackRequested?.Invoke(this, EventArgs.Empty);
                }
            }).ConfigureAwait(false);
        }
    }

    private async void CallStore_StateChanged(object sender, CallSessionState state)
    {
        if (!_isActive)
            return;

        await RunOnUiThreadAsync(() =>
        {
            ApplyCallState(state);
        }).ConfigureAwait(false);

        if (_callJoinSucceeded &&
            !_isEndingCall &&
            !_hasNavigatedBackAfterEnd &&
            state.Stage == CallConnectionStage.Idle)
        {
            await RunOnUiThreadAsync(() =>
            {
                _hasNavigatedBackAfterEnd = true;
                NavigateBackRequested?.Invoke(this, EventArgs.Empty);
            }).ConfigureAwait(false);
        }
    }

    private async void AuthStore_StateChanged(object sender, AuthSessionState state)
    {
        if (!_isActive)
            return;

        if (!state.IsAuthenticated)
            await RunOnUiThreadAsync(RaiseNavigateToLogin).ConfigureAwait(false);
    }

    private async void SessionStore_StateChanged(object sender, SessionState state)
    {
        if (!_isActive)
            return;

        await RunOnUiThreadAsync(ApplyIdentity).ConfigureAwait(false);
    }

    private void ApplyIdentity()
    {
        CallTitle = string.IsNullOrWhiteSpace(_counterpartTitle) ? "Личный звонок" : _counterpartTitle;
        var fallbackSubtitle = !string.IsNullOrWhiteSpace(_counterpartUserId) &&
                               _counterpartUserId!.Contains("@", StringComparison.Ordinal)
            ? _counterpartUserId
            : "Личный защищенный звонок";

        CallSubtitle = fallbackSubtitle;
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
            IsEndCallEnabled = true;

            MicrophoneButtonContent = "Подключение…";
            CameraButtonContent = "Подключение…";
            ScreenShareButtonContent = "Подключение…";
        }
        else if (state.Stage == CallConnectionStage.Connected)
        {
            IsMicrophoneButtonEnabled = true;
            IsCameraButtonEnabled = true;
            IsScreenShareButtonEnabled = true;
            IsEndCallEnabled = true;

            MicrophoneButtonContent = state.LocalMedia.MicrophoneEnabled ? "Микрофон вкл" : "Микрофон выкл";
            CameraButtonContent = state.LocalMedia.CameraEnabled ? "Камера вкл" : "Камера выкл";
            ScreenShareButtonContent = state.LocalMedia.ScreenShareEnabled ? "Экран вкл" : "Экран выкл";
        }
        else
        {
            IsMicrophoneButtonEnabled = false;
            IsCameraButtonEnabled = false;
            IsScreenShareButtonEnabled = false;
            IsEndCallEnabled = _startAttempted;

            MicrophoneButtonContent = "Микрофон";
            CameraButtonContent = "Камера";
            ScreenShareButtonContent = "Экран";
        }

        CallStatusText = state.Stage switch
        {
            CallConnectionStage.Idle => _startAttempted ? "Звонок завершён" : "Ожидание звонка…",
            CallConnectionStage.JoiningRoom => "Подключение к звонку…",
            CallConnectionStage.TransportOpening => "Открытие транспорта…",
            CallConnectionStage.Negotiating => "Согласование медиа…",
            CallConnectionStage.Publishing => "Публикация треков…",
            CallConnectionStage.Connected => "Вы в звонке",
            CallConnectionStage.Faulted => "Ошибка звонка",
            _ => state.Stage.ToString()
        };

        Participants.Clear();
        foreach (var participant in state.Participants.OrderBy(x => x.PeerId, StringComparer.Ordinal))
        {
            var title = UserFacingIdentityFormatter.ResolveParticipantLabel(participant.PeerId, participant.UserId);
            Participants.Add(new DirectCallParticipantViewItem(
                title,
                participant.HasAudio,
                participant.HasVideo,
                participant.HasScreenShare));
        }

        if (Participants.Count == 0 && !string.IsNullOrWhiteSpace(_counterpartTitle))
        {
            Participants.Add(new DirectCallParticipantViewItem(_counterpartTitle, false, false, false));
        }
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
                return false;
        }
        catch
        {
            return false;
        }

        return true;
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
