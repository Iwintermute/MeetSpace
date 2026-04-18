namespace MeetSpace.Client.App.Calls;

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
    Task StartCameraAsync(string serverProducerId, CancellationToken cancellationToken = default);
    Task StopCameraAsync(CancellationToken cancellationToken = default);
    Task StartScreenShareAsync(string serverProducerId, CancellationToken cancellationToken = default);
    Task StopScreenShareAsync(CancellationToken cancellationToken = default);
    Task ConsumeRemoteTrackAsync(ConsumerInfo info, CancellationToken cancellationToken = default);
    Task RemoveRemoteConsumerAsync(string consumerId, CancellationToken cancellationToken = default);
    Task SetMicrophoneEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    Task SetCameraEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    Task SetScreenShareEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
    Task ResolveTransportConnectAsync(string pendingId, bool ok, string? error = null, CancellationToken cancellationToken = default);
    Task ResolveProduceAsync(string pendingId, string serverProducerId, bool ok, string? error = null, CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
}