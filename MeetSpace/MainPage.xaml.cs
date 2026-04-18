using MeetSpace.Client.App.Auth;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace MeetSpace;

public sealed partial class MainPage : Page
{
    private readonly AuthSessionStore _authStore;
    private readonly ISupabaseAuthClient _authClient;
    private readonly RealtimeStartupService _realtimeStartupService;
    private readonly SessionStore _sessionStore;
    private readonly ClientRuntimeOptions _options;

    public MainPage()
    {
        InitializeComponent();

        var services = App.Current.Services;
        _authStore = services.GetRequiredService<AuthSessionStore>();
        _authClient = services.GetRequiredService<ISupabaseAuthClient>();
        _realtimeStartupService = services.GetRequiredService<RealtimeStartupService>();
        _sessionStore = services.GetRequiredService<SessionStore>();
        _options = services.GetRequiredService<ClientRuntimeOptions>();

        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        _authStore.StateChanged += AuthStore_StateChanged;

        var ok = await EnsureAuthorizedAsync().ConfigureAwait(true);
        if (!ok)
            return;

        UpdateVisualState(ActualWidth);

        if (PrimaryPane.Content == null)
            NavigateByTag("Chats");

        SelectNavItem("Chats");
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
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
            await _realtimeStartupService
                .EnsureConnectedAsync(_options.DefaultRealtimeEndpoint)
                .ConfigureAwait(true);
        }
        catch
        {
        }

        return true;
    }

    private void AppNavigation_ItemInvoked(Microsoft.UI.Xaml.Controls.NavigationView sender, Microsoft.UI.Xaml.Controls.NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            NavigateByTag("Settings");
            return;
        }

        var item = args.InvokedItemContainer as Microsoft.UI.Xaml.Controls.NavigationViewItem;
        var tag = item?.Tag as string;
        if (!string.IsNullOrWhiteSpace(tag))
            NavigateByTag(tag);
    }

    private void AppNavigation_SelectionChanged(Microsoft.UI.Xaml.Controls.NavigationView sender, Microsoft.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavigateByTag("Settings");
            return;
        }

        var item = args.SelectedItem as Microsoft.UI.Xaml.Controls.NavigationViewItem;
        var tag = item?.Tag as string;
        if (!string.IsNullOrWhiteSpace(tag))
            NavigateByTag(tag);
    }

    private void NavigateByTag(string tag)
    {
        switch (tag)
        {
            case "Chats":
                NavigatePrimary(typeof(MeetSpace.Views.Temporary.ChatPage));
                break;

            case "New":
                NavigatePrimary(typeof(MeetSpace.Views.Temporary.MeetingsHomePage));
                break;

            case "Notifications":
                NavigatePrimary(typeof(MeetSpace.Views.Temporary.MeetingsHomePage));
                break;

            case "Search":
                NavigatePrimary(typeof(MeetSpace.Views.Temporary.ChatPage));
                break;

            case "Logout":
                _ = LogoutAsync();
                break;

            case "Settings":
                NavigatePrimary(typeof(MeetSpace.Views.Temporary.MeetingsHomePage));
                break;

            default:
                NavigatePrimary(typeof(MeetSpace.Views.Temporary.ChatPage));
                break;
        }
    }

    private void NavigatePrimary(Type pageType)
    {
        if (PrimaryPane.Content?.GetType() == pageType)
            return;

        PrimaryPane.Navigate(pageType);
    }

    private async Task LogoutAsync()
    {
        try
        {
            var auth = _authStore.Current;
            if (!string.IsNullOrWhiteSpace(auth.AccessToken))
            {
                try
                {
                    await _authClient.SignOutAsync(auth.AccessToken).ConfigureAwait(true);
                }
                catch
                {
                }
            }

            _authStore.ClearSession();
            _sessionStore.Reset();

            try
            {
                await _realtimeStartupService.DisconnectAsync().ConfigureAwait(true);
            }
            catch
            {
            }
        }
        finally
        {
            Frame?.Navigate(typeof(LoginPage));
        }
    }

    private void PrimaryPaneToggle_Click(object sender, RoutedEventArgs e)
    {
        AppNavigation.IsPaneOpen = !AppNavigation.IsPaneOpen;
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateVisualState(e.NewSize.Width);
    }

    private void UpdateVisualState(double width)
    {
        var state = width >= 1000 ? "WideState" : "NarrowState";
        //VisualStateManager.GoToState(this, state, true);
    }

    private void SelectNavItem(string tag)
    {
        var item =
            AppNavigation.MenuItems.OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>().FirstOrDefault(x => string.Equals(x.Tag as string, tag, StringComparison.Ordinal)) ??
            AppNavigation.FooterMenuItems.OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>().FirstOrDefault(x => string.Equals(x.Tag as string, tag, StringComparison.Ordinal));

        if (item != null && !ReferenceEquals(AppNavigation.SelectedItem, item))
            AppNavigation.SelectedItem = item;
    }
}