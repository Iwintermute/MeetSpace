using MeetSpace.Client.Domain.Chat;
using MeetSpace.ViewModels.Temporary;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace MeetSpace.Views.Temporary;

public sealed partial class ChatPage : Page
{
    private CancellationTokenSource? _pageLifetimeCts;
    private UwpWebViewAudioBridgeHost? _audioBridgeHost;
    private WebView2? _mediaHostView;
    private bool _audioBridgeReady;

    public ChatPageViewModel ViewModel { get; }

    public ChatPage()
    {
        var services = App.Current.Services;
        ViewModel = services.GetRequiredService<ChatPageViewModel>();

        InitializeComponent();

        ViewModel.NavigateToLoginRequested += ViewModel_NavigateToLoginRequested;
        ViewModel.FilteredDialogs.CollectionChanged += FilteredDialogs_CollectionChanged;
        ViewModel.Messages.CollectionChanged += Messages_CollectionChanged;

        Loaded += ChatPage_Loaded;
        Unloaded += ChatPage_Unloaded;
    }

    private async void ChatPage_Loaded(object sender, RoutedEventArgs e)
    {
        _pageLifetimeCts?.Cancel();
        _pageLifetimeCts?.Dispose();
        _pageLifetimeCts = new CancellationTokenSource();
        _audioBridgeReady = false;

        await ViewModel.ActivateAsync(Dispatcher);
        SyncSelectedDialogInList();
        ScrollMessagesToEnd();
    }

    private void ChatPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _pageLifetimeCts?.Cancel();
        _pageLifetimeCts?.Dispose();
        _pageLifetimeCts = null;

        ViewModel.Deactivate();

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

    private void FilteredDialogs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncSelectedDialogInList();
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollMessagesToEnd();
    }

    private void ScrollMessagesToEnd()
    {
        var count = ViewModel.Messages.Count;
        if (count > 0)
            MessagesList.ScrollIntoView(ViewModel.Messages[count - 1]);
    }

    private void SyncSelectedDialogInList()
    {
        var selectedDialog = ViewModel.SelectedDialog;
        if (selectedDialog == null)
        {
            DialogsList.SelectedItem = null;
            return;
        }

        var selected = ViewModel.FilteredDialogs.FirstOrDefault(x =>
            string.Equals(x.ConversationId, selectedDialog.ConversationId, StringComparison.Ordinal));

        if (selected == null && !string.IsNullOrWhiteSpace(selectedDialog.PeerId))
        {
            selected = ViewModel.FilteredDialogs.FirstOrDefault(x =>
                string.Equals(x.PeerId, selectedDialog.PeerId, StringComparison.Ordinal));
        }

        if (!ReferenceEquals(DialogsList.SelectedItem, selected))
            DialogsList.SelectedItem = selected;
    }

    private async Task EnsureAudioBridgeReadyAsync(CancellationToken cancellationToken)
    {
        if (_audioBridgeReady)
            return;

        EnsureMediaHostViewCreated();

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

    private CancellationToken CurrentToken => _pageLifetimeCts?.Token ?? CancellationToken.None;

    private async void DialogsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await ViewModel.SelectDialogAsync(DialogsList.SelectedItem as ChatDialogItem);
        SyncSelectedDialogInList();
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        var sent = await ViewModel.SendMessageAsync(MessageTextBox.Text);
        if (sent)
            MessageTextBox.Text = string.Empty;
    }

    private void DialogsSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ViewModel.ApplyDialogsFilter(sender?.Text);
    }

    private void ResetSearchButton_Click(object sender, RoutedEventArgs e)
    {
        DialogsSearchBox.Text = string.Empty;
        ViewModel.ApplyDialogsFilter(string.Empty);
    }

    private async void SearchUsersButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SearchUsersByEmailAsync(UserSearchBox.Text, CurrentToken);
    }

    private async void UserSearchBox_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        await ViewModel.SearchUsersByEmailAsync(UserSearchBox.Text, CurrentToken);
    }

    private async void UserSearchResultsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        var item = e.ClickedItem as DirectUserSearchItem;
        await ViewModel.OpenSearchResultAsync(item);
        UserSearchResultsList.SelectedItem = null;
    }

    private async void StartAudioCallButton_Click(object sender, RoutedEventArgs e)
    {
        await EnsureAudioBridgeReadyAsync(CurrentToken);
        await ViewModel.StartDirectAudioCallAsync(CurrentToken);
    }

    private async void StartVideoCallButton_Click(object sender, RoutedEventArgs e)
    {
        await EnsureAudioBridgeReadyAsync(CurrentToken);
        await ViewModel.StartDirectVideoCallAsync(CurrentToken);
    }

    private async void AcceptIncomingCallButton_Click(object sender, RoutedEventArgs e)
    {
        await EnsureAudioBridgeReadyAsync(CurrentToken);
        await ViewModel.AcceptIncomingCallAsync(false, CurrentToken);
    }

    private async void DeclineIncomingCallButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.DeclineIncomingCallAsync(CurrentToken);
    }

    private async void ToggleMicrophoneButton_Click(object sender, RoutedEventArgs e)
    {
        await EnsureAudioBridgeReadyAsync(CurrentToken);
        await ViewModel.ToggleMicrophoneAsync(CurrentToken);
    }

    private async void ToggleCameraButton_Click(object sender, RoutedEventArgs e)
    {
        await EnsureAudioBridgeReadyAsync(CurrentToken);
        await ViewModel.ToggleCameraAsync(CurrentToken);
    }

    private async void ToggleScreenShareButton_Click(object sender, RoutedEventArgs e)
    {
        await EnsureAudioBridgeReadyAsync(CurrentToken);
        await ViewModel.ToggleScreenShareAsync(CurrentToken);
    }

    private async void EndCallButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.EndCurrentCallAsync(CurrentToken);
    }
}
