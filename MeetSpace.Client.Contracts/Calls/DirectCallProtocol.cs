#nullable enable

namespace MeetSpace.Client.Contracts.Calls;

public static class DirectCallProtocol
{
    public const string Object = "direct_call";

    public static class Agents
    {
        public const string Lifecycle = "lifecycle";
    }

    public static class Actions
    {
        public const string CreateCall = "create_call";
        public const string AcceptCall = "accept_call";
        public const string DeclineCall = "decline_call";
        public const string HangupCall = "hangup_call";
        public const string ListActiveCalls = "list_active_calls";

        public const string OpenTransport = "open_transport";
        public const string PublishTrack = "publish_track";
        public const string ConsumeTrack = "consume_track";
        public const string ConsumerReady = "consumer_ready";
        public const string WebRtcOffer = "webrtc_offer";
        public const string WebRtcIce = "webrtc_ice";
        public const string WebRtcClose = "webrtc_close";
        public const string MediaStats = "media_stats";
    }

    public static readonly string[] CreateCallActions =
    {
        Actions.CreateCall
    };

    public static readonly string[] AcceptCallActions =
    {
        Actions.AcceptCall
    };

    public static readonly string[] DeclineCallActions =
    {
        Actions.DeclineCall
    };

    public static readonly string[] HangupCallActions =
    {
        Actions.HangupCall
    };

    public static readonly string[] ListActiveCallsActions =
    {
        Actions.ListActiveCalls
    };

    public static readonly string[] OpenTransportActions =
    {
        Actions.OpenTransport
    };

    public static readonly string[] PublishTrackActions =
    {
        Actions.PublishTrack
    };

    public static readonly string[] ConsumeTrackActions =
    {
        Actions.ConsumeTrack
    };

    public static readonly string[] ConsumerReadyActions =
    {
        Actions.ConsumerReady
    };

    public static readonly string[] WebRtcOfferActions =
    {
        Actions.WebRtcOffer
    };

    public static readonly string[] WebRtcIceActions =
    {
        Actions.WebRtcIce
    };

    public static readonly string[] WebRtcCloseActions =
    {
        Actions.WebRtcClose
    };

    public static readonly string[] MediaStatsActions =
    {
        Actions.MediaStats
    };
}
