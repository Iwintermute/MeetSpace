using MeetSpace.Client.Shared.Results;
using System.Threading;
using System.Threading.Tasks;

namespace MeetSpace.Client.App.Calls;

public interface IMediasoupFeatureClient
{
    Task<Result> CreateRoomIfMissingAsync(string roomId, CancellationToken cancellationToken = default);
    Task<Result> JoinRoomAsync(string roomId, CancellationToken cancellationToken = default);
    Task<Result<WebRtcTransportInfo>> OpenTransportAsync(string roomId, string transportId, CancellationToken cancellationToken = default);
    Task<Result> ConnectTransportAsync(string roomId, string transportId, string dtlsParametersJson, CancellationToken cancellationToken = default);
    Task<Result> ProduceAsync(string roomId, string transportId, string producerId, string kind, string rtpParametersJson, CancellationToken cancellationToken = default);
    Task<Result<ConsumerInfo>> ConsumeAsync(string roomId, string transportId, string producerId, string recvRtpCapabilitiesJson, CancellationToken cancellationToken = default);
    Task<Result> CloseAsync(string roomId, CancellationToken cancellationToken = default);
}