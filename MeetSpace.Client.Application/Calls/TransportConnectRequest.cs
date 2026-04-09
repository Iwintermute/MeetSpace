namespace MeetSpace.Client.App.Calls;

public sealed record TransportConnectRequest(
    string PendingId,
    string TransportId,
    string Direction,
    string DtlsParametersJson);