namespace MeetSpace.Client.Domain.Calls;

public enum CallFileTransferStatus
{
    Offered = 0,
    Accepted = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4,
    Rejected = 5,
    Failed = 6
}

public sealed record CallFileTransferItem(
    string TransferId,
    string CallId,
    string FileName,
    string? MimeType,
    bool IsOutbound,
    CallFileTransferStatus Status,
    long? TotalBytes,
    long TransferredBytes,
    int? TotalChunks,
    int ProcessedChunks,
    DateTimeOffset UpdatedAtUtc,
    string? SavedFilePath = null,
    string? Error = null);
