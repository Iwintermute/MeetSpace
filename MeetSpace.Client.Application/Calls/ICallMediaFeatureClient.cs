using MeetSpace.Client.Shared.Results;

namespace MeetSpace.Client.App.Calls;

public interface ICallMediaFeatureClient
{
    Task<Result<WebRtcTransportInfo>> OpenTransportAsync(
        string sessionId,
        string transportId,
        CancellationToken cancellationToken = default);

    Task<Result> ConnectTransportAsync(
        string sessionId,
        string transportId,
        string dtlsParametersJson,
        CancellationToken cancellationToken = default);

    Task<Result> PublishTrackAsync(
        string sessionId,
        string transportId,
        string producerId,
        string kind,
        string trackType,
        string rtpParametersJson,
        CancellationToken cancellationToken = default);

    Task<Result<ConsumerInfo>> ConsumeTrackAsync(
        string sessionId,
        string transportId,
        string producerId,
        string recvRtpCapabilitiesJson,
        string? consumerId = null,
        CancellationToken cancellationToken = default);

    Task<Result> ConsumerReadyAsync(
        string sessionId,
        string consumerId,
        CancellationToken cancellationToken = default);

    Task<Result<MediaStatsSnapshot>> GetMediaStatsAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<Result> CloseSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default);
}
