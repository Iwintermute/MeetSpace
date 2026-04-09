namespace MeetSpace.Client.App.Calls;

public interface IMediasoupNativeClient
{
    Task InitializeAsync(string routerRtpCapabilitiesJson, CancellationToken cancellationToken = default);

    Task<string> CreateSendTransportAsync(
        string transportId,
        string iceParametersJson,
        string iceCandidatesJson,
        string dtlsParametersJson,
        CancellationToken cancellationToken = default);

    Task<string> CreateReceiveTransportAsync(
        string transportId,
        string iceParametersJson,
        string iceCandidatesJson,
        string dtlsParametersJson,
        CancellationToken cancellationToken = default);

    Task<string> GetRtpCapabilitiesJsonAsync(CancellationToken cancellationToken = default);

    Task<string> StartMicrophoneProducerAsync(
        string sendTransportId,
        string producerId,
        string? inputDeviceId,
        CancellationToken cancellationToken = default);

    Task StartAudioConsumerAsync(
        string receiveTransportId,
        string consumerId,
        string producerId,
        string kind,
        string rtpParametersJson,
        CancellationToken cancellationToken = default);

    Task SetMicrophoneEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);
}