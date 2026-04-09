namespace MeetSpace.Client.Contracts.Protocol;

public static class ProtocolMessageTypes
{
    public const string DispatchResult = "dispatch_result";
    public const string PeerAssigned = "peer_assigned";
    public const string WebRtcOffer = "webrtc_offer";
    public const string WebRtcIce = "webrtc_ice";
    public const string WebRtcClose = "webrtc_close";
    public const string AudioSessionLifecycle = "audio_session_lifecycle";
}