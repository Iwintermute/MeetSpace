using System;
using System.Collections.Generic;

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
    string RouterRtpCapabilitiesJson,
    string IceServersJson = "[]");

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
    bool Paused = false,
    double PacketLossPercent = 0,
    double BitrateKbps = 0,
    double Jitter = 0,
    double RoundTripTime = 0);

public sealed record CallQualityTrackSample(
    string Direction,
    string Kind,
    double BitrateKbps,
    double PacketLossPercent,
    double Jitter,
    double RoundTripTime);

public sealed record CallQualitySnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyList<CallQualityTrackSample> Tracks);

public sealed record IceConnectionStateChanged(
    string Direction,
    string State,
    DateTimeOffset Timestamp);
