using MeetSpace.Client.Domain.Chat;
using MeetSpace.ViewModels.Temporary;
using Microsoft.Extensions.DependencyInjection;
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
    private bool _isSynchronizingDialogsSelection;
    private bool _scrollRequestPending;

    public ChatPageViewModel ViewModel { get; }

    public ChatPage()
    {
        var services = App.Current.Services;
        ViewModel = services.GetRequiredService<ChatPageViewModel>();

        InitializeComponent();

        ViewModel.NavigateToLoginRequested += ViewModel_NavigateToLoginRequested;
        ViewModel.NavigateToDirectCallRequested += ViewModel_NavigateToDirectCallRequested;
        ViewModel.FilteredDialogs.CollectionChanged += FilteredDialogs_CollectionChanged;
        ViewModel.DisplayedMessages.CollectionChanged += DisplayedMessages_CollectionChanged;

        Loaded += ChatPage_Loaded;
        Unloaded += ChatPage_Unloaded;
    }

    private async void ChatPage_Loaded(object sender, RoutedEventArgs e)
    {
        _pageLifetimeCts?.Cancel();
        _pageLifetimeCts?.Dispose();
        _pageLifetimeCts = new CancellationTokenSource();

        await ViewModel.ActivateAsync(Dispatcher);
        SyncSelectedDialogInList();
        RequestScrollMessagesToEnd();
    }

    private void ChatPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _pageLifetimeCts?.Cancel();
        _pageLifetimeCts?.Dispose();
        _pageLifetimeCts = null;

        ViewModel.Deactivate();
    }

    private async void ViewModel_NavigateToLoginRequested(object? sender, EventArgs e)
    {
        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            Frame?.Navigate(typeof(LoginPage));
        });
    }

    private async void ViewModel_NavigateToDirectCallRequested(object? sender, DirectCallNavigationRequest request)
    {
        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            Frame?.Navigate(typeof(DirectCallPage), request);
        });
    }

    private void FilteredDialogs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncSelectedDialogInList();
    }

    private void DisplayedMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
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

        var count = ViewModel.DisplayedMessages.Count;
        if (count <= 0)
            return;

        var lastItem = ViewModel.DisplayedMessages[count - 1];
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

    private void SyncSelectedDialogInList()
    {
        if (_isSynchronizingDialogsSelection)
            return;

        _isSynchronizingDialogsSelection = true;
        var selectedDialog = ViewModel.SelectedDialog;
        try
        {
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
        finally
        {
            _isSynchronizingDialogsSelection = false;
        }
    }

    private CancellationToken CurrentToken => _pageLifetimeCts?.Token ?? CancellationToken.None;

    private async void DialogsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSynchronizingDialogsSelection)
            return;

        await ViewModel.SelectDialogAsync(DialogsList.SelectedItem as ChatDialogItem);
        SyncSelectedDialogInList();
        RequestScrollMessagesToEnd();
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

    //private void ResetSearchButton_Click(object sender, RoutedEventArgs e)
    //{
    //    DialogsSearchBox.Text = string.Empty;
    //    ViewModel.ApplyDialogsFilter(string.Empty);
    //}

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
        await ViewModel.StartDirectAudioCallAsync(CurrentToken);
    }

    private async void StartVideoCallButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.StartDirectVideoCallAsync(CurrentToken);
    }

    private async void IncomingCallOverlay_AcceptAudioRequested(object sender, EventArgs e)
    {
        await ViewModel.AcceptIncomingCallAsync(false, CurrentToken);
    }

    private async void IncomingCallOverlay_AcceptVideoRequested(object sender, EventArgs e)
    {
        await ViewModel.AcceptIncomingCallAsync(true, CurrentToken);
    }

    private async void IncomingCallOverlay_DeclineRequested(object sender, EventArgs e)
    {
        await ViewModel.DeclineIncomingCallAsync(CurrentToken);
    }
}
