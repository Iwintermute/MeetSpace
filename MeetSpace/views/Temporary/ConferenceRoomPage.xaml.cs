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
        }
        catch (OperationCanceledException)
        {
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
        var count = ViewModel.Messages.Count;
        if (count > 0)
            MessagesList.ScrollIntoView(ViewModel.Messages[count - 1]);
    }

    private void EnsureMediaHostViewCreated()
    {
        if (_mediaHostView != null)
            return;

        var webView = new WebView2
        {
            Width = 1,
            Height = 1,
            Opacity = 0.01,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
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
        var token = _pageLifetimeCts != null ? _pageLifetimeCts.Token : CancellationToken.None;

        try
        {
            if (!_audioBridgeReady)
                await EnsureAudioBridgeReadyAsync(token);

            await ViewModel.HandleMicrophoneAsync(token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.SetStatusMessage("audio bridge init failed: " + ex.Message);
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
}
