namespace MeetSpace.Client.App.Calls;

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
    string RtpParametersJson);

public interface IAudioCallEngine
{
    event Func<TransportConnectRequest, Task>? TransportConnectRequired;
    event Func<TransportProduceRequest, Task>? TransportProduceRequired;

    string? RecvRtpCapabilitiesJson { get; }

    Task AttachAsync(IAudioBridgeHost host, CancellationToken cancellationToken = default);
    Task<DeviceLoadResult> LoadDeviceAsync(string routerRtpCapabilitiesJson, CancellationToken cancellationToken = default);
    Task CreateSendTransportAsync(WebRtcTransportInfo info, CancellationToken cancellationToken = default);
    Task CreateRecvTransportAsync(WebRtcTransportInfo info, CancellationToken cancellationToken = default);
    Task StartMicrophoneAsync(string serverProducerId, CancellationToken cancellationToken = default);
    Task ConsumeRemoteAudioAsync(ConsumerInfo info, CancellationToken cancellationToken = default);
    Task SetMicrophoneEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    Task ResolveTransportConnectAsync(string pendingId, bool ok, string? error = null, CancellationToken cancellationToken = default);
    Task ResolveProduceAsync(string pendingId, string serverProducerId, bool ok, string? error = null, CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}