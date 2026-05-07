using MeetSpace.ViewModels.Temporary;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace MeetSpace.Views.Temporary;

public sealed partial class ConferenceRoomPage : Page
{
    private string _conferenceId = string.Empty;
    private CancellationTokenSource? _pageLifetimeCts;
    private UwpWebViewAudioBridgeHost? _audioBridgeHost;
    private WebView2? _mediaHostView;
    private bool _audioBridgeReady;
    private bool _scrollRequestPending;
    private bool _microphoneCommandInFlight;
    private bool _cameraCommandInFlight;
    private bool _screenShareCommandInFlight;
    private readonly SemaphoreSlim _audioBridgeInitSync = new SemaphoreSlim(1, 1);

    public ConferenceRoomPageViewModel ViewModel { get; }

    public ConferenceRoomPage()
    {
        var services = App.Current.Services;
        ViewModel = services.GetRequiredService<ConferenceRoomPageViewModel>();

        InitializeComponent();

        Loaded += ConferenceRoomPage_Loaded;
        Unloaded += ConferenceRoomPage_Unloaded;

        ViewModel.NavigateToLoginRequested += ViewModel_NavigateToLoginRequested;
        ViewModel.NavigateBackRequested += ViewModel_NavigateBackRequested;
        ViewModel.Messages.CollectionChanged += Messages_CollectionChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _conferenceId = e.Parameter as string ?? string.Empty;
        _audioBridgeReady = false;
        _microphoneCommandInFlight = false;
        _cameraCommandInFlight = false;
        _screenShareCommandInFlight = false;
    }

    private async void ConferenceRoomPage_Loaded(object sender, RoutedEventArgs e)
    {
        _pageLifetimeCts?.Cancel();
        _pageLifetimeCts?.Dispose();
        _pageLifetimeCts = new CancellationTokenSource();

        _audioBridgeReady = false;

        try
        {
            await ViewModel.ActivateAsync(_conferenceId, Dispatcher, _pageLifetimeCts.Token);
            var token = _pageLifetimeCts?.Token ?? CancellationToken.None;
            await EnsureAudioBridgeReadyAsync(token);
            await ViewModel.EnsureConferenceAudioStartedAsync(token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (IsDisposedBridgeError(ex))
        {
            try
            {
                var token = _pageLifetimeCts?.Token ?? CancellationToken.None;
                _audioBridgeReady = false;
                await EnsureAudioBridgeReadyAsync(token);
                await ViewModel.EnsureConferenceAudioStartedAsync(token);
            }
            catch (Exception retryEx)
            {
                ViewModel.SetStatusMessage("audio bridge init failed: " + retryEx.Message);
            }
        }
        catch (Exception ex)
        {
            ViewModel.SetStatusMessage("room init failed: " + ex.Message);
        }
    }

    private async void ConferenceRoomPage_Unloaded(object sender, RoutedEventArgs e)
    {
        var lifetime = _pageLifetimeCts;
        _pageLifetimeCts = null;

        try
        {
            lifetime?.Cancel();
        }
        catch
        {
        }

        try
        {
            await ViewModel.DeactivateAsync();
        }
        catch
        {
        }

        try
        {
            lifetime?.Dispose();
        }
        catch
        {
        }

        _audioBridgeReady = false;

        try
        {
            _audioBridgeHost?.Dispose();
        }
        catch
        {
        }

        _audioBridgeHost = null;

        try
        {
            if (_mediaHostView != null)
            {
                MediaHostContainer.Children.Clear();
                _mediaHostView = null;
            }
        }
        catch
        {
        }
    }

    private async void ViewModel_NavigateToLoginRequested(object? sender, EventArgs e)
    {
        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            Frame?.Navigate(typeof(LoginPage));
        });
    }

    private async void ViewModel_NavigateBackRequested(object? sender, EventArgs e)
    {
        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
        });
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RequestScrollMessagesToEnd();
    }

    private void RequestScrollMessagesToEnd()
    {
        if (_scrollRequestPending)
            return;

        _scrollRequestPending = true;
        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
        {
            _scrollRequestPending = false;
            TryScrollMessagesToEnd();
        });
    }

    private void TryScrollMessagesToEnd()
    {
        if (!IsLoaded || MessagesList == null || MessagesList.ActualHeight <= 0)
            return;

        var count = ViewModel.Messages.Count;
        if (count <= 0)
            return;

        var lastItem = ViewModel.Messages[count - 1];
        if (!MessagesList.Items.Contains(lastItem))
            return;

        try
        {
            MessagesList.UpdateLayout();
            MessagesList.ScrollIntoView(lastItem);
        }
        catch
        {
        }
    }

    private void EnsureMediaHostViewCreated()
    {
        if (_mediaHostView != null)
            return;

        var webView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        MediaHostContainer.Children.Clear();
        MediaHostContainer.Children.Add(webView);
        _mediaHostView = webView;
    }

    private async Task EnsureAudioBridgeReadyAsync(CancellationToken cancellationToken)
    {
        await _audioBridgeInitSync.WaitAsync(cancellationToken);
        try
        {
            if (_audioBridgeReady && _audioBridgeHost != null && !_audioBridgeHost.IsDisposed)
                return;

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, EnsureMediaHostViewCreated);

            if (_mediaHostView == null)
                throw new InvalidOperationException("MediaHostView was not created.");

            try
            {
                _audioBridgeHost?.Dispose();
            }
            catch
            {
            }

            _audioBridgeHost = new UwpWebViewAudioBridgeHost(_mediaHostView);
            await ViewModel.AttachAudioHostAsync(_audioBridgeHost, cancellationToken);

            _audioBridgeReady = true;
        }
        finally
        {
            _audioBridgeInitSync.Release();
        }
    }


    private async Task FocusMediaHostForCaptureAsync()
    {
        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            try
            {
                if (_mediaHostView == null)
                    return;

                _ = _mediaHostView.Focus(FocusState.Programmatic);
                try
                {
                    _ = _mediaHostView.CoreWebView2?.ExecuteScriptAsync(
                        "try{window.focus();true;}catch(_){false;}");
                }
                catch
                {
                }
            }
            catch
            {
            }
        });
    }

    private void OpenChatButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenChatPanel();
    }

    private void CloseChatButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseChatPanel();
    }

    private async void SendMessageButton_Click(object sender, RoutedEventArgs e)
    {
        var sent = await ViewModel.SendMessageAsync(MessageTextBox.Text);
        if (sent)
            MessageTextBox.Text = string.Empty;
    }

    private async void MicrophoneButton_Click(object sender, RoutedEventArgs e)
    {
        if (_microphoneCommandInFlight)
            return;

        _microphoneCommandInFlight = true;
        var token = _pageLifetimeCts != null ? _pageLifetimeCts.Token : CancellationToken.None;

        try
        {
            if (!_audioBridgeReady || _audioBridgeHost == null || _audioBridgeHost.IsDisposed)
                await EnsureAudioBridgeReadyAsync(token);

            await ViewModel.HandleMicrophoneAsync(token);
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
                await ViewModel.HandleMicrophoneAsync(token);
            }
            catch (Exception retryEx)
            {
                ViewModel.SetStatusMessage("audio bridge init failed: " + retryEx.Message);
            }
        }
        catch (Exception ex)
        {
            ViewModel.SetStatusMessage("audio bridge init failed: " + ex.Message);
        }
        finally
        {
            _microphoneCommandInFlight = false;
        }
    }

    private async void CameraButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cameraCommandInFlight)
            return;

        _cameraCommandInFlight = true;
        var token = _pageLifetimeCts != null ? _pageLifetimeCts.Token : CancellationToken.None;

        try
        {
            if (!_audioBridgeReady || _audioBridgeHost == null || _audioBridgeHost.IsDisposed)
                await EnsureAudioBridgeReadyAsync(token);

            await ViewModel.HandleCameraAsync(token);
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
                await ViewModel.HandleCameraAsync(token);
            }
            catch (Exception retryEx)
            {
                ViewModel.SetStatusMessage("camera bridge init failed: " + retryEx.Message);
            }
        }
        catch (Exception ex)
        {
            ViewModel.SetStatusMessage("camera bridge init failed: " + ex.Message);
        }
        finally
        {
            _cameraCommandInFlight = false;
        }
    }

    private async void ScreenShareButton_Click(object sender, RoutedEventArgs e)
    {
        if (_screenShareCommandInFlight)
            return;

        _screenShareCommandInFlight = true;
        var token = _pageLifetimeCts != null ? _pageLifetimeCts.Token : CancellationToken.None;

        try
        {
            if (!_audioBridgeReady || _audioBridgeHost == null || _audioBridgeHost.IsDisposed)
                await EnsureAudioBridgeReadyAsync(token);
            await FocusMediaHostForCaptureAsync();

            await ViewModel.HandleScreenShareAsync(token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (IsScreenShareCancelledError(ex))
        {
            ViewModel.SetStatusMessage("Вы отменили выбор экрана.");
        }
        catch (Exception ex) when (IsDisposedBridgeError(ex))
        {
            try
            {
                _audioBridgeReady = false;
                await EnsureAudioBridgeReadyAsync(token);
                await ViewModel.HandleScreenShareAsync(token);
            }
            catch (Exception retryEx)
            {
                ViewModel.SetStatusMessage("screen bridge init failed: " + retryEx.Message);
            }
        }
        catch (Exception ex)
        {
            ViewModel.SetStatusMessage("screen bridge init failed: " + ex.Message);
        }
        finally
        {
            _screenShareCommandInFlight = false;
        }
    }

    private async void LeaveConferenceButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _pageLifetimeCts?.Cancel();
        }
        catch
        {
        }

        await ViewModel.LeaveConferenceAsync();
    }

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
