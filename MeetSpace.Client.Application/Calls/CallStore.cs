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

    public void SetStage(
        CallConnectionStage stage,
        string? conversationId = null,
        string? roomId = null,
        string? transportId = null,
        string? sessionId = null,
        CallKind? kind = null)
    {
        Update(state => state with
        {
            ConversationId = conversationId ?? state.ConversationId,
            RoomId = roomId ?? state.RoomId,
            TransportId = transportId ?? state.TransportId,
            SessionId = sessionId ?? state.SessionId,
            Kind = kind ?? state.Kind,
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

    public void SetCameraEnabled(bool enabled, string? activeCameraId = null)
    {
        Update(state => state with
        {
            LocalMedia = state.LocalMedia with
            {
                CameraEnabled = enabled,
                ActiveCameraId = activeCameraId ?? state.LocalMedia.ActiveCameraId
            }
        });
    }

    public void SetScreenShareEnabled(bool enabled, string? activeScreenSourceId = null)
    {
        Update(state => state with
        {
            LocalMedia = state.LocalMedia with
            {
                ScreenShareEnabled = enabled,
                ActiveScreenSourceId = activeScreenSourceId ?? state.LocalMedia.ActiveScreenSourceId
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