using MeetSpace.Client.Domain.Calls;
using MeetSpace.Client.Shared.Stores;

namespace MeetSpace.Client.App.Calls;

public sealed class CallStore : StoreBase<CallSessionState>
{
    public CallStore() : base(CallSessionState.Empty)
    {
    }

    public void Reset()
    {
        Set(CallSessionState.Empty);
    }

    public void SetStage(CallConnectionStage stage, string? roomId = null, string? transportId = null)
    {
        Update(state => state with
        {
            RoomId = roomId ?? state.RoomId,
            TransportId = transportId ?? state.TransportId,
            Stage = stage
        });
    }

    public void SetParticipants(IReadOnlyList<RemoteParticipantState> participants)
    {
        Update(state => state with
        {
            Participants = participants
        });
    }

    public void SetMicrophoneEnabled(bool enabled, string? activeMicrophoneId = null)
    {
        Update(state => state with
        {
            LocalMedia = state.LocalMedia with
            {
                MicrophoneEnabled = enabled,
                ActiveMicrophoneId = activeMicrophoneId ?? state.LocalMedia.ActiveMicrophoneId
            }
        });
    }

    public void SetLastNegotiation(string? sdp = null, string? candidate = null)
    {
        Update(state => state with
        {
            LastSdp = sdp ?? state.LastSdp,
            LastCandidate = candidate ?? state.LastCandidate
        });
    }
}