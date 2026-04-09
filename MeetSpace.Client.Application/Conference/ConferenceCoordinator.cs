using MeetSpace.Client.Contracts.Conference;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Realtime.Abstractions;
using MeetSpace.Client.Shared.Results;

namespace MeetSpace.Client.App.Conference;

public sealed class ConferenceCoordinator : IDisposable
{
    private readonly IRealtimeGateway _gateway;
    private readonly IConferenceFeatureClient _client;
    private readonly ConferenceStore _store;

    public ConferenceCoordinator(
        IRealtimeGateway gateway,
        IConferenceFeatureClient client,
        ConferenceStore store)
    {
        _gateway = gateway;
        _client = client;
        _store = store;

        _gateway.EnvelopeReceived += OnEnvelopeReceived;
    }

    public async Task<Result> ConnectAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return Result.Failure(new Error("endpoint.invalid", "Endpoint URI is invalid."));

        _store.Update(s => s with { IsBusy = true, LastError = null });

        try
        {
            await _gateway.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
            _store.Update(s => s with { IsBusy = false });
            return Result.Success();
        }
        catch (Exception ex)
        {
            _store.Update(s => s with { IsBusy = false, LastError = ex.Message });
            return Result.Failure(new Error("connection.failed", ex.Message));
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => _gateway.DisconnectAsync(cancellationToken);

    public async Task<Result> CreateConferenceAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conferenceId))
            return Result.Failure(new Error("conference.invalid_id", "Conference ID must not be empty."));

        _store.Update(s => s with { IsBusy = true, LastError = null });

        try
        {
            await _client.CreateConferenceAsync(conferenceId, Guid.NewGuid().ToString("N"), cancellationToken)
                .ConfigureAwait(false);

            _store.Update(s => s with { ActiveConferenceId = conferenceId, IsBusy = false });
            await _client.ListMembersAsync(conferenceId, cancellationToken).ConfigureAwait(false);
            return Result.Success();

        }
        catch (Exception ex)
        {
            _store.Update(s => s with { IsBusy = false, LastError = ex.Message });
            return Result.Failure(new Error("conference.create_failed", ex.Message));
        }
    }

    public async Task<Result> JoinConferenceAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conferenceId))
            return Result.Failure(new Error("conference.invalid_id", "Conference ID must not be empty."));

        _store.Update(s => s with { IsBusy = true, LastError = null });

        try
        {
            await _client.JoinConferenceAsync(conferenceId, cancellationToken).ConfigureAwait(false);
            _store.Update(s => s with { ActiveConferenceId = conferenceId, IsBusy = false });
            await _client.ListMembersAsync(conferenceId, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _store.Update(s => s with { IsBusy = false, LastError = ex.Message });
            return Result.Failure(new Error("conference.join_failed", ex.Message));
        }
    }

    public async Task<Result> LeaveConferenceAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conferenceId))
            return Result.Failure(new Error("conference.invalid_id", "Conference ID must not be empty."));

        _store.Update(s => s with { IsBusy = true, LastError = null });

        try
        {
            await _client.LeaveConferenceAsync(conferenceId, cancellationToken).ConfigureAwait(false);

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
        catch (Exception ex)
        {
            _store.Update(s => s with { IsBusy = false, LastError = ex.Message });
            return Result.Failure(new Error("conference.leave_failed", ex.Message));
        }
    }

    public async Task<Result> ListMembersAsync(string conferenceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conferenceId))
            return Result.Failure(new Error("conference.invalid_id", "Conference ID must not be empty."));

        _store.Update(s => s with { IsBusy = true, LastError = null });

        try
        {
            await _client.ListMembersAsync(conferenceId, cancellationToken).ConfigureAwait(false);
            _store.Update(s => s with { ActiveConferenceId = conferenceId, IsBusy = false });
            return Result.Success();
        }
        catch (Exception ex)
        {
            _store.Update(s => s with { IsBusy = false, LastError = ex.Message });
            return Result.Failure(new Error("conference.list_members_failed", ex.Message));
        }
    }

    private void OnEnvelopeReceived(object? sender, FeatureResponseEnvelope envelope)
    {
        if (!string.Equals(envelope.Type, ProtocolMessageTypes.DispatchResult, StringComparison.Ordinal))
            return;

        if (!string.Equals(envelope.Object, ConferenceProtocol.Object, StringComparison.Ordinal))
            return;

        _store.Update(s => s with
        {
            IsBusy = false,
            LastError = envelope.Ok == false ? envelope.Message : null
        });
    }

    public void Dispose()
    {
        _gateway.EnvelopeReceived -= OnEnvelopeReceived;
    }
}