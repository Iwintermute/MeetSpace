namespace MeetSpace.Client.Contracts.Calls;

public static class MediasoupProtocol
{
    public const string Object = "mediasoup";
    public const string DefaultAgent = "signaling";

    public static class Actions
    {
        public const string CreateRoom = "create_room";
        public const string JoinRoom = "join_room";
        public const string LeaveRoom = "leave_room";
        public const string OpenTransport = "open_transport";
        public const string Produce = "produce";
        public const string Consume = "consume";
        public const string WebRtcOffer = "webrtc_offer";
        public const string WebRtcIce = "webrtc_ice";
        public const string WebRtcClose = "webrtc_close";
        public const string Stats = "stats";
    }
}