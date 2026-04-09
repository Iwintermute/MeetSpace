namespace MeetSpace.Client.App.Calls;

public sealed record TransportProduceRequest(
    string PendingId,
    string TransportId,
    string Kind,
    string RtpParametersJson,
    string ServerProducerId);