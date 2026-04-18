using MeetSpace.Client.Domain.Conference;
using MeetSpace.Client.Shared.Results;

namespace MeetSpace.Client.App.Conference;

public interface IConferenceFeatureClient
{
    Task<Result<ConferenceDetails>> CreateConferenceAsync(string conferenceId, CancellationToken cancellationToken = default);
    Task<Result<ConferenceDetails>> GetConferenceAsync(string conferenceId, CancellationToken cancellationToken = default);
    Task<Result<ConferenceDetails>> JoinConferenceAsync(string conferenceId, CancellationToken cancellationToken = default);
    Task<Result> LeaveConferenceAsync(string conferenceId, CancellationToken cancellationToken = default);
    Task<Result> CloseConferenceAsync(string conferenceId, CancellationToken cancellationToken = default);
    Task<Result<ConferenceDetails>> ListMembersAsync(string conferenceId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<ConferenceSummary>>> ListConferencesAsync(CancellationToken cancellationToken = default);
}