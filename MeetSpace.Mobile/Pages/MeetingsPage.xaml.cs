using MeetSpace.Mobile.Services;
using MeetSpace.Mobile.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MeetSpace.Mobile.Pages;

public partial class MeetingsPage : ContentPage
{
	private readonly MeetingsHomePageViewModel _viewModel;
	private readonly IServiceProvider _serviceProvider;
	private readonly MeetSpaceMobileRuntime _runtime;
	private bool _activated;

	public MeetingsPage(
		MeetingsHomePageViewModel viewModel,
		IServiceProvider serviceProvider,
		MeetSpaceMobileRuntime runtime)
	{
		InitializeComponent();
		_viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
		_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
		_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

		BindingContext = _viewModel;
		_viewModel.NavigateToConferenceRequested += ViewModel_NavigateToConferenceRequested;
		_viewModel.NavigateToLoginRequested += ViewModel_NavigateToLoginRequested;
		_viewModel.ErrorRequested += ViewModel_ErrorRequested;
		_viewModel.InviteRequested += ViewModel_InviteRequested;
		_viewModel.InviteClosedRequested += ViewModel_InviteClosedRequested;

		InviteOverlay.JoinRequested += InviteOverlay_JoinRequested;
		InviteOverlay.Closed += InviteOverlay_Closed;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await _runtime.InitializeAsync();
		if (!_activated)
		{
			_activated = true;
			await _viewModel.ActivateAsync(Dispatcher);
		}
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_viewModel.Deactivate();
		_activated = false;
	}

	private async void CreateMeetingButton_Clicked(object? sender, EventArgs e)
	{
		await _viewModel.CreateMeetingAsync();
	}

	private async void InstantMeetingButton_Clicked(object? sender, EventArgs e)
	{
		await _viewModel.StartInstantMeetingAsync();
	}

	private async void JoinMeetingButton_Clicked(object? sender, EventArgs e)
	{
		await _viewModel.JoinMeetingAsync();
	}

	private void MeetingsTabButton_Clicked(object? sender, EventArgs e)
	{
	}

	private void CallsTabButton_Clicked(object? sender, EventArgs e)
	{
		SwitchToChatsTab();
	}

	private async void ViewModel_NavigateToConferenceRequested(object? sender, string conferenceId)
	{
		var page = _serviceProvider.GetRequiredService<ConferenceRoomPage>();
		page.Prepare(conferenceId);
		await Navigation.PushAsync(page);
	}

	private async void ViewModel_NavigateToLoginRequested(object? sender, EventArgs e)
	{
		if (Application.Current is App app)
			await app.NavigateToLoginAsync();
	}

	private async void ViewModel_ErrorRequested(object? sender, string message)
	{
		StatusLabel.Text = message;
		await DisplayAlertAsync("Ошибка", message, "ОК");
	}

	private void ViewModel_InviteRequested(object? sender, MeetingInviteRequestedEventArgs e)
	{
		InviteOverlay.Show(e.JoinLink, e.ConferenceId);
	}

	private void ViewModel_InviteClosedRequested(object? sender, EventArgs e)
	{
		InviteOverlay.IsVisible = false;
	}

	private async void InviteOverlay_JoinRequested(object? sender, string conferenceId)
	{
		await _viewModel.JoinConferenceAsync(conferenceId);
	}

	private void InviteOverlay_Closed(object? sender, EventArgs e)
	{
		_viewModel.CloseInvite();
	}

	private void SwitchToChatsTab()
	{
		if (Parent is NavigationPage navigationPage &&
			navigationPage.Parent is TabbedPage tabbedPage &&
			tabbedPage.Children.Count > 1)
		{
			tabbedPage.CurrentPage = tabbedPage.Children[1];
			return;
		}

		var mainPage = Application.Current?.Windows.Count > 0
			? Application.Current.Windows[0].Page
			: null;
		if (mainPage is HomeTabbedPage homeTabbedPage && homeTabbedPage.Children.Count > 1)
			homeTabbedPage.CurrentPage = homeTabbedPage.Children[1];
	}
}
