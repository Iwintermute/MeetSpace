using MeetSpace.Mobile.Services;
using MeetSpace.Mobile.ViewModels;
using Microsoft.Maui.ApplicationModel;

namespace MeetSpace.Mobile.Pages;

public partial class DirectCallPage : ContentPage
{
	private readonly DirectCallPageViewModel _viewModel;
	private readonly MeetSpaceMobileRuntime _runtime;
	private DirectCallNavigationRequest _navigationRequest = new(
		callId: string.Empty,
		counterpartUserId: null,
		counterpartTitle: "Личный звонок",
		isIncoming: false,
		autoEnableCamera: false);

	private CancellationTokenSource? _pageLifetimeCts;
	private MauiWebViewAudioBridgeHost? _audioBridgeHost;
	private bool _audioBridgeReady;
	private bool _microphoneCommandInFlight;
	private bool _cameraCommandInFlight;
	private bool _screenShareCommandInFlight;
	private bool _endCallCommandInFlight;
	private readonly SemaphoreSlim _audioBridgeInitSync = new(1, 1);
	private bool _activated;

	public DirectCallPage(
		DirectCallPageViewModel viewModel,
		MeetSpaceMobileRuntime runtime)
	{
		InitializeComponent();
		_viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		BindingContext = _viewModel;

		_viewModel.NavigateBackRequested += ViewModel_NavigateBackRequested;
		_viewModel.NavigateToLoginRequested += ViewModel_NavigateToLoginRequested;
	}

	public void Prepare(DirectCallNavigationRequest request)
	{
		_navigationRequest = request ?? throw new ArgumentNullException(nameof(request));
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		_pageLifetimeCts?.Cancel();
		_pageLifetimeCts?.Dispose();
		_pageLifetimeCts = new CancellationTokenSource();
		_audioBridgeReady = false;
		_microphoneCommandInFlight = false;
		_cameraCommandInFlight = false;
		_screenShareCommandInFlight = false;
		_endCallCommandInFlight = false;

		var token = CurrentToken;
		try
		{
			await _runtime.InitializeAsync(token);
			await EnsureMediaPermissionsAsync();

			if (!_activated)
			{
				_activated = true;
				await _viewModel.ActivateAsync(_navigationRequest, Dispatcher);
			}

			await EnsureAudioBridgeReadyAsync(token);
			await _viewModel.EnsureCallStartedAsync(token);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex) when (IsDisposedBridgeError(ex))
		{
			try
			{
				_audioBridgeReady = false;
				await EnsureAudioBridgeReadyAsync(token);
				await _viewModel.EnsureCallStartedAsync(token);
			}
			catch (Exception retryEx)
			{
				_viewModel.SetStatusMessage("audio bridge init failed: " + retryEx.Message);
			}
		}
		catch (Exception ex)
		{
			_viewModel.SetStatusMessage("call init failed: " + ex.Message);
		}
	}

	protected override async void OnDisappearing()
	{
		base.OnDisappearing();

		_pageLifetimeCts?.Cancel();
		_pageLifetimeCts?.Dispose();
		_pageLifetimeCts = null;

		try
		{
			await _viewModel.DeactivateAsync();
		}
		catch
		{
		}
		_activated = false;
		_audioBridgeReady = false;

		try
		{
			_audioBridgeHost?.Dispose();
		}
		catch
		{
		}

		_audioBridgeHost = null;
	}

	private async void MicrophoneButton_Clicked(object? sender, EventArgs e)
	{
		if (_microphoneCommandInFlight)
			return;

		_microphoneCommandInFlight = true;
		try
		{
			if (!_audioBridgeReady || _audioBridgeHost == null)
				await EnsureAudioBridgeReadyAsync(CurrentToken);
			await _viewModel.ToggleMicrophoneAsync(CurrentToken);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex) when (IsDisposedBridgeError(ex))
		{
			try
			{
				_audioBridgeReady = false;
				await EnsureAudioBridgeReadyAsync(CurrentToken);
				await _viewModel.ToggleMicrophoneAsync(CurrentToken);
			}
			catch (Exception retryEx)
			{
				_viewModel.SetStatusMessage("audio bridge init failed: " + retryEx.Message);
			}
		}
		catch (Exception ex)
		{
			_viewModel.SetStatusMessage("audio bridge init failed: " + ex.Message);
		}
		finally
		{
			_microphoneCommandInFlight = false;
		}
	}

	private async void CameraButton_Clicked(object? sender, EventArgs e)
	{
		if (_cameraCommandInFlight)
			return;

		_cameraCommandInFlight = true;
		try
		{
			if (!_audioBridgeReady || _audioBridgeHost == null)
				await EnsureAudioBridgeReadyAsync(CurrentToken);
			await _viewModel.ToggleCameraAsync(CurrentToken);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex) when (IsDisposedBridgeError(ex))
		{
			try
			{
				_audioBridgeReady = false;
				await EnsureAudioBridgeReadyAsync(CurrentToken);
				await _viewModel.ToggleCameraAsync(CurrentToken);
			}
			catch (Exception retryEx)
			{
				_viewModel.SetStatusMessage("camera bridge init failed: " + retryEx.Message);
			}
		}
		catch (Exception ex)
		{
			_viewModel.SetStatusMessage("camera bridge init failed: " + ex.Message);
		}
		finally
		{
			_cameraCommandInFlight = false;
		}
	}

	private async void ScreenShareButton_Clicked(object? sender, EventArgs e)
	{
		if (_screenShareCommandInFlight)
			return;

		_screenShareCommandInFlight = true;
		try
		{
			if (!_audioBridgeReady || _audioBridgeHost == null)
				await EnsureAudioBridgeReadyAsync(CurrentToken);
			await _viewModel.ToggleScreenShareAsync(CurrentToken);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex) when (IsScreenShareCancelledError(ex))
		{
			_viewModel.SetStatusMessage("Вы отменили выбор экрана.");
		}
		catch (Exception ex) when (IsDisposedBridgeError(ex))
		{
			try
			{
				_audioBridgeReady = false;
				await EnsureAudioBridgeReadyAsync(CurrentToken);
				await _viewModel.ToggleScreenShareAsync(CurrentToken);
			}
			catch (Exception retryEx)
			{
				_viewModel.SetStatusMessage("screen bridge init failed: " + retryEx.Message);
			}
		}
		catch (Exception ex)
		{
			_viewModel.SetStatusMessage("screen bridge init failed: " + ex.Message);
		}
		finally
		{
			_screenShareCommandInFlight = false;
		}
	}

	private async void EndCallButton_Clicked(object? sender, EventArgs e)
	{
		if (_endCallCommandInFlight)
			return;

		_endCallCommandInFlight = true;
		try
		{
			await _viewModel.EndCallAsync(CurrentToken);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			_viewModel.SetStatusMessage(ex.Message);
		}
		finally
		{
			_endCallCommandInFlight = false;
		}
	}

	private async void BackButton_Clicked(object? sender, EventArgs e)
	{
		if (Navigation.NavigationStack.Count > 1)
			await Navigation.PopAsync();
	}

	private async Task EnsureAudioBridgeReadyAsync(CancellationToken cancellationToken)
	{
		await _audioBridgeInitSync.WaitAsync(cancellationToken);
		try
		{
			if (_audioBridgeReady && _audioBridgeHost != null && !_audioBridgeHost.IsDisposed)
				return;

			try
			{
				_audioBridgeHost?.Dispose();
			}
			catch
			{
			}

			_audioBridgeHost = new MauiWebViewAudioBridgeHost(MediaHostView);
			await _viewModel.AttachAudioHostAsync(_audioBridgeHost, cancellationToken);
			_audioBridgeReady = true;
		}
		finally
		{
			_audioBridgeInitSync.Release();
		}
	}

	private async Task EnsureMediaPermissionsAsync()
	{
		try
		{
			await Permissions.RequestAsync<Permissions.Microphone>();
			await Permissions.RequestAsync<Permissions.Camera>();
		}
		catch
		{
		}
	}

	private async void ViewModel_NavigateBackRequested(object? sender, EventArgs e)
	{
		if (Navigation.NavigationStack.Count > 1)
			await Navigation.PopAsync();
	}

	private async void ViewModel_NavigateToLoginRequested(object? sender, EventArgs e)
	{
		if (Application.Current is App app)
			await app.NavigateToLoginAsync();
	}

	private CancellationToken CurrentToken => _pageLifetimeCts?.Token ?? CancellationToken.None;

	private static bool IsDisposedBridgeError(Exception ex)
	{
		for (var current = ex; current != null; current = current.InnerException)
		{
			if (current is ObjectDisposedException)
				return true;

			var message = current.Message;
			if (string.IsNullOrWhiteSpace(message))
				continue;

			if (message.IndexOf("disposed", StringComparison.OrdinalIgnoreCase) >= 0 ||
				message.IndexOf("reinitialization", StringComparison.OrdinalIgnoreCase) >= 0 ||
				message.IndexOf("reinitialize", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsScreenShareCancelledError(Exception ex)
	{
		for (var current = ex; current != null; current = current.InnerException)
		{
			var message = current.Message;
			if (string.IsNullOrWhiteSpace(message))
				continue;

			if (message.IndexOf("screen sharing was cancelled", StringComparison.OrdinalIgnoreCase) >= 0 ||
				message.IndexOf("cancelled", StringComparison.OrdinalIgnoreCase) >= 0 ||
				message.IndexOf("canceled", StringComparison.OrdinalIgnoreCase) >= 0 ||
				message.IndexOf("permission denied for screen", StringComparison.OrdinalIgnoreCase) >= 0 ||
				message.IndexOf("notallowederror", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}

		return false;
	}
}
