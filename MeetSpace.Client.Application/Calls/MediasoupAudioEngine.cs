namespace MeetSpace.Client.App.Calls;

public sealed class MediasoupAudioEngine : IAudioCallMediaEngine
{
    private readonly IMediasoupNativeClient _nativeClient;
    private bool _initialized;
    private bool _deviceLoaded;

    public MediasoupAudioEngine(IMediasoupNativeClient nativeClient)
    {
        _nativeClient = nativeClient;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _initialized = true;
        return Task.CompletedTask;
    }

    public async Task EnsureLoadedAsync(string routerRtpCapabilitiesJson, CancellationToken cancellationToken = default)
    {
        if (!_initialized)
            await InitializeAsync(cancellationToken).ConfigureAwait(false);

        if (_deviceLoaded)
            return;

        await _nativeClient.InitializeAsync(routerRtpCapabilitiesJson, cancellationToken).ConfigureAwait(false);
        _deviceLoaded = true;
    }

    public Task<string> CreateSendTransportAsync(
        string transportId,
        string iceParametersJson,
        string iceCandidatesJson,
        string dtlsParametersJson,
        CancellationToken cancellationToken = default)
    {
        return _nativeClient.CreateSendTransportAsync(
            transportId,
            iceParametersJson,
            iceCandidatesJson,
            dtlsParametersJson,
            cancellationToken);
    }

    public Task<string> CreateReceiveTransportAsync(
        string transportId,
        string iceParametersJson,
        string iceCandidatesJson,
        string dtlsParametersJson,
        CancellationToken cancellationToken = default)
    {
        return _nativeClient.CreateReceiveTransportAsync(
            transportId,
            iceParametersJson,
            iceCandidatesJson,
            dtlsParametersJson,
            cancellationToken);
    }

    public Task<string> GetRtpCapabilitiesJsonAsync(CancellationToken cancellationToken = default)
    {
        return _nativeClient.GetRtpCapabilitiesJsonAsync(cancellationToken);
    }

    public Task<string> StartLocalAudioAsync(
        string sendTransportId,
        string producerId,
        string? inputDeviceId,
        CancellationToken cancellationToken = default)
    {
        return _nativeClient.StartMicrophoneProducerAsync(
            sendTransportId,
            producerId,
            inputDeviceId,
            cancellationToken);
    }

    public Task StartRemoteAudioAsync(
        string receiveTransportId,
        string consumerId,
        string producerId,
        string kind,
        string rtpParametersJson,
        CancellationToken cancellationToken = default)
    {
        return _nativeClient.StartAudioConsumerAsync(
            receiveTransportId,
            consumerId,
            producerId,
            kind,
            rtpParametersJson,
            cancellationToken);
    }

    public Task SetMicrophoneEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        return _nativeClient.SetMicrophoneEnabledAsync(enabled, cancellationToken);
    }

    public Task CloseCallAsync(CancellationToken cancellationToken = default)
    {
        return _nativeClient.CloseAsync(cancellationToken);
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await _nativeClient.CloseAsync(cancellationToken).ConfigureAwait(false);
        _deviceLoaded = false;
        _initialized = false;
    }
}