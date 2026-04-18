
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
        public const string ConnectTransport = "connect_transport";
        public const string Produce = "produce";
        public const string Consume = "consume";
        public const string ListProducers = "list_producers";

        public const string LegacyOpenTransport = "create_transport";
        public const string LegacyConnectDtls = "connect_dtls";
        public const string LegacyWebRtcOffer = "webrtc_offer";
        public const string LegacyWebRtcClose = "webrtc_close";
    }

    public static readonly string[] CreateRoomActions =
    {
        Actions.CreateRoom
    };

    public static readonly string[] JoinRoomActions =
    {
        Actions.JoinRoom
    };

    public static readonly string[] OpenTransportActions =
    {
        Actions.OpenTransport,
        Actions.LegacyOpenTransport
    };

    public static readonly string[] ConnectTransportActions =
    {
        Actions.ConnectTransport,
        Actions.LegacyConnectDtls,
        Actions.LegacyWebRtcOffer
    };

    public static readonly string[] ProduceActions =
    {
        Actions.Produce
    };

    public static readonly string[] ConsumeActions =
    {
        Actions.Consume
    };

    public static readonly string[] ListProducersActions =
    {
        Actions.ListProducers
    };

    public static readonly string[] CloseActions =
    {
        Actions.LeaveRoom,
        Actions.LegacyWebRtcClose
    };
}