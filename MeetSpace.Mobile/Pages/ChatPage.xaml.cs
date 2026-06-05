using MeetSpace.Client.Domain.Chat;
using MeetSpace.Mobile.Services;
using MeetSpace.Mobile.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Specialized;
using System.IO;

namespace MeetSpace.Mobile.Pages;

public partial class ChatPage : ContentPage
{
	private const long MaxAttachmentSizeBytes = 20L * 1024L * 1024L;
	private readonly ChatPageViewModel _viewModel;
	private readonly IServiceProvider _serviceProvider;
	private readonly MeetSpaceMobileRuntime _runtime;
	private CancellationTokenSource? _lifetimeCts;
	private bool _activated;
	private bool _isProgrammaticDialogSelection;
	private bool _scrollPending;

	public ChatPage(
		ChatPageViewModel viewModel,
		IServiceProvider serviceProvider,
		MeetSpaceMobileRuntime runtime)
	{
		InitializeComponent();
		_viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
		_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

		BindingContext = _viewModel;
		_viewModel.NavigateToLoginRequested += ViewModel_NavigateToLoginRequested;
		_viewModel.NavigateToDirectCallRequested += ViewModel_NavigateToDirectCallRequested;
		_viewModel.DisplayedMessages.CollectionChanged += DisplayedMessages_CollectionChanged;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		_lifetimeCts?.Cancel();
		_lifetimeCts?.Dispose();
		_lifetimeCts = new CancellationTokenSource();

		await _runtime.InitializeAsync(_lifetimeCts.Token);
		if (!_activated)
		{
			_activated = true;
			await _viewModel.ActivateAsync(Dispatcher);
		}

		SyncSelectedDialogInCollection();
		RequestScrollToEnd();
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();

		_lifetimeCts?.Cancel();
		_lifetimeCts?.Dispose();
		_lifetimeCts = null;

		_viewModel.Deactivate();
		_activated = false;
	}

	private async void SearchUsersButton_Clicked(object? sender, EventArgs e)
	{
		await _viewModel.SearchUsersByEmailAsync(UserSearchEntry.Text, CurrentToken);
	}

	private async void UserSearchEntry_Completed(object? sender, EventArgs e)
	{
		await _viewModel.SearchUsersByEmailAsync(UserSearchEntry.Text, CurrentToken);
	}

	private async void UserSearchResultsCollection_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		var selected = e.CurrentSelection.FirstOrDefault() as DirectUserSearchItem;
		await _viewModel.OpenSearchResultAsync(selected);
		UserSearchResultsCollection.SelectedItem = null;
		SyncSelectedDialogInCollection();
	}

	private async void DialogsCollection_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (_isProgrammaticDialogSelection)
			return;

		var selected = e.CurrentSelection.FirstOrDefault() as ChatDialogItem;
		await _viewModel.SelectDialogAsync(selected);
		SyncSelectedDialogInCollection();
		RequestScrollToEnd();
	}

	private async void SendMessageButton_Clicked(object? sender, EventArgs e)
	{
		var sent = await _viewModel.SendMessageAsync(MessageEditor.Text);
		if (sent)
			MessageEditor.Text = string.Empty;
	}

	private async void AttachFileButton_Clicked(object? sender, EventArgs e)
	{
		if (_viewModel.SelectedDialog == null)
		{
			await DisplayAlertAsync("Файлы", "Сначала выберите диалог.", "OK");
			return;
		}

		FileResult? fileResult;
		try
		{
			fileResult = await FilePicker.Default.PickAsync(new PickOptions
			{
				PickerTitle = "Выберите файл для отправки"
			});
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Файлы", "Не удалось открыть выбор файла: " + ex.Message, "OK");
			return;
		}

		if (fileResult == null)
			return;

		byte[] content;
		try
		{
			using var source = await fileResult.OpenReadAsync();
			using var memory = new MemoryStream();
			await source.CopyToAsync(memory);
			content = memory.ToArray();
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("Файлы", "Не удалось прочитать файл: " + ex.Message, "OK");
			return;
		}

		if (content.Length == 0)
		{
			await DisplayAlertAsync("Файлы", "Файл пустой.", "OK");
			return;
		}

		if (content.LongLength > MaxAttachmentSizeBytes)
		{
			await DisplayAlertAsync(
				"Файлы",
				"Файл слишком большой. Максимум: " + FormatFileSize(MaxAttachmentSizeBytes) + ".",
				"OK");
			return;
		}

		var confirm = await DisplayAlertAsync(
			"Отправить файл?",
			fileResult.FileName + Environment.NewLine + FormatFileSize(content.LongLength),
			"Отправить",
			"Отмена");
		if (!confirm)
			return;

		var sent = await _viewModel.SendFileAsync(
			fileResult.FileName,
			content,
			string.IsNullOrWhiteSpace(fileResult.ContentType) ? null : fileResult.ContentType);
		if (!sent)
			await DisplayAlertAsync("Файлы", "Не удалось отправить файл.", "OK");
	}

	private async void StartAudioCallButton_Clicked(object? sender, EventArgs e)
	{
		await _viewModel.StartDirectAudioCallAsync(CurrentToken);
	}

	private async void StartVideoCallButton_Clicked(object? sender, EventArgs e)
	{
		await _viewModel.StartDirectVideoCallAsync(CurrentToken);
	}

	private async void IncomingCallOverlay_AcceptAudioRequested(object? sender, EventArgs e)
	{
		await _viewModel.AcceptIncomingCallAsync(false, CurrentToken);
	}

	private async void IncomingCallOverlay_AcceptVideoRequested(object? sender, EventArgs e)
	{
		await _viewModel.AcceptIncomingCallAsync(true, CurrentToken);
	}

	private async void IncomingCallOverlay_DeclineRequested(object? sender, EventArgs e)
	{
		await _viewModel.DeclineIncomingCallAsync(CurrentToken);
	}

	private async void ViewModel_NavigateToLoginRequested(object? sender, EventArgs e)
	{
		if (Application.Current is App app)
			await app.NavigateToLoginAsync();
	}

	private async void ViewModel_NavigateToDirectCallRequested(object? sender, DirectCallNavigationRequest request)
	{
		var page = _serviceProvider.GetRequiredService<DirectCallPage>();
		page.Prepare(request);
		await Navigation.PushAsync(page);
	}

	private void DisplayedMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (!_activated || !_viewModel.IsMessagesVisible)
			return;

		if (e.Action != NotifyCollectionChangedAction.Add &&
			e.Action != NotifyCollectionChangedAction.Reset)
		{
			return;
		}

		RequestScrollToEnd();
	}

	private void RequestScrollToEnd()
	{
		if (_scrollPending)
			return;

		_scrollPending = true;

		void ScrollAction()
		{
			_scrollPending = false;
			if (!_activated || !_viewModel.IsMessagesVisible)
				return;

			var lastIndex = _viewModel.DisplayedMessages.Count - 1;
			if (lastIndex < 0)
				return;

			try
			{
				MessagesCollection.ScrollTo(lastIndex, position: ScrollToPosition.End, animate: false);
			}
			catch (Exception ex) when (IsTransientRecyclerScrollException(ex))
			{
				// Android RecyclerView can throw transient "Invalid target position"
				// while the CollectionView adapter is still reconciling item updates.
			}
		}

		var dispatcher = Dispatcher;
		if (dispatcher != null)
		{
			dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(60), ScrollAction);
		}
		else
		{
			MainThread.BeginInvokeOnMainThread(ScrollAction);
		}
	}

	private static bool IsTransientRecyclerScrollException(Exception ex)
	{
		for (var current = ex; current != null; current = current.InnerException)
		{
			var typeName = current.GetType().FullName ?? current.GetType().Name;
			if (typeName.IndexOf("IllegalArgumentException", StringComparison.OrdinalIgnoreCase) >= 0 &&
				current.Message?.IndexOf("Invalid target position", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}

		return false;
	}

	private void SyncSelectedDialogInCollection()
	{
		var selected = _viewModel.SelectedDialog;
		_isProgrammaticDialogSelection = true;
		try
		{
			if (selected == null)
			{
				DialogsCollection.SelectedItem = null;
				return;
			}

			var item = _viewModel.FilteredDialogs.FirstOrDefault(x =>
				string.Equals(x.ConversationId, selected.ConversationId, StringComparison.Ordinal));
			if (item == null && !string.IsNullOrWhiteSpace(selected.PeerId))
			{
				item = _viewModel.FilteredDialogs.FirstOrDefault(x =>
					string.Equals(x.PeerId, selected.PeerId, StringComparison.Ordinal));
			}

			DialogsCollection.SelectedItem = item;
		}
		finally
		{
			_isProgrammaticDialogSelection = false;
		}
	}

	private CancellationToken CurrentToken => _lifetimeCts?.Token ?? CancellationToken.None;

	private static string FormatFileSize(long sizeBytes)
	{
		const double kb = 1024d;
		const double mb = kb * 1024d;
		if (sizeBytes < 1024)
			return sizeBytes + " B";
		if (sizeBytes < 1024 * 1024)
			return (sizeBytes / kb).ToString("0.#") + " KB";
		return (sizeBytes / mb).ToString("0.##") + " MB";
	}
}
