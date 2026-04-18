namespace MeetSpace.Client.Domain.Conference;

public sealed record ConferenceDetails(
    string ConferenceId,
    string OwnerPeerId,
    bool IsClosed,
    ulong Revision,
    IReadOnlyList<ConferenceMember> Members,
    string? Title = null,
    string? Status = null,
    string? MediaRoomId = null,
    IReadOnlyList<string>? ActivePeerIds = null);
