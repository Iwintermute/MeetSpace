namespace MeetSpace.Client.Domain.Conference;

public sealed record ConferenceMember(
    string PeerId,
    string SessionId,
    bool IsOwner,
    string? UserId = null,
    string? Role = null,
    string? MembershipStatus = null,
    DateTimeOffset? JoinedAtUtc = null,
    DateTimeOffset? LeftAtUtc = null);
