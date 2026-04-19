namespace MeetSpace.Client.Contracts.Conference;

public static class ConferenceProtocol
{
    public const string Object = "conference";

    public static class Agents
    {
        public const string Lifecycle = "lifecycle";
        public const string Membership = "membership";
        public const string Directory = "lifecycle";
    }

    public static class Actions
    {
        public const string CreateConference = "create_conference";
        public const string GetConference = "get_conference";
        public const string CloseConference = "close_conference";
        public const string JoinConference = "join_conference";
        public const string LeaveConference = "leave_conference";
        public const string ListMembers = "list_members";
        public const string ListUserConferences = "list_user_conferences";

        public const string OpenTransport = "open_transport";
        public const string PublishTrack = "publish_track";
        public const string PauseTrack = "pause_track";
        public const string ResumeTrack = "resume_track";
        public const string CloseTrack = "close_track";
        public const string ConsumeTrack = "consume_track";
        public const string ConsumerReady = "consumer_ready";
        public const string WebRtcOffer = "webrtc_offer";
        public const string WebRtcIce = "webrtc_ice";
        public const string WebRtcClose = "webrtc_close";
        public const string MediaStats = "media_stats";
    }

    public static readonly string[] CreateActions =
    {
        Actions.CreateConference,
        "create"
    };

    public static readonly string[] GetActions =
    {
        Actions.GetConference,
        "get"
    };

    public static readonly string[] CloseActions =
    {
        Actions.CloseConference,
        "close"
    };

    public static readonly string[] JoinActions =
    {
        Actions.JoinConference,
        "join"
    };

    public static readonly string[] LeaveActions =
    {
        Actions.LeaveConference,
        "leave"
    };

    public static readonly string[] ListMembersActions =
    {
        Actions.ListMembers,
        "get_members"
    };

    public static readonly string[] ListConferencesActions =
    {
        Actions.ListUserConferences,
        "list_user_conferences",
        "list_conferences",
        "list"
    };

    public static readonly string[] OpenTransportActions =
    {
        Actions.OpenTransport
    };

    public static readonly string[] PublishTrackActions =
    {
        Actions.PublishTrack
    };
    public static readonly string[] PauseTrackActions =
    {
        Actions.PauseTrack
    };

    public static readonly string[] ResumeTrackActions =
    {
        Actions.ResumeTrack
    };

    public static readonly string[] CloseTrackActions =
    {
        Actions.CloseTrack
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