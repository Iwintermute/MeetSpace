using MeetSpace.Client.Domain.Chat;
using MeetSpace.ViewModels.Temporary;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace MeetSpace.Views.Temporary;

public sealed partial class ChatPage : Page
{
    private const ulong MaxAttachmentSizeBytes = 20UL * 1024UL * 1024UL;
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

    private async void AttachFileButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        if (file == null)
            return;

        await SendFileWithConfirmationAsync(file);
    }

    private void ChatDropArea_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Отпустите файл для отправки";
            e.DragUIOverride.IsCaptionVisible = true;
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }

        e.DragUIOverride.IsGlyphVisible = true;
        e.Handled = true;
    }

    private async void ChatDropArea_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        var items = await e.DataView.GetStorageItemsAsync();
        var file = items?.OfType<StorageFile>().FirstOrDefault();
        if (file == null)
            return;

        await SendFileWithConfirmationAsync(file);
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

    private async Task SendFileWithConfirmationAsync(StorageFile file)
    {
        if (ViewModel.SelectedDialog == null)
        {
            await ShowInfoDialogAsync("Сначала выберите диалог.");
            return;
        }

        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size == 0)
        {
            await ShowInfoDialogAsync("Файл пустой.");
            return;
        }

        if (properties.Size > MaxAttachmentSizeBytes)
        {
            await ShowInfoDialogAsync(
                "Файл слишком большой. Максимум: " + FormatFileSize(MaxAttachmentSizeBytes) + ".");
            return;
        }

        var confirmed = await ShowSendConfirmationDialogAsync(file.Name, properties.Size);
        if (!confirmed)
            return;

        var buffer = await FileIO.ReadBufferAsync(file);
        var content = buffer.ToArray();
        var sent = await ViewModel.SendFileAsync(file.Name, content, file.ContentType);
        if (!sent)
            await ShowInfoDialogAsync("Не удалось отправить файл.");
    }

    private async Task<bool> ShowSendConfirmationDialogAsync(string fileName, ulong sizeBytes)
    {
        var dialog = new ContentDialog
        {
            Title = "Отправить файл?",
            Content = fileName + "\n" + FormatFileSize(sizeBytes),
            PrimaryButtonText = "Отправить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private static string FormatFileSize(ulong sizeBytes)
    {
        const double kb = 1024d;
        const double mb = kb * 1024d;
        if (sizeBytes < 1024)
            return sizeBytes + " B";
        if (sizeBytes < 1024 * 1024)
            return (sizeBytes / kb).ToString("0.#") + " KB";
        return (sizeBytes / mb).ToString("0.##") + " MB";
    }

    private static async Task ShowInfoDialogAsync(string text)
    {
        var dialog = new ContentDialog
        {
            Title = "Файлы",
            Content = text,
            CloseButtonText = "OK"
        };
        await dialog.ShowAsync();
    }
}
