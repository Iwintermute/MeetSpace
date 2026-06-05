using MeetSpace.Mobile.Services;
using MeetSpace.Mobile.ViewModels;
using Microsoft.Maui.ApplicationModel;
using System.Collections.Specialized;

namespace MeetSpace.Mobile.Pages;

public partial class ConferenceRoomPage : ContentPage
{
	private readonly ConferenceRoomPageViewModel _viewModel;
	private readonly MeetSpaceMobileRuntime _runtime;
	private string _conferenceId = string.Empty;
	private CancellationTokenSource? _pageLifetimeCts;
	private MauiWebViewAudioBridgeHost? _audioBridgeHost;
	private bool _audioBridgeReady;
	private bool _microphoneCommandInFlight;
	private bool _cameraCommandInFlight;
	private bool _screenShareCommandInFlight;
	private bool _leaveCommandInFlight;
	private readonly SemaphoreSlim _audioBridgeInitSync = new(1, 1);
	private bool _activated;

	public ConferenceRoomPage(
		ConferenceRoomPageViewModel viewModel,
		MeetSpaceMobileRuntime runtime)
	{
		InitializeComponent();
		_viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
		BindingContext = _viewModel;

		_viewModel.NavigateToLoginRequested += ViewModel_NavigateToLoginRequested;
		_viewModel.NavigateBackRequested += ViewModel_NavigateBackRequested;
		_viewModel.Messages.CollectionChanged += Messages_CollectionChanged;
	}

	public void Prepare(string conferenceId)
	{
		_conferenceId = conferenceId ?? string.Empty;
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
		_leaveCommandInFlight = false;

		var token = CurrentToken;
		try
		{
			await _runtime.InitializeAsync(token);
			await EnsureMediaPermissionsAsync();

			if (!_activated)
			{
				_activated = true;
				await _viewModel.ActivateAsync(_conferenceId, Dispatcher, token);
			}

			await EnsureAudioBridgeReadyAsync(token);
			await _viewModel.EnsureConferenceAudioStartedAsync(token);
			RequestScrollMessagesToEnd();
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
				await _viewModel.EnsureConferenceAudioStartedAsync(token);
			}
			catch (Exception retryEx)
			{
				_viewModel.SetStatusMessage("audio bridge init failed: " + retryEx.Message);
			}
		}
		catch (Exception ex)
		{
			_viewModel.SetStatusMessage("room init failed: " + ex.Message);
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

	private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		RequestScrollMessagesToEnd();
	}

	private void RequestScrollMessagesToEnd()
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			TryScrollMessagesToEnd();
		});
	}

	private void TryScrollMessagesToEnd()
	{
		var count = _viewModel.Messages.Count;
		if (count <= 0)
			return;

		try
		{
			MessagesList.ScrollTo(count - 1, position: ScrollToPosition.End, animate: true);
		}
		catch
		{
		}
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

	private async void OpenChatButton_Clicked(object? sender, EventArgs e)
	{
		_viewModel.OpenChatPanel();
		await Task.Delay(50);
		RequestScrollMessagesToEnd();
	}

	private void CloseChatButton_Clicked(object? sender, EventArgs e)
	{
		_viewModel.CloseChatPanel();
	}

	private async void SendMessageButton_Clicked(object? sender, EventArgs e)
	{
		var sent = await _viewModel.SendMessageAsync(MessageEditor.Text);
		if (sent)
		{
			MessageEditor.Text = string.Empty;
			RequestScrollMessagesToEnd();
		}
	}

	private async void MicrophoneButton_Clicked(object? sender, EventArgs e)
	{
		if (_microphoneCommandInFlight)
			return;

		_microphoneCommandInFlight = true;
		try
		{
			if (!_audioBridgeReady || _audioBridgeHost == null || _audioBridgeHost.IsDisposed)
				await EnsureAudioBridgeReadyAsync(CurrentToken);

			await _viewModel.HandleMicrophoneAsync(CurrentToken);
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
				await _viewModel.HandleMicrophoneAsync(CurrentToken);
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
			if (!_audioBridgeReady || _audioBridgeHost == null || _audioBridgeHost.IsDisposed)
				await EnsureAudioBridgeReadyAsync(CurrentToken);

			await _viewModel.HandleCameraAsync(CurrentToken);
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
				await _viewModel.HandleCameraAsync(CurrentToken);
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
			if (!_audioBridgeReady || _audioBridgeHost == null || _audioBridgeHost.IsDisposed)
				await EnsureAudioBridgeReadyAsync(CurrentToken);

			try
			{
				MediaHostView.Focus();
			}
			catch
			{
			}

			await _viewModel.HandleScreenShareAsync(CurrentToken);
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
				await _viewModel.HandleScreenShareAsync(CurrentToken);
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

	private async void LeaveConferenceButton_Clicked(object? sender, EventArgs e)
	{
		if (_leaveCommandInFlight)
			return;

		_leaveCommandInFlight = true;
		try
		{
			_pageLifetimeCts?.Cancel();
			await _viewModel.LeaveConferenceAsync();
		}
		catch
		{
		}
		finally
		{
			_leaveCommandInFlight = false;
		}
	}

	private async void ViewModel_NavigateToLoginRequested(object? sender, EventArgs e)
	{
		if (Application.Current is App app)
			await app.NavigateToLoginAsync();
	}

	private async void ViewModel_NavigateBackRequested(object? sender, EventArgs e)
	{
		if (Navigation.NavigationStack.Count > 1)
			await Navigation.PopAsync();
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
