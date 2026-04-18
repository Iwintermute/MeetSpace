using MeetSpace.Client.Domain.Chat;
using MeetSpace.ViewModels.Temporary;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Specialized;
using System.Linq;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace MeetSpace.Views.Temporary;

public sealed partial class ChatPage : Page
{
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
        await ViewModel.ActivateAsync(Dispatcher);
        SyncSelectedDialogInList();
        ScrollMessagesToEnd();
    }

    private void ChatPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Deactivate();
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

    private async void DialogsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await ViewModel.SelectDialogAsync(DialogsList.SelectedItem as ChatDialogItem);
        SyncSelectedDialogInList();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ConnectAsync(EndpointTextBox.Text);
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.DisconnectAsync();
    }

    private async void CreateConferenceButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.OpenDirectDialogAsync(ConferenceIdTextBox.Text?.Trim());
    }

    private async void JoinConferenceButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.OpenDirectDialogAsync(ConferenceIdTextBox.Text?.Trim());
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
}
