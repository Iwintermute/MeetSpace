namespace MeetSpace.Client.App.Calls;

public sealed class NullAudioCallEngine : IAudioCallEngine
{
    public event Func<TransportConnectRequest, Task>? TransportConnectRequired;
    public event Func<TransportProduceRequest, Task>? TransportProduceRequired;

    public string? RecvRtpCapabilitiesJson { get; private set; } = "{}";

    public Task AttachAsync(IAudioBridgeHost host, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<DeviceLoadResult> LoadDeviceAsync(string routerRtpCapabilitiesJson, CancellationToken cancellationToken = default)
    {
        RecvRtpCapabilitiesJson = string.IsNullOrWhiteSpace(routerRtpCapabilitiesJson)
            ? "{}"
            : routerRtpCapabilitiesJson;

        return Task.FromResult(new DeviceLoadResult(
            RecvRtpCapabilitiesJson,
            RecvRtpCapabilitiesJson));
    }

    public Task CreateSendTransportAsync(WebRtcTransportInfo info, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task CreateRecvTransportAsync(WebRtcTransportInfo info, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task StartMicrophoneAsync(string serverProducerId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
    public Task StartCameraAsync(string serverProducerId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task StopCameraAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task StartScreenShareAsync(string serverProducerId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task StopScreenShareAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task ConsumeRemoteTrackAsync(ConsumerInfo info, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task RemoveRemoteConsumerAsync(string consumerId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task SetMicrophoneEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task SetCameraEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task SetScreenShareEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task ResolveTransportConnectAsync(string pendingId, bool ok, string? error = null, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task ResolveProduceAsync(string pendingId, string serverProducerId, bool ok, string? error = null, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}