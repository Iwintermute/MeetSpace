namespace MeetSpace.Client.Domain.Conference;

public sealed record ConferenceSummary(
    string ConferenceId,
    string Title,
    int MemberCount,
    bool IsClosed = false,
    string? Status = null,
    string? MembershipStatus = null,
    string? MediaRoomId = null,
    DateTimeOffset? UpdatedAtUtc = null,
    DateTimeOffset? LastMessageAtUtc = null);
