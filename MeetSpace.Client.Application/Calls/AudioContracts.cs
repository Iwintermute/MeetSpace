namespace MeetSpace.Client.App.Calls;

public sealed record TransportProduceRequest(
    string PendingId,
    string TransportId,
    string Kind,
    string RtpParametersJson,
    string ServerProducerId,
    string? TrackType = null);

public sealed record TransportConnectRequest(
    string PendingId,
    string TransportId,
    string Direction,
    string DtlsParametersJson);

public sealed record DeviceLoadResult(
    string RecvRtpCapabilitiesJson,
    string SendRtpCapabilitiesJson);

public sealed record WebRtcTransportInfo(
    string RoomId,
    string TransportId,
    string IceParametersJson,
    string IceCandidatesJson,
    string DtlsParametersJson,
    string RouterRtpCapabilitiesJson);

public sealed record ConsumerInfo(
    string ConsumerId,
    string ProducerId,
    string Kind,
    string RtpParametersJson,
    string? TrackType = null,
    string? ProducerPeerId = null,
    bool Paused = false);

public sealed record RemoteProducerDescriptor(
    string PeerId,
    string ProducerId,
    string Kind,
    string? TrackType = null,
    bool Paused = false);
