using MeetSpace.ViewModels.Temporary;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace MeetSpace.Views.Temporary;

public sealed partial class DirectCallPage : Page
{
    private DirectCallNavigationRequest _navigationRequest = new(
        callId: string.Empty,
        counterpartUserId: null,
        counterpartTitle: "Личный звонок",
        isIncoming: false,
        autoEnableCamera: false);

    private CancellationTokenSource? _pageLifetimeCts;
    private UwpWebViewAudioBridgeHost? _audioBridgeHost;
    private WebView2? _mediaHostView;
    private bool _audioBridgeReady;
    private bool _microphoneCommandInFlight;
    private bool _cameraCommandInFlight;
    private bool _screenShareCommandInFlight;
    private bool _endCallCommandInFlight;
    private readonly SemaphoreSlim _audioBridgeInitSync = new SemaphoreSlim(1, 1);

    public DirectCallPageViewModel ViewModel { get; }

    public DirectCallPage()
    {
        var services = App.Current.Services;
        ViewModel = services.GetRequiredService<DirectCallPageViewModel>();

        InitializeComponent();

        Loaded += DirectCallPage_Loaded;
        Unloaded += DirectCallPage_Unloaded;

        ViewModel.NavigateBackRequested += ViewModel_NavigateBackRequested;
        ViewModel.NavigateToLoginRequested += ViewModel_NavigateToLoginRequested;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is DirectCallNavigationRequest request)
            _navigationRequest = request;
    }

    private async void DirectCallPage_Loaded(object sender, RoutedEventArgs e)
    {
        _pageLifetimeCts?.Cancel();
        _pageLifetimeCts?.Dispose();
        _pageLifetimeCts = new CancellationTokenSource();
        _audioBridgeReady = false;
        _microphoneCommandInFlight = false;
        _cameraCommandInFlight = false;
        _screenShareCommandInFlight = false;
        _endCallCommandInFlight = false;

        var token = _pageLifetimeCts.Token;

        try
        {
            await ViewModel.ActivateAsync(_navigationRequest, Dispatcher);
            await EnsureAudioBridgeReadyAsync(token);
            await ViewModel.EnsureCallStartedAsync(token);
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
                await ViewModel.EnsureCallStartedAsync(token);
            }
            catch (Exception retryEx)
            {
                ViewModel.SetStatusMessage("audio bridge init failed: " + retryEx.Message);
            }
        }
        catch (Exception ex)
        {
            ViewModel.SetStatusMessage("call init failed: " + ex.Message);
        }
    }

    private async void DirectCallPage_Unloaded(object sender, RoutedEventArgs e)
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

    private async void ViewModel_NavigateBackRequested(object? sender, EventArgs e)
    {
        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            if (Frame?.CanGoBack == true)
                Frame.GoBack();
        });
    }

    private async void ViewModel_NavigateToLoginRequested(object? sender, EventArgs e)
    {
        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            Frame?.Navigate(typeof(LoginPage));
        });
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

    private CancellationToken CurrentToken => _pageLifetimeCts?.Token ?? CancellationToken.None;

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame?.CanGoBack == true)
            Frame.GoBack();
    }

    private async void MicrophoneButton_Click(object sender, RoutedEventArgs e)
    {
        if (_microphoneCommandInFlight)
            return;

        _microphoneCommandInFlight = true;
        try
        {
            if (!_audioBridgeReady || _audioBridgeHost == null || _audioBridgeHost.IsDisposed)
                await EnsureAudioBridgeReadyAsync(CurrentToken);
            await ViewModel.ToggleMicrophoneAsync(CurrentToken);
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
                await ViewModel.ToggleMicrophoneAsync(CurrentToken);
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
        try
        {
            if (!_audioBridgeReady || _audioBridgeHost == null || _audioBridgeHost.IsDisposed)
                await EnsureAudioBridgeReadyAsync(CurrentToken);
            await ViewModel.ToggleCameraAsync(CurrentToken);
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
                await ViewModel.ToggleCameraAsync(CurrentToken);
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
        try
        {
            if (!_audioBridgeReady || _audioBridgeHost == null || _audioBridgeHost.IsDisposed)
                await EnsureAudioBridgeReadyAsync(CurrentToken);
            await FocusMediaHostForCaptureAsync();
            await ViewModel.ToggleScreenShareAsync(CurrentToken);
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
                await EnsureAudioBridgeReadyAsync(CurrentToken);
                await ViewModel.ToggleScreenShareAsync(CurrentToken);
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

    private async void EndCallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_endCallCommandInFlight)
            return;

        _endCallCommandInFlight = true;
        try
        {
            await ViewModel.EndCallAsync(CurrentToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.SetStatusMessage(ex.Message);
        }
        finally
        {
            _endCallCommandInFlight = false;
        }
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
