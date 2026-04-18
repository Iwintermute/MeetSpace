namespace MeetSpace.Client.Domain.Calls;

public enum DirectCallStatus
{
    Unknown = 0,
    Invited = 1,
    Ringing = 2,
    Accepted = 3,
    Declined = 4,
    Ended = 5
}

public sealed record DirectCallParticipant(
    string UserId,
    string Role,
    string ParticipantStatus,
    DateTimeOffset? InvitedAtUtc = null,
    DateTimeOffset? AnsweredAtUtc = null,
    DateTimeOffset? LeftAtUtc = null,
    string? EndReason = null);

public sealed record DirectCallSessionInfo(
    string CallId,
    string RoomId,
    DirectCallStatus Status,
    string? InitiatorUserId = null,
    IReadOnlyList<DirectCallParticipant>? Participants = null,
    IReadOnlyList<string>? ParticipantPeerIds = null,
    DateTimeOffset? StartedAtUtc = null,
    DateTimeOffset? AnsweredAtUtc = null,
    DateTimeOffset? EndedAtUtc = null);
