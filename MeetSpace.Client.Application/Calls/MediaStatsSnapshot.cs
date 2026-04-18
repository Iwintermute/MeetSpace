namespace MeetSpace.Client.App.Calls;

public sealed record MediaStatsSnapshot(
    string SessionId,
    string RoomId,
    IReadOnlyList<RemoteProducerDescriptor> Producers,
    IReadOnlyList<string> MemberPeerIds,
    int TransportCount = 0,
    int ProducerCount = 0,
    int ConsumerCount = 0);
