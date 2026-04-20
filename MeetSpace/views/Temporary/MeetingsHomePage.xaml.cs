using MeetSpace.ViewModels.Temporary;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace MeetSpace.Views.Temporary;

public sealed partial class MeetingsHomePage : Page
{
    public MeetingsHomePageViewModel ViewModel { get; }

    public MeetingsHomePage()
    {
        var services = App.Current.Services;
        ViewModel = services.GetRequiredService<MeetingsHomePageViewModel>();

        InitializeComponent();

        Loaded += MeetingsHomePage_Loaded;
        Unloaded += MeetingsHomePage_Unloaded;

        InviteOverlay.JoinRequested += InviteOverlay_JoinRequested;
        InviteOverlay.Closed += InviteOverlay_Closed;

        ViewModel.NavigateToLoginRequested += ViewModel_NavigateToLoginRequested;
        ViewModel.NavigateToConferenceRequested += ViewModel_NavigateToConferenceRequested;
        ViewModel.ErrorRequested += ViewModel_ErrorRequested;
        ViewModel.InviteRequested += ViewModel_InviteRequested;
        ViewModel.InviteClosedRequested += ViewModel_InviteClosedRequested;
    }

    private async void MeetingsHomePage_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.ActivateAsync(Dispatcher);
    }

    private void MeetingsHomePage_Unloaded(object sender, RoutedEventArgs e)
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

    private async void ViewModel_NavigateToConferenceRequested(object? sender, string conferenceId)
    {
        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            Frame?.Navigate(typeof(ConferenceRoomPage), conferenceId);
        });
    }

    private async void ViewModel_ErrorRequested(object? sender, string message)
    {
        await ShowErrorAsync(message);
    }

    private async void ViewModel_InviteRequested(object? sender, MeetingInviteRequestedEventArgs e)
    {
        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            InviteOverlay.Show(e.JoinLink, e.ConferenceId);
        });
    }

    private async void ViewModel_InviteClosedRequested(object? sender, EventArgs e)
    {
        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            InviteOverlay.Visibility = Visibility.Collapsed;
        });
    }

    private void MeetingsTabButton_Click(object sender, RoutedEventArgs e)
    {
    }

    private async void CallsTabButton_Click(object sender, RoutedEventArgs e)
    {
        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            Frame?.Navigate(typeof(ChatPage));
        });
    }

    private void NewMeetingButton_Click(object sender, RoutedEventArgs e)
    {
    }

    private async void CreateMeetingMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.CreateMeetingAsync();
    }

    private async void InstantMeetingMenuItem_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.StartInstantMeetingAsync();
    }

    private async void JoinMeetingButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.JoinMeetingAsync();
    }

    private async void InviteOverlay_JoinRequested(object sender, string conferenceId)
    {
        await ViewModel.JoinConferenceAsync(conferenceId);
    }

    private void InviteOverlay_Closed(object sender, EventArgs e)
    {
        ViewModel.CloseInvite();
    }

    private async Task ShowErrorAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Ошибка",
            Content = message,
            CloseButtonText = "ОК"
        };

        await dialog.ShowAsync();
    }
}
