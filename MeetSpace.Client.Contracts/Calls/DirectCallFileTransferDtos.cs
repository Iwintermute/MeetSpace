#nullable enable

namespace MeetSpace.Client.Contracts.Calls;

public enum DirectCallFileTransferEventKind
{
    Unknown = 0,
    Offer = 1,
    Accept = 2,
    Chunk = 3,
    Complete = 4,
    Cancel = 5
}

public abstract record DirectCallFileTransferEvent(
    DirectCallFileTransferEventKind Kind,
    string EventType,
    string CallId,
    string TransferId,
    string? SenderPeerId,
    string? TargetPeerId,
    DateTimeOffset? SentAtUtc);

public sealed record DirectCallFileOfferEvent(
    string EventType,
    string CallId,
    string TransferId,
    string? SenderPeerId,
    string? TargetPeerId,
    DateTimeOffset? SentAtUtc,
    string FileName,
    string? MimeType,
    long? FileSizeBytes,
    int? ChunkSizeBytes,
    int? ChunkCount,
    string? EncryptionAlgorithm)
    : DirectCallFileTransferEvent(
        DirectCallFileTransferEventKind.Offer,
        EventType,
        CallId,
        TransferId,
        SenderPeerId,
        TargetPeerId,
        SentAtUtc);

public sealed record DirectCallFileAcceptEvent(
    string EventType,
    string CallId,
    string TransferId,
    string? SenderPeerId,
    string? TargetPeerId,
    DateTimeOffset? SentAtUtc,
    bool Accepted,
    string? Reason)
    : DirectCallFileTransferEvent(
        DirectCallFileTransferEventKind.Accept,
        EventType,
        CallId,
        TransferId,
        SenderPeerId,
        TargetPeerId,
        SentAtUtc);

public sealed record DirectCallFileChunkEvent(
    string EventType,
    string CallId,
    string TransferId,
    string? SenderPeerId,
    string? TargetPeerId,
    DateTimeOffset? SentAtUtc,
    int ChunkIndex,
    int? ChunkCount,
    string? EncryptedPayloadJson,
    string? ChunkSha256,
    bool? IsLastChunk)
    : DirectCallFileTransferEvent(
        DirectCallFileTransferEventKind.Chunk,
        EventType,
        CallId,
        TransferId,
        SenderPeerId,
        TargetPeerId,
        SentAtUtc);

public sealed record DirectCallFileCompleteEvent(
    string EventType,
    string CallId,
    string TransferId,
    string? SenderPeerId,
    string? TargetPeerId,
    DateTimeOffset? SentAtUtc,
    long? FileSizeBytes,
    int? ChunkCount,
    string? FileSha256)
    : DirectCallFileTransferEvent(
        DirectCallFileTransferEventKind.Complete,
        EventType,
        CallId,
        TransferId,
        SenderPeerId,
        TargetPeerId,
        SentAtUtc);

public sealed record DirectCallFileCancelEvent(
    string EventType,
    string CallId,
    string TransferId,
    string? SenderPeerId,
    string? TargetPeerId,
    DateTimeOffset? SentAtUtc,
    string? Reason)
    : DirectCallFileTransferEvent(
        DirectCallFileTransferEventKind.Cancel,
        EventType,
        CallId,
        TransferId,
        SenderPeerId,
        TargetPeerId,
        SentAtUtc);

public sealed record DirectCallFileOfferRequest(
    string CallId,
    string TransferId,
    string FileName,
    string? MimeType,
    long FileSizeBytes,
    int ChunkSizeBytes,
    int ChunkCount,
    string EncryptionAlgorithm,
    string? TargetPeerId = null,
    string? ClientRequestId = null);

public sealed record DirectCallFileAcceptRequest(
    string CallId,
    string TransferId,
    bool Accepted = true,
    string? Reason = null,
    string? TargetPeerId = null,
    string? ClientRequestId = null);

public sealed record DirectCallFileChunkRequest(
    string CallId,
    string TransferId,
    int ChunkIndex,
    int ChunkCount,
    string EncryptedPayloadJson,
    string? ChunkSha256 = null,
    bool? IsLastChunk = null,
    string? TargetPeerId = null,
    string? ClientRequestId = null);

public sealed record DirectCallFileCompleteRequest(
    string CallId,
    string TransferId,
    long? FileSizeBytes = null,
    int? ChunkCount = null,
    string? FileSha256 = null,
    string? TargetPeerId = null,
    string? ClientRequestId = null);

public sealed record DirectCallFileCancelRequest(
    string CallId,
    string TransferId,
    string? Reason = null,
    string? TargetPeerId = null,
    string? ClientRequestId = null);
