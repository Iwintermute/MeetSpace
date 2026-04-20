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
        if (_audioBridgeReady)
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

    private CancellationToken CurrentToken => _pageLifetimeCts?.Token ?? CancellationToken.None;

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame?.CanGoBack == true)
            Frame.GoBack();
    }

    private async void MicrophoneButton_Click(object sender, RoutedEventArgs e)
    {
        await EnsureAudioBridgeReadyAsync(CurrentToken);
        await ViewModel.ToggleMicrophoneAsync(CurrentToken);
    }

    private async void CameraButton_Click(object sender, RoutedEventArgs e)
    {
        await EnsureAudioBridgeReadyAsync(CurrentToken);
        await ViewModel.ToggleCameraAsync(CurrentToken);
    }

    private async void ScreenShareButton_Click(object sender, RoutedEventArgs e)
    {
        await EnsureAudioBridgeReadyAsync(CurrentToken);
        await ViewModel.ToggleScreenShareAsync(CurrentToken);
    }

    private async void EndCallButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.EndCallAsync(CurrentToken);
    }
}
