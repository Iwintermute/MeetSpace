using MeetSpace.Client.App.Auth;
using MeetSpace.Client.App.Calls;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Bootstrap;
using MeetSpace.Client.Infrastructure.Paths;
using MeetSpace.Client.Infrastructure.Storage;
using MeetSpace.Client.Shared.Configuration;
using MeetSpace.Temporary;
using MeetSpace.ViewModels.Temporary;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace MeetSpace;

sealed partial class App : Application
{
    public new static App Current => (App)Application.Current;

    public IServiceProvider Services { get; private set; }

    private readonly MeetSpaceAppHost _host;
    private readonly ClientRuntimeOptions _options;
    private bool _hostStarted;
    private static int _fatalDialogShown;

    public ApplicationDataContainer Settings = ApplicationData.Current.LocalSettings;

    private const string AuthSessionFileName = "auth-session.json";

    public App()
    {
        InitializeComponent();

        _host = MeetSpaceHostBuilder.Build(services =>
        {
            services.AddSingleton<IAudioCallEngine, WebViewAudioCallEngine>();
            services.AddTransient<ChatPageViewModel>();
            services.AddTransient<ConferenceRoomPageViewModel>();
            services.AddTransient<DirectCallPageViewModel>();
            services.AddTransient<MeetingsHomePageViewModel>();
        });

        Services = _host.Services;
        _options = Services.GetRequiredService<ClientRuntimeOptions>();

        var authStore = Services.GetRequiredService<AuthSessionStore>();
        authStore.StateChanged += AuthStore_StateChanged;

        Suspending += OnSuspending;
        UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedException;
        AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs e)
    {
        await EnsureHostStartedAsync().ConfigureAwait(true);

        var isAuthenticated = await RestoreAuthSessionAsync().ConfigureAwait(true);
        if (isAuthenticated)
            _ = TryConnectRealtimeForAuthenticatedUserAsync();

        var rootFrame = Window.Current.Content as Frame;
        if (rootFrame == null)
        {
            rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;
            Window.Current.Content = rootFrame;
        }

        if (!e.PrelaunchActivated)
        {
            Windows.ApplicationModel.Core.CoreApplication.EnablePrelaunch(true);

            if (rootFrame.Content == null)
            {
                rootFrame.Navigate(
                    isAuthenticated ? typeof(MainPage) : typeof(LoginPage),
                    e.Arguments);
            }

            Window.Current.Activate();
        }
    }

    private async Task EnsureHostStartedAsync()
    {
        if (_hostStarted)
            return;

        await _host.StartAsync().ConfigureAwait(true);
        _hostStarted = true;
    }

    private async Task<bool> RestoreAuthSessionAsync()
    {
        var storage = Services.GetRequiredService<JsonFileStorage>();
        var paths = Services.GetRequiredService<IAppPaths>();
        var authStore = Services.GetRequiredService<AuthSessionStore>();
        var authClient = Services.GetRequiredService<ISupabaseAuthClient>();

        var filePath = Path.Combine(paths.SettingsDirectory, AuthSessionFileName);

        AuthTokens? persistedTokens;
        try
        {
            persistedTokens = await storage.LoadAsync<AuthTokens>(filePath).ConfigureAwait(true);
        }
        catch
        {
            authStore.ClearSession();
            await SafeDeleteAuthFileAsync(filePath).ConfigureAwait(true);
            return false;
        }

        if (persistedTokens == null)
        {
            authStore.ClearSession();
            return false;
        }

        var accessToken = NormalizeAuthValue(persistedTokens.AccessToken);
        var refreshToken = NormalizeAuthValue(persistedTokens.RefreshToken);
        var userId = NormalizeAuthValue(persistedTokens.UserId);
        var email = NormalizeAuthValue(persistedTokens.Email);

        if (string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(refreshToken) ||
            string.IsNullOrWhiteSpace(userId))
        {
            authStore.ClearSession();
            await SafeDeleteAuthFileAsync(filePath).ConfigureAwait(true);
            return false;
        }

        var effectiveTokens = new AuthTokens(
            accessToken,
            refreshToken,
            userId,
            email,
            persistedTokens.ExpiresAtUtc);
        var nowUtc = DateTimeOffset.UtcNow;

        var needRefresh =
            !effectiveTokens.ExpiresAtUtc.HasValue ||
            effectiveTokens.ExpiresAtUtc.Value <= nowUtc.AddMinutes(1);

        if (needRefresh)
        {
            if (string.IsNullOrWhiteSpace(effectiveTokens.RefreshToken))
            {
                authStore.ClearSession();
                await SafeDeleteAuthFileAsync(filePath).ConfigureAwait(true);
                return false;
            }

            try
            {
                effectiveTokens = await authClient.RefreshAsync(effectiveTokens.RefreshToken).ConfigureAwait(true);
                authStore.SetSession(effectiveTokens);
                await storage.SaveAsync(filePath, effectiveTokens).ConfigureAwait(true);
                return true;
            }
            catch
            {
                authStore.ClearSession();
                await SafeDeleteAuthFileAsync(filePath).ConfigureAwait(true);
                return false;
            }
        }

        authStore.SetSession(effectiveTokens);
        return true;
    }

    private async Task TryConnectRealtimeForAuthenticatedUserAsync()
    {
        try
        {
            var authStore = Services.GetRequiredService<AuthSessionStore>();
            if (!authStore.Current.IsAuthenticated)
                return;

            var startupService = Services.GetRequiredService<RealtimeStartupService>();
            await startupService.EnsureConnectedAsync(_options.DefaultRealtimeEndpoint).ConfigureAwait(true);
        }
        catch
        {
        }
    }

    private async void AuthStore_StateChanged(object sender, AuthSessionState state)
    {
        try
        {
            var storage = Services.GetRequiredService<JsonFileStorage>();
            var paths = Services.GetRequiredService<IAppPaths>();
            var filePath = Path.Combine(paths.SettingsDirectory, AuthSessionFileName);

            if (!state.IsAuthenticated ||
                string.IsNullOrWhiteSpace(state.AccessToken) ||
                string.IsNullOrWhiteSpace(state.RefreshToken) ||
                string.IsNullOrWhiteSpace(state.UserId))
            {
                await SafeDeleteAuthFileAsync(filePath).ConfigureAwait(true);
                return;
            }

            var tokens = new AuthTokens(
                state.AccessToken,
                state.RefreshToken,
                state.UserId,
                state.Email,
                state.ExpiresAtUtc);

            await storage.SaveAsync(filePath, tokens).ConfigureAwait(true);
        }
        catch
        {
        }
    }

    private static async Task SafeDeleteAuthFileAsync(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
        }

        await Task.CompletedTask;
    }

    private static string? NormalizeAuthValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        if (string.Equals(normalized, "null", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "undefined", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalized;
    }

    private async void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        var text =
            "Failed to navigate to page: " + (e.SourcePageType != null ? e.SourcePageType.FullName : "unknown") +
            Environment.NewLine + Environment.NewLine +
            (e.Exception != null ? e.Exception.ToString() : "No exception details.");

        Debug.WriteLine(text);
        await ShowFatalDialogOnceAsync("Navigation failed", text).ConfigureAwait(true);
    }

    private async void OnSuspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
    {
        var deferral = e.SuspendingOperation.GetDeferral();
        try
        {
            if (_hostStarted)
            {
                await _host.StopAsync().ConfigureAwait(true);
                _hostStarted = false;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static void OnUnobservedException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        Debug.WriteLine("UnobservedTaskException: " + e.Exception);
        e.SetObserved();
    }

    private async void OnUnhandledException(object sender, Windows.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;

        var text = e.Exception != null ? e.Exception.ToString() : e.Message;

        Debug.WriteLine("UnhandledException: " + text);
        await ShowFatalDialogOnceAsync("Unhandled exception", text).ConfigureAwait(true);
    }

    private void CurrentDomain_FirstChanceException(object sender, FirstChanceExceptionEventArgs e)
    {
        Debug.WriteLine("FirstChanceException: " + e.Exception.GetType().FullName + " :: " + e.Exception.Message);
    }

    private static async Task ShowFatalDialogOnceAsync(string title, string content)
    {
        if (Interlocked.Exchange(ref _fatalDialogShown, 1) == 1)
            return;

        try
        {
            await RunOnUiAsync(async () =>
            {
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = content,
                    CloseButtonText = "Close"
                };

                await dialog.ShowAsync().AsTask().ConfigureAwait(true);
            }).ConfigureAwait(true);
        }
        catch
        {
        }
        finally
        {
            Interlocked.Exchange(ref _fatalDialogShown, 0);
        }
    }

    private static Task RunOnUiAsync(Func<Task> action)
    {
        var tcs = new TaskCompletionSource<bool>();

        _ = Window.Current.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
        {
            try
            {
                await action().ConfigureAwait(true);
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }
}