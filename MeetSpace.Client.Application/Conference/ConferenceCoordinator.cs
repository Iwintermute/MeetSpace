using MeetSpace.Client.App.Session;
using MeetSpace.Client.Shared.Results;

namespace MeetSpace.Client.App.Conference;

public sealed class ConferenceCoordinator
{
    private readonly RealtimeStartupService _startupService;
    private readonly IConferenceFeatureClient _client;
    private readonly ConferenceStore _store;

    public ConferenceCoordinator(
        RealtimeStartupService startupService,
        IConferenceFeatureClient client,
        ConferenceStore store)
    {
        _startupService = startupService;
        _client = client;
        _store = store;
    }

    public async Task<Result> ConnectAsync(string? endpoint = null, CancellationToken cancellationToken = default)
    {
        _store.Update(s => s with { IsBusy = true, LastError = null });

        var result = await _startupService.EnsureConnectedAsync(endpoint, cancellationToken).ConfigureAwait(false);

        _store.Update(s => s with
        {
            IsBusy = false,
            LastError = result.IsFailure ? result.Error?.Message : null
        });

        return result;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return _startupService.DisconnectAsync(cancellationToken);
    }

    public async Task<Result> CreateConferenceAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conferenceId))
            return Result.Failure(new Error("conference.invalid_id", "Conference ID must not be empty."));

        _store.Update(s => s with { IsBusy = true, LastError = null });

        var ready = await _startupService.EnsureConnectedAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            _store.Update(s => s with { IsBusy = false, LastError = ready.Error!.Message });
            return Result.Failure(ready.Error!);
        }

        var result = await _client.CreateConferenceAsync(conferenceId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            _store.Update(s => s with { IsBusy = false, LastError = result.Error!.Message });
            return Result.Failure(result.Error!);
        }

        _store.Update(s => s with
        {
            IsBusy = false,
            LastError = null,
            ActiveConferenceId = result.Value!.ConferenceId,
            ActiveConference = result.Value
        });

        return Result.Success();
    }

    public async Task<Result> JoinConferenceAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conferenceId))
            return Result.Failure(new Error("conference.invalid_id", "Conference ID must not be empty."));

        _store.Update(s => s with { IsBusy = true, LastError = null });

        var ready = await _startupService.EnsureConnectedAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            _store.Update(s => s with { IsBusy = false, LastError = ready.Error!.Message });
            return Result.Failure(ready.Error!);
        }

        var result = await _client.JoinConferenceAsync(conferenceId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            _store.Update(s => s with { IsBusy = false, LastError = result.Error!.Message });
            return Result.Failure(result.Error!);
        }

        _store.Update(s => s with
        {
            IsBusy = false,
            LastError = null,
            ActiveConferenceId = result.Value!.ConferenceId,
            ActiveConference = result.Value
        });

        return Result.Success();
    }

    public async Task<Result> LeaveConferenceAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conferenceId))
            return Result.Failure(new Error("conference.invalid_id", "Conference ID must not be empty."));

        _store.Update(s => s with { IsBusy = true, LastError = null });

        var ready = await _startupService.EnsureConnectedAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            _store.Update(s => s with { IsBusy = false, LastError = ready.Error!.Message });
            return Result.Failure(ready.Error!);
        }

        var result = await _client.LeaveConferenceAsync(conferenceId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            _store.Update(s => s with { IsBusy = false, LastError = result.Error!.Message });
            return Result.Failure(result.Error!);
        }

        _store.Update(s => s with
        {
            IsBusy = false,
            LastError = null,
            ActiveConferenceId = string.Equals(s.ActiveConferenceId, conferenceId, StringComparison.Ordinal)
                ? null
                : s.ActiveConferenceId,
            ActiveConference = string.Equals(s.ActiveConferenceId, conferenceId, StringComparison.Ordinal)
                ? null
                : s.ActiveConference
        });

        return Result.Success();
    }

    public async Task<Result> CloseConferenceAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conferenceId))
            return Result.Failure(new Error("conference.invalid_id", "Conference ID must not be empty."));

        _store.Update(s => s with { IsBusy = true, LastError = null });

        var ready = await _startupService.EnsureConnectedAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            _store.Update(s => s with { IsBusy = false, LastError = ready.Error!.Message });
            return Result.Failure(ready.Error!);
        }

        var result = await _client.CloseConferenceAsync(conferenceId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            _store.Update(s => s with { IsBusy = false, LastError = result.Error!.Message });
            return Result.Failure(result.Error!);
        }

        _store.Update(s => s with
        {
            IsBusy = false,
            LastError = null
        });

        return Result.Success();
    }

    public async Task<Result> GetConferenceAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conferenceId))
            return Result.Failure(new Error("conference.invalid_id", "Conference ID must not be empty."));

        _store.Update(s => s with { IsBusy = true, LastError = null });

        var ready = await _startupService.EnsureConnectedAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            _store.Update(s => s with { IsBusy = false, LastError = ready.Error!.Message });
            return Result.Failure(ready.Error!);
        }

        var result = await _client.GetConferenceAsync(conferenceId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            _store.Update(s => s with { IsBusy = false, LastError = result.Error!.Message });
            return Result.Failure(result.Error!);
        }

        _store.Update(s => s with
        {
            IsBusy = false,
            LastError = null,
            ActiveConferenceId = result.Value!.ConferenceId,
            ActiveConference = result.Value
        });

        return Result.Success();
    }

    public async Task<Result> ListMembersAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conferenceId))
            return Result.Failure(new Error("conference.invalid_id", "Conference ID must not be empty."));

        var ready = await _startupService.EnsureConnectedAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            _store.Update(s => s with { IsBusy = false, LastError = ready.Error!.Message });
            return Result.Failure(ready.Error!);
        }

        var result = await _client.ListMembersAsync(conferenceId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            _store.Update(s => s with { IsBusy = false, LastError = result.Error!.Message });
            return Result.Failure(result.Error!);
        }

        _store.Update(s => s with
        {
            IsBusy = false,
            LastError = null,
            ActiveConferenceId = result.Value!.ConferenceId,
            ActiveConference = result.Value
        });

        return Result.Success();
    }

    public async Task<Result> ListConferencesAsync(CancellationToken cancellationToken = default)
    {
        _store.Update(s => s with { IsBusy = true, LastError = null });

        var ready = await _startupService.EnsureConnectedAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (ready.IsFailure)
        {
            _store.Update(s => s with { IsBusy = false, LastError = ready.Error!.Message });
            return Result.Failure(ready.Error!);
        }

        var result = await _client.ListConferencesAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            _store.Update(s => s with { IsBusy = false, LastError = result.Error!.Message });
            return Result.Failure(result.Error!);
        }

        _store.Update(s => s with
        {
            IsBusy = false,
            LastError = null,
            Conferences = result.Value!
        });

        return Result.Success();
    }
}