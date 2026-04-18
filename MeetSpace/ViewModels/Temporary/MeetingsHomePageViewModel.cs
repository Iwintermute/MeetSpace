using CommunityToolkit.Mvvm.ComponentModel;
using MeetSpace.Client.App.Auth;
using MeetSpace.Client.App.Conference;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Shared.Configuration;
using System;
using System.Threading.Tasks;
using Windows.UI.Core;

namespace MeetSpace.ViewModels.Temporary;

public sealed class MeetingsHomePageViewModel : ObservableObject
{
    private readonly ConferenceCoordinator _conferenceCoordinator;
    private readonly AuthSessionStore _authStore;
    private readonly RealtimeStartupService _realtimeStartupService;
    private readonly ClientRuntimeOptions _options;

    private CoreDispatcher? _dispatcher;
    private bool _isActive;
    private string _meetingCode = string.Empty;

    public MeetingsHomePageViewModel(
        ConferenceCoordinator conferenceCoordinator,
        AuthSessionStore authStore,
        RealtimeStartupService realtimeStartupService,
        ClientRuntimeOptions options)
    {
        _conferenceCoordinator = conferenceCoordinator;
        _authStore = authStore;
        _realtimeStartupService = realtimeStartupService;
        _options = options;
    }

    public string MeetingCode
    {
        get => _meetingCode;
        set => SetProperty(ref _meetingCode, value);
    }

    public event EventHandler? NavigateToLoginRequested;
    public event EventHandler<string>? NavigateToConferenceRequested;
    public event EventHandler<string>? ErrorRequested;
    public event EventHandler<MeetingInviteRequestedEventArgs>? InviteRequested;
    public event EventHandler? InviteClosedRequested;

    public async Task ActivateAsync(CoreDispatcher dispatcher)
    {
        if (_isActive)
            return;

        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _isActive = true;

        _authStore.StateChanged += AuthStore_StateChanged;

        var authorized = await EnsureAuthorizedAsync().ConfigureAwait(false);
        if (!authorized)
            await RunOnUiThreadAsync(RaiseNavigateToLogin).ConfigureAwait(false);
    }

    public void Deactivate()
    {
        if (!_isActive)
            return;

        _isActive = false;
        _authStore.StateChanged -= AuthStore_StateChanged;
        _dispatcher = null;
    }

    public async Task CreateMeetingAsync()
    {
        var conferenceId = "meet-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var result = await _conferenceCoordinator
            .CreateConferenceAsync(conferenceId)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            await RaiseErrorAsync(result.Error?.Message ?? "CreateConferenceAsync failed.").ConfigureAwait(false);
            return;
        }

        var joinLink = "meetspace://conference/" + conferenceId;
        await RunOnUiThreadAsync(() =>
        {
            InviteRequested?.Invoke(this, new MeetingInviteRequestedEventArgs(joinLink, conferenceId));
        }).ConfigureAwait(false);
    }

    public async Task StartInstantMeetingAsync()
    {
        var authorized = await EnsureAuthorizedAsync().ConfigureAwait(false);
        if (!authorized)
        {
            await RunOnUiThreadAsync(RaiseNavigateToLogin).ConfigureAwait(false);
            return;
        }

        var conferenceId = "meet-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var result = await _conferenceCoordinator
            .CreateConferenceAsync(conferenceId)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            await RaiseErrorAsync(result.Error?.Message ?? "Не удалось создать встречу.").ConfigureAwait(false);
            return;
        }

        await RunOnUiThreadAsync(() =>
        {
            NavigateToConferenceRequested?.Invoke(this, conferenceId);
        }).ConfigureAwait(false);
    }

    public async Task JoinMeetingAsync()
    {
        var authorized = await EnsureAuthorizedAsync().ConfigureAwait(false);
        if (!authorized)
        {
            await RunOnUiThreadAsync(RaiseNavigateToLogin).ConfigureAwait(false);
            return;
        }

        var value = MeetingCode?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            await RaiseErrorAsync("Введите код встречи или ссылку.").ConfigureAwait(false);
            return;
        }

        var conferenceId = ExtractConferenceId(value);
        await JoinConferenceAsync(conferenceId).ConfigureAwait(false);
    }

    public async Task JoinConferenceAsync(string conferenceId)
    {
        var authorized = await EnsureAuthorizedAsync().ConfigureAwait(false);
        if (!authorized)
        {
            await RunOnUiThreadAsync(RaiseNavigateToLogin).ConfigureAwait(false);
            return;
        }

        var result = await _conferenceCoordinator
            .JoinConferenceAsync(conferenceId)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            await RaiseErrorAsync(result.Error?.Message ?? "Не удалось присоединиться к встрече.").ConfigureAwait(false);
            return;
        }

        await RunOnUiThreadAsync(() =>
        {
            NavigateToConferenceRequested?.Invoke(this, conferenceId);
        }).ConfigureAwait(false);
    }

    public void CloseInvite()
    {
        InviteClosedRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void AuthStore_StateChanged(object sender, AuthSessionState state)
    {
        if (!_isActive)
            return;

        if (!state.IsAuthenticated)
            await RunOnUiThreadAsync(RaiseNavigateToLogin).ConfigureAwait(false);
    }

    private async Task<bool> EnsureAuthorizedAsync()
    {
        var auth = _authStore.Current;

        if (!auth.IsAuthenticated || string.IsNullOrWhiteSpace(auth.UserId))
            return false;

        try
        {
            var result = await _realtimeStartupService
                .EnsureConnectedAsync(_options.DefaultRealtimeEndpoint)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                _authStore.ClearSession();
                return false;
            }
        }
        catch
        {
            _authStore.ClearSession();
            return false;
        }

        return true;
    }

    private async Task RaiseErrorAsync(string message)
    {
        await RunOnUiThreadAsync(() =>
        {
            ErrorRequested?.Invoke(this, message);
        }).ConfigureAwait(false);
    }

    private void RaiseNavigateToLogin()
    {
        NavigateToLoginRequested?.Invoke(this, EventArgs.Empty);
    }

    private Task RunOnUiThreadAsync(Action action)
    {
        var dispatcher = _dispatcher;
        if (dispatcher == null)
            return Task.CompletedTask;

        if (dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<bool>();
        _ = dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            try
            {
                action();
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        return tcs.Task;
    }

    private static string ExtractConferenceId(string raw)
    {
        if (raw.StartsWith("meetspace://conference/", StringComparison.OrdinalIgnoreCase))
            return raw.Substring("meetspace://conference/".Length);

        return raw;
    }
}

public sealed class MeetingInviteRequestedEventArgs : EventArgs
{
    public MeetingInviteRequestedEventArgs(string joinLink, string conferenceId)
    {
        JoinLink = joinLink;
        ConferenceId = conferenceId;
    }

    public string JoinLink { get; }
    public string ConferenceId { get; }
}
