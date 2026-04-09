using MeetSpace.Client.App.Auth;
using MeetSpace.Client.App.Conference;
using MeetSpace.Client.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace MeetSpace.Views.Temporary
{
    public sealed partial class MeetingsHomePage : Page
    {
        private readonly ConferenceCoordinator _conferenceCoordinator;
        private readonly AuthSessionStore _authStore;
        private readonly RealtimeStartupService _realtimeStartupService;
        private readonly RealtimeAuthBinder _realtimeAuthBinder;
        public MeetingsHomePage()
        {
            this.InitializeComponent();

            _conferenceCoordinator = App.Current.Services.GetRequiredService<ConferenceCoordinator>();
            _authStore = App.Current.Services.GetRequiredService<AuthSessionStore>();
            _realtimeStartupService = App.Current.Services.GetRequiredService<RealtimeStartupService>();
            _realtimeAuthBinder = App.Current.Services.GetRequiredService<RealtimeAuthBinder>();

            Loaded += MeetingsHomePage_Loaded;
            Unloaded += MeetingsHomePage_Unloaded;

            InviteOverlay.JoinRequested += InviteOverlay_JoinRequested;
            InviteOverlay.Closed += InviteOverlay_Closed;
        }
        private async void MeetingsHomePage_Loaded(object sender, RoutedEventArgs e)
        {
            _authStore.StateChanged += AuthStore_StateChanged;

            var ok = await EnsureAuthorizedAsync();
            if (!ok)
                return;
        }

        private void MeetingsHomePage_Unloaded(object sender, RoutedEventArgs e)
        {
            _authStore.StateChanged -= AuthStore_StateChanged;
        }

        private async void AuthStore_StateChanged(object sender, AuthSessionState state)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (!state.IsAuthenticated)
                    Frame?.Navigate(typeof(LoginPage));
            });
        }

        private async Task<bool> EnsureAuthorizedAsync()
        {
            var auth = _authStore.Current;

            if (!auth.IsAuthenticated || string.IsNullOrWhiteSpace(auth.UserId))
            {
                Frame?.Navigate(typeof(LoginPage));
                return false;
            }

            try
            {
                var result = await _realtimeStartupService.EnsureConnectedAsync("ws://127.0.0.1:9002");
                if (result.IsSuccess)
                    await _realtimeAuthBinder.BindAsync();
            }
            catch
            {
            }

            return true;
        }
        private void NewMeetingButton_Click(object sender, RoutedEventArgs e)
        {
            // Пусто. Flyout откроется сам.
        }

        private async void CreateMeetingMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var conferenceId = "meet-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var result = await _conferenceCoordinator.CreateConferenceAsync(conferenceId);

            if (result.IsFailure)
                return;

            // Пока строим ссылку от conferenceId.
            // Когда сервер начнет возвращать invite/token - подставишь его сюда.
            var joinLink = "meetspace://conference/" + conferenceId;

            InviteOverlay.Show(joinLink, conferenceId);
        }

        private async void InstantMeetingMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var conferenceId = "meet-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var result = await _conferenceCoordinator.CreateConferenceAsync(conferenceId);

            if (result.IsFailure)
                return;

            Frame.Navigate(typeof(ConferenceRoomPage), conferenceId);
        }

        private async void JoinMeetingButton_Click(object sender, RoutedEventArgs e)
        {
            var value = MeetingCodeTextBox.Text != null ? MeetingCodeTextBox.Text.Trim() : null;
            if (string.IsNullOrWhiteSpace(value))
                return;

            var conferenceId = ExtractConferenceId(value);

            var result = await _conferenceCoordinator.JoinConferenceAsync(conferenceId);
            if (result.IsFailure)
                return;

            Frame.Navigate(typeof(ConferenceRoomPage), conferenceId);
        }

        private async void InviteOverlay_JoinRequested(object sender, string conferenceId)
        {
            var result = await _conferenceCoordinator.JoinConferenceAsync(conferenceId);
            if (result.IsFailure)
                return;

            Frame.Navigate(typeof(ConferenceRoomPage), conferenceId);
        }

        private void InviteOverlay_Closed(object sender, EventArgs e)
        {
            InviteOverlay.Visibility = Visibility.Collapsed;
        }

        private static string ExtractConferenceId(string raw)
        {
            if (raw.StartsWith("meetspace://conference/", StringComparison.OrdinalIgnoreCase))
                return raw.Substring("meetspace://conference/".Length);

            return raw;
        }
    }
}