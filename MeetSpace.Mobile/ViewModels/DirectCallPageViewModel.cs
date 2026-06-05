using CommunityToolkit.Mvvm.ComponentModel;
using MeetSpace.Client.App.Auth;
using MeetSpace.Client.App.Calls;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Domain.Calls;
using MeetSpace.Client.Shared.Configuration;
using MeetSpace.Client.Shared.Results;
using Microsoft.Maui.Dispatching;
using System.Collections.ObjectModel;

namespace MeetSpace.Mobile.ViewModels;

public sealed class DirectCallPageViewModel : ObservableObject
{
	private readonly CallCoordinator _callCoordinator;
	private readonly CallStore _callStore;
	private readonly AuthSessionStore _authStore;
	private readonly SessionStore _sessionStore;
	private readonly RealtimeStartupService _realtimeStartupService;
	private readonly ClientRuntimeOptions _options;

	private IDispatcher? _dispatcher;
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

	public async Task ActivateAsync(DirectCallNavigationRequest request, IDispatcher dispatcher)
	{
		if (_isActive)
			return;

		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_isActive = true;

		_callId = request.CallId ?? string.Empty;
		_counterpartUserId = request.CounterpartUserId;
		_counterpartTitle = UserFacingIdentityFormatter.ResolveUserLabel(
			request.CounterpartUserId,
			request.CounterpartTitle,
			null);
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
		{
			result = await _callCoordinator.AcceptAndJoinDirectCallAsync(_callId, cancellationToken).ConfigureAwait(false);
		}
		else
		{
			await RunOnUiThreadAsync(() =>
			{
				CallStatusText = "Вызов…";
				StatusDetailsText = "Ожидание ответа собеседника…";
			}).ConfigureAwait(false);

			var accepted = await WaitForCallAcceptedAsync(cancellationToken).ConfigureAwait(false);
			if (!accepted)
			{
				await RunOnUiThreadAsync(() =>
				{
					CallStatusText = "Нет ответа";
					StatusDetailsText = "Собеседник не принял звонок.";
				}).ConfigureAwait(false);

				_ = Task.Delay(2000, CancellationToken.None).ContinueWith(_ =>
				{
					_ = RunOnUiThreadAsync(() =>
					{
						if (!_hasNavigatedBackAfterEnd)
						{
							_hasNavigatedBackAfterEnd = true;
							NavigateBackRequested?.Invoke(this, EventArgs.Empty);
						}
					});
				}, TaskScheduler.Default);
				return;
			}

			result = await _callCoordinator.JoinDirectCallMediaAsync(_callId, cancellationToken).ConfigureAwait(false);
		}

		if (result.IsFailure)
		{
			if (IsBridgeDisposedFailure(result.Error))
			{
				_startAttempted = false;
				throw new InvalidOperationException(result.Error?.Message ?? "Audio bridge host was disposed.");
			}
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
			if (toggleCameraResult.IsFailure)
			{
				if (IsBridgeDisposedFailure(toggleCameraResult.Error))
					throw new InvalidOperationException(toggleCameraResult.Error?.Message ?? "Audio bridge host was disposed.");

				if (cancellationToken.IsCancellationRequested)
					return;
				await RunOnUiThreadAsync(() =>
				{
					StatusDetailsText = toggleCameraResult.Error?.Message ?? "Не удалось включить камеру.";
				}).ConfigureAwait(false);
			}
		}
	}

	private async Task<bool> WaitForCallAcceptedAsync(CancellationToken cancellationToken)
	{
		var acceptTimeout = TimeSpan.FromSeconds(60);
		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(acceptTimeout);

		try
		{
			while (!timeoutCts.Token.IsCancellationRequested)
			{
				var state = _callStore.Current;

				if (state.Stage == CallConnectionStage.Idle)
					return false;

				if (state.Stage == CallConnectionStage.Connected ||
					state.Stage == CallConnectionStage.Negotiating ||
					state.Stage == CallConnectionStage.TransportOpening ||
					state.Stage == CallConnectionStage.Publishing)
				{
					return true;
				}

				var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

				void OnStateChanged(object s, CallSessionState st)
				{
					tcs.TrySetResult(true);
				}

				_callStore.StateChanged += OnStateChanged;
				try
				{
					using (timeoutCts.Token.Register(() => tcs.TrySetCanceled()))
					{
						await tcs.Task.ConfigureAwait(false);
					}
				}
				catch (OperationCanceledException)
				{
					return false;
				}
				finally
				{
					_callStore.StateChanged -= OnStateChanged;
				}
			}
		}
		catch (OperationCanceledException)
		{
		}

		return false;
	}

	public async Task ToggleMicrophoneAsync(CancellationToken cancellationToken = default)
	{
		var connected = await EnsureDirectCallMediaConnectedAsync(cancellationToken).ConfigureAwait(false);
		if (!connected)
			return;
		var result = await _callCoordinator.ToggleMicrophoneAsync(cancellationToken).ConfigureAwait(false);
		if (result.IsFailure)
		{
			if (IsBridgeDisposedFailure(result.Error))
				throw new InvalidOperationException(result.Error?.Message ?? "Audio bridge host was disposed.");

			if (cancellationToken.IsCancellationRequested)
				return;
			await RunOnUiThreadAsync(() =>
			{
				StatusDetailsText = result.Error?.Message ?? "Не удалось переключить микрофон.";
			}).ConfigureAwait(false);
		}
	}

	public async Task ToggleCameraAsync(CancellationToken cancellationToken = default)
	{
		var connected = await EnsureDirectCallMediaConnectedAsync(cancellationToken).ConfigureAwait(false);
		if (!connected)
			return;
		var result = await _callCoordinator.ToggleCameraAsync(cancellationToken).ConfigureAwait(false);
		if (result.IsFailure)
		{
			if (IsBridgeDisposedFailure(result.Error))
				throw new InvalidOperationException(result.Error?.Message ?? "Audio bridge host was disposed.");

			if (cancellationToken.IsCancellationRequested)
				return;
			await RunOnUiThreadAsync(() =>
			{
				StatusDetailsText = result.Error?.Message ?? "Не удалось переключить камеру.";
			}).ConfigureAwait(false);
		}
	}

	public async Task ToggleScreenShareAsync(CancellationToken cancellationToken = default)
	{
		var connected = await EnsureDirectCallMediaConnectedAsync(cancellationToken).ConfigureAwait(false);
		if (!connected)
			return;
		var result = await _callCoordinator.ToggleScreenShareAsync(cancellationToken).ConfigureAwait(false);
		if (result.IsFailure)
		{
			if (IsBridgeDisposedFailure(result.Error))
				throw new InvalidOperationException(result.Error?.Message ?? "Audio bridge host was disposed.");
			if (IsScreenShareCancelledFailure(result.Error))
				throw new InvalidOperationException(result.Error?.Message ?? "Screen sharing was cancelled.");

			if (cancellationToken.IsCancellationRequested)
				return;
			await RunOnUiThreadAsync(() =>
			{
				StatusDetailsText = result.Error?.Message ?? "Не удалось переключить демонстрацию экрана.";
			}).ConfigureAwait(false);
		}
	}

	public void SetStatusMessage(string? message)
	{
		if (string.IsNullOrWhiteSpace(message))
			return;

		StatusDetailsText = message.Trim();
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
		CallTitle = UserFacingIdentityFormatter.ResolveUserLabel(
			_counterpartUserId,
			_counterpartTitle,
			null,
			"Личный звонок");
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
			var canRecover = _startAttempted && !_isEndingCall && !string.IsNullOrWhiteSpace(_callId);
			IsMicrophoneButtonEnabled = canRecover;
			IsCameraButtonEnabled = canRecover;
			IsScreenShareButtonEnabled = canRecover;
			IsEndCallEnabled = canRecover;

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
		foreach (var participant in state.Participants
					 .OrderBy(
						 x => UserFacingIdentityFormatter.ResolveParticipantLabel(x.PeerId, x.UserId),
						 StringComparer.OrdinalIgnoreCase))
		{
			var title = ResolveParticipantTitle(participant);
			Participants.Add(new DirectCallParticipantViewItem(
				title,
				participant.HasAudio,
				participant.HasVideo,
				participant.HasScreenShare));
		}
		if (Participants.Count == 0)
		{
			var fallbackTitle = !string.IsNullOrWhiteSpace(_counterpartTitle) &&
								!UserFacingIdentityFormatter.LooksLikeTechnicalId(_counterpartTitle) &&
								!string.Equals(_counterpartTitle, "Пользователь", StringComparison.OrdinalIgnoreCase)
				? _counterpartTitle
				: "Собеседник";

			Participants.Add(new DirectCallParticipantViewItem(fallbackTitle, false, false, false));
		}
	}

	private string ResolveParticipantTitle(RemoteParticipantState participant)
	{
		var resolved = UserFacingIdentityFormatter.ResolveParticipantLabel(participant.PeerId, participant.UserId);
		if (!string.Equals(resolved, "Участник", StringComparison.OrdinalIgnoreCase) &&
			!UserFacingIdentityFormatter.LooksLikeTechnicalId(resolved))
		{
			return resolved;
		}

		if (!string.IsNullOrWhiteSpace(_counterpartTitle) &&
			!UserFacingIdentityFormatter.LooksLikeTechnicalId(_counterpartTitle) &&
			!string.Equals(_counterpartTitle, "Пользователь", StringComparison.OrdinalIgnoreCase))
		{
			return _counterpartTitle;
		}

		return "Собеседник";
	}

	private async Task<bool> EnsureDirectCallMediaConnectedAsync(CancellationToken cancellationToken)
	{
		var stage = _callStore.Current.Stage;

		if (stage == CallConnectionStage.JoiningRoom ||
			stage == CallConnectionStage.TransportOpening ||
			stage == CallConnectionStage.Negotiating ||
			stage == CallConnectionStage.Publishing)
		{
			return false;
		}

		if (string.IsNullOrWhiteSpace(_callId))
			return false;

		var joinResult = await _callCoordinator.JoinDirectCallMediaAsync(_callId, cancellationToken).ConfigureAwait(false);
		if (joinResult.IsFailure)
		{
			if (IsBridgeDisposedFailure(joinResult.Error))
				throw new InvalidOperationException(joinResult.Error?.Message ?? "Audio bridge host was disposed.");

			if (!cancellationToken.IsCancellationRequested)
			{
				await RunOnUiThreadAsync(() =>
				{
					StatusDetailsText = joinResult.Error?.Message ?? "Не удалось подключиться к звонку.";
				}).ConfigureAwait(false);
			}

			return false;
		}

		_callJoinSucceeded = true;
		return true;
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
					StatusDetailsText = result.Error?.Message ?? "Не удалось подключить realtime.";
				}).ConfigureAwait(false);
				return false;
			}
		}
		catch (Exception ex)
		{
			await RunOnUiThreadAsync(() =>
			{
				StatusDetailsText = ex.Message;
			}).ConfigureAwait(false);
			return false;
		}

		return true;
	}

	private void RaiseNavigateToLogin()
	{
		NavigateToLoginRequested?.Invoke(this, EventArgs.Empty);
	}

	private static bool IsBridgeDisposedFailure(Error? error)
	{
		if (error == null)
			return false;

		if (string.Equals(error.Code, "call.bridge_disposed", StringComparison.OrdinalIgnoreCase))
			return true;

		return ContainsIgnoreCase(error.Message, "disposed") ||
			   ContainsIgnoreCase(error.Message, "reinitialization") ||
			   ContainsIgnoreCase(error.Message, "reinitialize");
	}

	private static bool IsScreenShareCancelledFailure(Error? error)
	{
		if (error == null)
			return false;
		return ContainsIgnoreCase(error.Message, "screen sharing was cancelled") ||
			   ContainsIgnoreCase(error.Message, "cancelled") ||
			   ContainsIgnoreCase(error.Message, "canceled") ||
			   ContainsIgnoreCase(error.Message, "permission denied for screen") ||
			   ContainsIgnoreCase(error.Message, "notallowederror") ||
			   ContainsIgnoreCase(error.Message, "aborterror");
	}

	private static bool ContainsIgnoreCase(string? value, string fragment)
	{
		return !string.IsNullOrWhiteSpace(value) &&
			   value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private Task RunOnUiThreadAsync(Action action)
	{
		var dispatcher = _dispatcher;
		if (dispatcher == null)
			return Task.CompletedTask;

		if (!dispatcher.IsDispatchRequired)
		{
			action();
			return Task.CompletedTask;
		}

		var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		dispatcher.Dispatch(() =>
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
