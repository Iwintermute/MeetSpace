namespace MeetSpace.Client.Contracts.Protocol;

public static class ProtocolMessageTypes
{
    public const string DispatchResult = "dispatch_result";
    public const string PeerAssigned = "peer_assigned";

    public const string DirectChatMessage = "direct_chat_message";
    public const string ConferenceChatMessage = "chat_message";

    public const string ConferenceUpdated = "conference_updated";
    public const string ConferenceMembersUpdated = "conference_members_updated";
    public const string DirectCallInvite = "direct_call_invite";
    public const string DirectCallAccepted = "direct_call_accepted";
    public const string DirectCallDeclined = "direct_call_declined";
    public const string DirectCallEnded = "direct_call_ended";

    public const string RoomState = "room_state";
    public const string SessionStarted = "session_started";
    public const string SessionEnded = "session_ended";
    public const string PeerJoined = "peer_joined";
    public const string PeerLeft = "peer_left";
    public const string TransportOpened = "transport_opened";
    public const string TrackPublished = "track_published";
    public const string TrackClosed = "track_closed";
    public const string ConsumerResumed = "consumer_resumed";
    public const string SessionClosed = "session_closed";
    public const string TransportError = "transport_error";

    public const string MediaProducerAdded = "media_producer_added";
    public const string MediaProducerRemoved = "media_producer_removed";
}