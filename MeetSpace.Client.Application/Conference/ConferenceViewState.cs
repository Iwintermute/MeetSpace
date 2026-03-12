using MeetSpace.Client.Domain.Conference;

namespace MeetSpace.Client.Application.Conference;

public sealed record ConferenceViewState(
    bool IsBusy,
    string? ActiveConferenceId,
    IReadOnlyList<ConferenceSummary> Conferences,
    ConferenceDetails? ActiveConference,
    string? LastError)
{
    public static ConferenceViewState Empty { get; } = new(
        false,
        null,
        Array.Empty<ConferenceSummary>(),
        null,
        null);
}