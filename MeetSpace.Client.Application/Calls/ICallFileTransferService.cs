using MeetSpace.Client.Contracts.Calls;
using MeetSpace.Client.Shared.Results;

namespace MeetSpace.Client.App.Calls;

public interface ICallFileTransferService
{
    Task<Result<string>> SendFileAsync(
        string callId,
        string fileName,
        byte[] fileContent,
        string? mimeType = null,
        string? targetPeerId = null,
        CancellationToken cancellationToken = default);

    Task<Result<string>> SendFileFromPathAsync(
        string callId,
        string filePath,
        string? mimeType = null,
        string? targetPeerId = null,
        CancellationToken cancellationToken = default);

    Task<Result> HandleInboundEventAsync(
        DirectCallFileTransferEvent transferEvent,
        CancellationToken cancellationToken = default);
}
