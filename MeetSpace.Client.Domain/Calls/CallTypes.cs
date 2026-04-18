namespace MeetSpace.Client.Domain.Calls;

public enum CallConnectionStage
{
    Idle = 0,
    JoiningRoom = 1,
    TransportOpening = 2,
    Publishing = 3,
    Negotiating = 4,
    Connected = 5,
    Faulted = 6
}
public enum CallKind
{
    Unknown = 0,
    Conference = 1,
    Direct = 2
}

public enum CallMediaTrackType
{
    Audio = 0,
    Video = 1,
    ScreenShare = 2
}

public sealed record LocalMediaState(
    bool MicrophoneEnabled,
    bool CameraEnabled,
    bool ScreenShareEnabled,
    string? ActiveMicrophoneId = null,
    string? ActiveCameraId = null,
    string? ActiveScreenSourceId = null);

public sealed record RemoteParticipantState(
    string PeerId,
    bool HasAudio,
    bool HasVideo,
    bool HasScreenShare,
    bool IsSpeaking = false,
    string? UserId = null);

public sealed record CallSessionState(
    string? ConversationId,
    string? RoomId,
    string? TransportId,
    CallConnectionStage Stage,
    LocalMediaState LocalMedia,
    IReadOnlyList<RemoteParticipantState> Participants,
    string? LastSdp = null,
    string? LastCandidate = null,
    string? SessionId = null,
    CallKind Kind = CallKind.Unknown)
{
    public static CallSessionState Empty { get; } = new(
        null,
        null,
        null,
        CallConnectionStage.Idle,
        new LocalMediaState(false, false, false),
        Array.Empty<RemoteParticipantState>(),
        null,
        null,
        null,
        CallKind.Unknown);
}