using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Domain.Calls;

public sealed record CallSessionState(
    string? RoomId,
    string? TransportId,
    CallConnectionStage Stage,
    LocalMediaState LocalMedia,
    IReadOnlyList<RemoteParticipantState> Participants,
    string? LastSdp = null,
    string? LastCandidate = null)
{
    public static CallSessionState Empty { get; } = new(
        null,
        null,
        CallConnectionStage.Idle,
        new LocalMediaState(false, false, false),
        Array.Empty<RemoteParticipantState>());
}