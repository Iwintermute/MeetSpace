using System.IO;
using System.Security.Cryptography;
using MeetSpace.Client.Contracts.Calls;
using MeetSpace.Client.Domain.Calls;
using MeetSpace.Client.Security.Abstractions;
using MeetSpace.Client.Security.Models;
using MeetSpace.Client.Shared.Json;
using MeetSpace.Client.Shared.Results;
using MeetSpace.Client.Shared.Utilities;

namespace MeetSpace.Client.App.Calls;

public sealed class CallFileTransferService : ICallFileTransferService
{
    private const int DefaultChunkSizeBytes = 48 * 1024;
    private static readonly TimeSpan OfferAcceptTimeout = TimeSpan.FromSeconds(30);
    private const string EncryptionAlgorithm = "AES-256-GCM";

    private readonly IDirectCallFeatureClient _directCallClient;
    private readonly IEncryptionService _encryptionService;
    private readonly CallStore _callStore;
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly Dictionary<string, OutboundTransferState> _outboundTransfers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InboundTransferState> _inboundTransfers = new(StringComparer.Ordinal);
    private readonly string _incomingFilesDirectory;

    public CallFileTransferService(
        IDirectCallFeatureClient directCallClient,
        IEncryptionService encryptionService,
        CallStore callStore)
    {
        _directCallClient = directCallClient;
        _encryptionService = encryptionService;
        _callStore = callStore;
        _incomingFilesDirectory = Path.Combine(Path.GetTempPath(), "MeetSpace", "incoming-call-files");
        Directory.CreateDirectory(_incomingFilesDirectory);
    }

    public async Task<Result<string>> SendFileFromPathAsync(
        string callId,
        string filePath,
        string? mimeType = null,
        string? targetPeerId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Result<string>.Failure(new Error("direct_call.file.path_empty", "File path is empty."));

        if (!File.Exists(filePath))
            return Result<string>.Failure(new Error("direct_call.file.not_found", "File does not exist."));

        byte[] fileContent;
        try
        {
            fileContent = File.ReadAllBytes(filePath);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(new Error("direct_call.file.read_failed", ex.Message));
        }

        return await SendFileAsync(
            callId,
            Path.GetFileName(filePath),
            fileContent,
            mimeType,
            targetPeerId,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<string>> SendFileAsync(
        string callId,
        string fileName,
        byte[] fileContent,
        string? mimeType = null,
        string? targetPeerId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(callId))
            return Result<string>.Failure(new Error("direct_call.file.call_id_empty", "Call ID is empty."));
        if (string.IsNullOrWhiteSpace(fileName))
            return Result<string>.Failure(new Error("direct_call.file.name_empty", "File name is empty."));
        if (fileContent == null || fileContent.Length == 0)
            return Result<string>.Failure(new Error("direct_call.file.empty", "File content is empty."));

        var transferId = Guid.NewGuid().ToString("N");
        var chunkSize = DefaultChunkSizeBytes;
        var chunkCount = (int)Math.Ceiling(fileContent.Length / (double)chunkSize);
        var transferKey = ComposeTransferKey(callId, transferId);
        var outboundState = new OutboundTransferState(
            transferId,
            callId,
            fileName,
            mimeType,
            fileContent,
            targetPeerId,
            chunkSize,
            chunkCount);

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _outboundTransfers[transferKey] = outboundState;
        }
        finally
        {
            _sync.Release();
        }

        UpsertSnapshot(
            transferId,
            callId,
            fileName,
            mimeType,
            isOutbound: true,
            status: CallFileTransferStatus.Offered,
            totalBytes: fileContent.Length,
            transferredBytes: 0,
            totalChunks: chunkCount,
            processedChunks: 0,
            savedFilePath: null,
            error: null);

        var offerResult = await _directCallClient.OfferFileAsync(
            new DirectCallFileOfferRequest(
                CallId: callId,
                TransferId: transferId,
                FileName: fileName,
                MimeType: mimeType,
                FileSizeBytes: fileContent.LongLength,
                ChunkSizeBytes: chunkSize,
                ChunkCount: chunkCount,
                EncryptionAlgorithm: EncryptionAlgorithm,
                TargetPeerId: targetPeerId),
            cancellationToken).ConfigureAwait(false);

        if (offerResult.IsFailure)
        {
            await RemoveOutboundStateAsync(transferKey, cancellationToken).ConfigureAwait(false);
            UpsertSnapshot(
                transferId,
                callId,
                fileName,
                mimeType,
                isOutbound: true,
                status: CallFileTransferStatus.Failed,
                totalBytes: fileContent.LongLength,
                transferredBytes: 0,
                totalChunks: chunkCount,
                processedChunks: 0,
                savedFilePath: null,
                error: offerResult.Error?.Message);
            return Result<string>.Failure(offerResult.Error!);
        }

        var acceptanceResult = await WaitForAcceptanceAsync(outboundState.AcceptanceSignal.Task, cancellationToken)
            .ConfigureAwait(false);
        if (!acceptanceResult.IsSuccess)
        {
            await RemoveOutboundStateAsync(transferKey, cancellationToken).ConfigureAwait(false);
            UpsertSnapshot(
                transferId,
                callId,
                fileName,
                mimeType,
                isOutbound: true,
                status: CallFileTransferStatus.Failed,
                totalBytes: fileContent.LongLength,
                transferredBytes: 0,
                totalChunks: chunkCount,
                processedChunks: 0,
                savedFilePath: null,
                error: acceptanceResult.Error!.Message);
            return Result<string>.Failure(acceptanceResult.Error!);
        }

        if (!acceptanceResult.Value)
        {
            await RemoveOutboundStateAsync(transferKey, cancellationToken).ConfigureAwait(false);
            UpsertSnapshot(
                transferId,
                callId,
                fileName,
                mimeType,
                isOutbound: true,
                status: CallFileTransferStatus.Rejected,
                totalBytes: fileContent.LongLength,
                transferredBytes: 0,
                totalChunks: chunkCount,
                processedChunks: 0,
                savedFilePath: null,
                error: "Transfer was rejected by remote peer.");
            return Result<string>.Failure(new Error("direct_call.file.rejected", "Transfer was rejected by remote peer."));
        }

        UpsertSnapshot(
            transferId,
            callId,
            fileName,
            mimeType,
            isOutbound: true,
            status: CallFileTransferStatus.Accepted,
            totalBytes: fileContent.LongLength,
            transferredBytes: 0,
            totalChunks: chunkCount,
            processedChunks: 0,
            savedFilePath: null,
            error: null);

        var key = DeriveTransferKey(callId, transferId);
        long transferredBytes = 0;
        var fileSha256 = ComputeSha256Hex(fileContent);

        for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var offset = chunkIndex * chunkSize;
            var length = Math.Min(chunkSize, fileContent.Length - offset);
            var chunk = new byte[length];
            Buffer.BlockCopy(fileContent, offset, chunk, 0, length);

            var chunkBase64 = Convert.ToBase64String(chunk);
            EncryptedPayload encryptedPayload;
            try
            {
                encryptedPayload = await _encryptionService
                    .EncryptAsync(chunkBase64, key, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await FailOutboundTransferAsync(
                    outboundState,
                    "Chunk encryption failed: " + ex.Message,
                    cancellationToken).ConfigureAwait(false);
                return Result<string>.Failure(new Error("direct_call.file.encrypt_failed", ex.Message));
            }

            var chunkRequest = new DirectCallFileChunkRequest(
                CallId: callId,
                TransferId: transferId,
                ChunkIndex: chunkIndex,
                ChunkCount: chunkCount,
                EncryptedPayloadJson: JsonSerializer.Serialize(encryptedPayload),
                ChunkSha256: ComputeSha256Hex(chunk),
                IsLastChunk: chunkIndex == chunkCount - 1,
                TargetPeerId: targetPeerId);

            var chunkResult = await _directCallClient
                .SendFileChunkAsync(chunkRequest, cancellationToken)
                .ConfigureAwait(false);
            if (chunkResult.IsFailure)
            {
                await FailOutboundTransferAsync(
                    outboundState,
                    chunkResult.Error?.Message ?? "Chunk send failed.",
                    cancellationToken).ConfigureAwait(false);
                return Result<string>.Failure(chunkResult.Error!);
            }

            transferredBytes += length;
            UpsertSnapshot(
                transferId,
                callId,
                fileName,
                mimeType,
                isOutbound: true,
                status: CallFileTransferStatus.InProgress,
                totalBytes: fileContent.LongLength,
                transferredBytes: transferredBytes,
                totalChunks: chunkCount,
                processedChunks: chunkIndex + 1,
                savedFilePath: null,
                error: null);
        }

        var completeResult = await _directCallClient.CompleteFileTransferAsync(
            new DirectCallFileCompleteRequest(
                CallId: callId,
                TransferId: transferId,
                FileSizeBytes: fileContent.LongLength,
                ChunkCount: chunkCount,
                FileSha256: fileSha256,
                TargetPeerId: targetPeerId),
            cancellationToken).ConfigureAwait(false);

        if (completeResult.IsFailure)
        {
            await FailOutboundTransferAsync(
                outboundState,
                completeResult.Error?.Message ?? "Transfer completion failed.",
                cancellationToken).ConfigureAwait(false);
            return Result<string>.Failure(completeResult.Error!);
        }

        await RemoveOutboundStateAsync(transferKey, cancellationToken).ConfigureAwait(false);
        UpsertSnapshot(
            transferId,
            callId,
            fileName,
            mimeType,
            isOutbound: true,
            status: CallFileTransferStatus.Completed,
            totalBytes: fileContent.LongLength,
            transferredBytes: fileContent.LongLength,
            totalChunks: chunkCount,
            processedChunks: chunkCount,
            savedFilePath: null,
            error: null);

        return Result<string>.Success(transferId);
    }

    public async Task<Result> HandleInboundEventAsync(
        DirectCallFileTransferEvent transferEvent,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(transferEvent, nameof(transferEvent));

        switch (transferEvent)
        {
            case DirectCallFileOfferEvent offer:
                return await HandleOfferAsync(offer, cancellationToken).ConfigureAwait(false);
            case DirectCallFileAcceptEvent accept:
                return await HandleAcceptAsync(accept, cancellationToken).ConfigureAwait(false);
            case DirectCallFileChunkEvent chunk:
                return await HandleChunkAsync(chunk, cancellationToken).ConfigureAwait(false);
            case DirectCallFileCompleteEvent complete:
                return await HandleCompleteAsync(complete, cancellationToken).ConfigureAwait(false);
            case DirectCallFileCancelEvent cancel:
                return await HandleCancelAsync(cancel, cancellationToken).ConfigureAwait(false);
            default:
                return Result.Failure(new Error("direct_call.file.unsupported_event", "Unsupported file transfer event."));
        }
    }

    private async Task<Result> HandleOfferAsync(
        DirectCallFileOfferEvent offer,
        CancellationToken cancellationToken)
    {
        var transferKey = ComposeTransferKey(offer.CallId, offer.TransferId);
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_inboundTransfers.ContainsKey(transferKey))
            {
                _inboundTransfers[transferKey] = new InboundTransferState(
                    offer.TransferId,
                    offer.CallId,
                    offer.FileName,
                    offer.MimeType,
                    offer.FileSizeBytes,
                    offer.ChunkCount,
                    offer.SenderPeerId);
            }
        }
        finally
        {
            _sync.Release();
        }

        UpsertSnapshot(
            offer.TransferId,
            offer.CallId,
            offer.FileName,
            offer.MimeType,
            isOutbound: false,
            status: CallFileTransferStatus.Offered,
            totalBytes: offer.FileSizeBytes,
            transferredBytes: 0,
            totalChunks: offer.ChunkCount,
            processedChunks: 0,
            savedFilePath: null,
            error: null);

        var acceptResult = await _directCallClient.AcceptFileAsync(
            new DirectCallFileAcceptRequest(
                CallId: offer.CallId,
                TransferId: offer.TransferId,
                Accepted: true,
                Reason: null,
                TargetPeerId: offer.SenderPeerId),
            cancellationToken).ConfigureAwait(false);

        if (acceptResult.IsFailure)
        {
            UpsertSnapshot(
                offer.TransferId,
                offer.CallId,
                offer.FileName,
                offer.MimeType,
                isOutbound: false,
                status: CallFileTransferStatus.Failed,
                totalBytes: offer.FileSizeBytes,
                transferredBytes: 0,
                totalChunks: offer.ChunkCount,
                processedChunks: 0,
                savedFilePath: null,
                error: acceptResult.Error?.Message);
        }
        else
        {
            UpsertSnapshot(
                offer.TransferId,
                offer.CallId,
                offer.FileName,
                offer.MimeType,
                isOutbound: false,
                status: CallFileTransferStatus.Accepted,
                totalBytes: offer.FileSizeBytes,
                transferredBytes: 0,
                totalChunks: offer.ChunkCount,
                processedChunks: 0,
                savedFilePath: null,
                error: null);
        }

        return acceptResult;
    }

    private async Task<Result> HandleAcceptAsync(
        DirectCallFileAcceptEvent accept,
        CancellationToken cancellationToken)
    {
        var transferKey = ComposeTransferKey(accept.CallId, accept.TransferId);
        OutboundTransferState? outboundState = null;

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_outboundTransfers.TryGetValue(transferKey, out var found))
            {
                outboundState = found;
                found.AcceptanceSignal.TrySetResult(accept.Accepted);
            }
        }
        finally
        {
            _sync.Release();
        }

        var fileName = outboundState?.FileName ?? accept.TransferId;
        var mimeType = outboundState?.MimeType;
        var totalBytes = outboundState?.FileContent.LongLength;
        var totalChunks = outboundState?.ChunkCount;

        UpsertSnapshot(
            accept.TransferId,
            accept.CallId,
            fileName,
            mimeType,
            isOutbound: true,
            status: accept.Accepted ? CallFileTransferStatus.Accepted : CallFileTransferStatus.Rejected,
            totalBytes: totalBytes,
            transferredBytes: 0,
            totalChunks: totalChunks,
            processedChunks: 0,
            savedFilePath: null,
            error: accept.Accepted ? null : (accept.Reason ?? "Transfer rejected."));

        return Result.Success();
    }

    private async Task<Result> HandleChunkAsync(
        DirectCallFileChunkEvent chunkEvent,
        CancellationToken cancellationToken)
    {
        var transferKey = ComposeTransferKey(chunkEvent.CallId, chunkEvent.TransferId);
        InboundTransferState inboundState;

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_inboundTransfers.TryGetValue(transferKey, out inboundState!))
            {
                inboundState = new InboundTransferState(
                    chunkEvent.TransferId,
                    chunkEvent.CallId,
                    chunkEvent.TransferId,
                    null,
                    null,
                    chunkEvent.ChunkCount,
                    chunkEvent.SenderPeerId);
                _inboundTransfers[transferKey] = inboundState;
            }

            if (chunkEvent.ChunkCount.HasValue && chunkEvent.ChunkCount.Value > 0)
                inboundState.TotalChunks = chunkEvent.ChunkCount.Value;
        }
        finally
        {
            _sync.Release();
        }

        if (!TryParseEncryptedPayload(chunkEvent.EncryptedPayloadJson, out var encryptedPayload, out var parseError))
        {
            UpsertSnapshot(
                chunkEvent.TransferId,
                chunkEvent.CallId,
                inboundState.FileName,
                inboundState.MimeType,
                isOutbound: false,
                status: CallFileTransferStatus.Failed,
                totalBytes: inboundState.TotalBytes,
                transferredBytes: inboundState.ReceivedBytes,
                totalChunks: inboundState.TotalChunks,
                processedChunks: inboundState.ReceivedChunks,
                savedFilePath: null,
                error: parseError);
            return Result.Failure(new Error("direct_call.file.invalid_chunk_payload", parseError ?? "Chunk payload is invalid."));
        }

        var key = DeriveTransferKey(chunkEvent.CallId, chunkEvent.TransferId);
        string decryptedChunkBase64;
        try
        {
            decryptedChunkBase64 = await _encryptionService
                .DecryptAsync(encryptedPayload!, key, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            UpsertSnapshot(
                chunkEvent.TransferId,
                chunkEvent.CallId,
                inboundState.FileName,
                inboundState.MimeType,
                isOutbound: false,
                status: CallFileTransferStatus.Failed,
                totalBytes: inboundState.TotalBytes,
                transferredBytes: inboundState.ReceivedBytes,
                totalChunks: inboundState.TotalChunks,
                processedChunks: inboundState.ReceivedChunks,
                savedFilePath: null,
                error: ex.Message);
            return Result.Failure(new Error("direct_call.file.decrypt_failed", ex.Message));
        }

        byte[] chunkBytes;
        try
        {
            chunkBytes = Convert.FromBase64String(decryptedChunkBase64);
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("direct_call.file.chunk_decode_failed", ex.Message));
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!inboundState.Chunks.ContainsKey(chunkEvent.ChunkIndex))
            {
                inboundState.Chunks[chunkEvent.ChunkIndex] = chunkBytes;
                inboundState.ReceivedBytes += chunkBytes.LongLength;
                inboundState.ReceivedChunks += 1;
            }
        }
        finally
        {
            _sync.Release();
        }

        UpsertSnapshot(
            chunkEvent.TransferId,
            chunkEvent.CallId,
            inboundState.FileName,
            inboundState.MimeType,
            isOutbound: false,
            status: CallFileTransferStatus.InProgress,
            totalBytes: inboundState.TotalBytes,
            transferredBytes: inboundState.ReceivedBytes,
            totalChunks: inboundState.TotalChunks,
            processedChunks: inboundState.ReceivedChunks,
            savedFilePath: null,
            error: null);

        return Result.Success();
    }

    private async Task<Result> HandleCompleteAsync(
        DirectCallFileCompleteEvent completeEvent,
        CancellationToken cancellationToken)
    {
        var transferKey = ComposeTransferKey(completeEvent.CallId, completeEvent.TransferId);
        InboundTransferState? inboundState = null;

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _inboundTransfers.TryGetValue(transferKey, out inboundState);
            if (inboundState != null)
            {
                if (completeEvent.FileSizeBytes.HasValue && completeEvent.FileSizeBytes.Value > 0)
                    inboundState.TotalBytes = completeEvent.FileSizeBytes.Value;
                if (completeEvent.ChunkCount.HasValue && completeEvent.ChunkCount.Value > 0)
                    inboundState.TotalChunks = completeEvent.ChunkCount.Value;
            }
        }
        finally
        {
            _sync.Release();
        }

        if (inboundState == null)
        {
            return Result.Failure(new Error("direct_call.file.complete_without_offer", "Transfer state was not found."));
        }

        if (inboundState.TotalChunks.HasValue && inboundState.ReceivedChunks < inboundState.TotalChunks.Value)
        {
            return Result.Failure(new Error(
                "direct_call.file.incomplete_chunks",
                "Not all file chunks were received before completion."));
        }

        byte[] fileContent;
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (inboundState.Chunks.Count == 0)
            {
                return Result.Failure(new Error("direct_call.file.empty_payload", "No chunks were received."));
            }

            var orderedChunkIndexes = inboundState.Chunks.Keys.OrderBy(x => x).ToList();
            using var buffer = new MemoryStream();
            foreach (var chunkIndex in orderedChunkIndexes)
            {
                var chunk = inboundState.Chunks[chunkIndex];
                buffer.Write(chunk, 0, chunk.Length);
            }
            fileContent = buffer.ToArray();
        }
        finally
        {
            _sync.Release();
        }

        if (completeEvent.FileSizeBytes.HasValue && completeEvent.FileSizeBytes.Value != fileContent.LongLength)
        {
            return Result.Failure(new Error(
                "direct_call.file.size_mismatch",
                "Received file size does not match declared size."));
        }

        if (!string.IsNullOrWhiteSpace(completeEvent.FileSha256))
        {
            var actualSha = ComputeSha256Hex(fileContent);
            if (!string.Equals(actualSha, completeEvent.FileSha256, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure(new Error(
                    "direct_call.file.hash_mismatch",
                    "Received file integrity check failed."));
            }
        }

        var safeFileName = SanitizeFileName(inboundState.FileName);
        var destinationPath = Path.Combine(
            _incomingFilesDirectory,
            $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{inboundState.TransferId}_{safeFileName}");

        try
        {
            File.WriteAllBytes(destinationPath, fileContent);
        }
        catch (Exception ex)
        {
            return Result.Failure(new Error("direct_call.file.write_failed", ex.Message));
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _inboundTransfers.Remove(transferKey);
        }
        finally
        {
            _sync.Release();
        }

        UpsertSnapshot(
            completeEvent.TransferId,
            completeEvent.CallId,
            inboundState.FileName,
            inboundState.MimeType,
            isOutbound: false,
            status: CallFileTransferStatus.Completed,
            totalBytes: fileContent.LongLength,
            transferredBytes: fileContent.LongLength,
            totalChunks: inboundState.TotalChunks ?? inboundState.ReceivedChunks,
            processedChunks: inboundState.ReceivedChunks,
            savedFilePath: destinationPath,
            error: null);

        return Result.Success();
    }

    private async Task<Result> HandleCancelAsync(
        DirectCallFileCancelEvent cancelEvent,
        CancellationToken cancellationToken)
    {
        var transferKey = ComposeTransferKey(cancelEvent.CallId, cancelEvent.TransferId);
        OutboundTransferState? outboundState = null;
        InboundTransferState? inboundState = null;

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_outboundTransfers.TryGetValue(transferKey, out outboundState))
            {
                outboundState.AcceptanceSignal.TrySetResult(false);
                _outboundTransfers.Remove(transferKey);
            }

            if (_inboundTransfers.TryGetValue(transferKey, out inboundState))
            {
                _inboundTransfers.Remove(transferKey);
            }
        }
        finally
        {
            _sync.Release();
        }

        UpsertSnapshot(
            cancelEvent.TransferId,
            cancelEvent.CallId,
            outboundState?.FileName ?? inboundState?.FileName ?? cancelEvent.TransferId,
            outboundState?.MimeType ?? inboundState?.MimeType,
            isOutbound: outboundState != null,
            status: CallFileTransferStatus.Cancelled,
            totalBytes: outboundState?.FileContent.LongLength ?? inboundState?.TotalBytes,
            transferredBytes: outboundState != null ? 0 : inboundState?.ReceivedBytes ?? 0,
            totalChunks: outboundState?.ChunkCount ?? inboundState?.TotalChunks,
            processedChunks: outboundState != null ? 0 : inboundState?.ReceivedChunks ?? 0,
            savedFilePath: null,
            error: cancelEvent.Reason);

        return Result.Success();
    }

    private async Task<Result<bool>> WaitForAcceptanceAsync(
        Task<bool> acceptanceTask,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeoutTask = Task.Delay(OfferAcceptTimeout, timeoutCts.Token);
        var completedTask = await Task.WhenAny(acceptanceTask, timeoutTask).ConfigureAwait(false);
        if (completedTask == timeoutTask)
        {
            return Result<bool>.Failure(new Error(
                "direct_call.file.accept_timeout",
                "Timed out waiting for remote acceptance."));
        }

        timeoutCts.Cancel();
        return Result<bool>.Success(await acceptanceTask.ConfigureAwait(false));
    }

    private async Task FailOutboundTransferAsync(
        OutboundTransferState outboundState,
        string reason,
        CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _outboundTransfers.Remove(ComposeTransferKey(outboundState.CallId, outboundState.TransferId));
        }
        finally
        {
            _sync.Release();
        }

        UpsertSnapshot(
            outboundState.TransferId,
            outboundState.CallId,
            outboundState.FileName,
            outboundState.MimeType,
            isOutbound: true,
            status: CallFileTransferStatus.Failed,
            totalBytes: outboundState.FileContent.LongLength,
            transferredBytes: 0,
            totalChunks: outboundState.ChunkCount,
            processedChunks: 0,
            savedFilePath: null,
            error: reason);

        _ = _directCallClient.CancelFileTransferAsync(
            new DirectCallFileCancelRequest(
                CallId: outboundState.CallId,
                TransferId: outboundState.TransferId,
                Reason: reason,
                TargetPeerId: outboundState.TargetPeerId),
            cancellationToken);
    }

    private async Task RemoveOutboundStateAsync(string transferKey, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _outboundTransfers.Remove(transferKey);
        }
        finally
        {
            _sync.Release();
        }
    }

    private void UpsertSnapshot(
        string transferId,
        string callId,
        string fileName,
        string? mimeType,
        bool isOutbound,
        CallFileTransferStatus status,
        long? totalBytes,
        long transferredBytes,
        int? totalChunks,
        int processedChunks,
        string? savedFilePath,
        string? error)
    {
        _callStore.UpsertFileTransfer(new CallFileTransferItem(
            TransferId: transferId,
            CallId: callId,
            FileName: fileName,
            MimeType: mimeType,
            IsOutbound: isOutbound,
            Status: status,
            TotalBytes: totalBytes,
            TransferredBytes: transferredBytes,
            TotalChunks: totalChunks,
            ProcessedChunks: processedChunks,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            SavedFilePath: savedFilePath,
            Error: error));
    }

    private static string ComposeTransferKey(string callId, string transferId)
    {
        return callId + "|" + transferId;
    }

    private static byte[] DeriveTransferKey(string callId, string transferId)
    {
        using var sha = SHA256.Create();
        var material = Encoding.UTF8.GetBytes("meetspace-direct-call-file-transfer-v1:" + callId + ":" + transferId);
        return sha.ComputeHash(material);
    }

    private static string ComputeSha256Hex(byte[] value)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(value);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var item in hash)
            builder.Append(item.ToString("x2"));
        return builder.ToString();
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "incoming.bin";

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(fileName.Length);
        foreach (var ch in fileName)
            builder.Append(invalid.Contains(ch) ? '_' : ch);

        var sanitized = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "incoming.bin" : sanitized;
    }

    private static bool TryParseEncryptedPayload(
        string? serializedPayload,
        out EncryptedPayload? encryptedPayload,
        out string? error)
    {
        encryptedPayload = null;
        error = null;

        if (string.IsNullOrWhiteSpace(serializedPayload))
        {
            error = "Encrypted chunk payload is empty.";
            return false;
        }

        try
        {
            var normalized = serializedPayload.Trim();
            if (normalized.Length == 0)
            {
                error = "Encrypted chunk payload is empty.";
                return false;
            }

            if (normalized.StartsWith("\"", StringComparison.Ordinal))
            {
                normalized = JsonSerializer.Deserialize<string>(normalized) ?? string.Empty;
            }

            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Encrypted chunk payload must be a JSON object.";
                return false;
            }

            var root = doc.RootElement;
            var algorithm = root.GetString("Algorithm", "algorithm", "encryptionAlgorithm", "encryption_algorithm")
                ?? EncryptionAlgorithm;
            var cipherTextBase64 = root.GetString(
                "CipherTextBase64",
                "cipherTextBase64",
                "cipher_text_base64",
                "chunkBase64",
                "chunk_base64");
            var ivBase64 = root.GetString("IvBase64", "ivBase64", "nonceBase64", "nonce_base64", "iv", "nonce");
            var tagBase64 = root.GetString("TagBase64", "tagBase64", "tag", "tag_base64");

            if (string.IsNullOrWhiteSpace(cipherTextBase64) || string.IsNullOrWhiteSpace(ivBase64))
            {
                error = "Encrypted chunk payload is missing cipher text or nonce.";
                return false;
            }

            Dictionary<string, string>? metadata = null;
            var metadataObject = root.GetObject("Metadata", "metadata");
            if (metadataObject.HasValue)
            {
                metadata = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in metadataObject.Value.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            metadata[property.Name] = value!;
                    }
                }
            }

            encryptedPayload = new EncryptedPayload(
                algorithm,
                cipherTextBase64!,
                ivBase64!,
                tagBase64,
                metadata);

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private sealed class OutboundTransferState
    {
        public OutboundTransferState(
            string transferId,
            string callId,
            string fileName,
            string? mimeType,
            byte[] fileContent,
            string? targetPeerId,
            int chunkSizeBytes,
            int chunkCount)
        {
            TransferId = transferId;
            CallId = callId;
            FileName = fileName;
            MimeType = mimeType;
            FileContent = fileContent;
            TargetPeerId = targetPeerId;
            ChunkSizeBytes = chunkSizeBytes;
            ChunkCount = chunkCount;
            AcceptanceSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public string TransferId { get; }
        public string CallId { get; }
        public string FileName { get; }
        public string? MimeType { get; }
        public byte[] FileContent { get; }
        public string? TargetPeerId { get; }
        public int ChunkSizeBytes { get; }
        public int ChunkCount { get; }
        public TaskCompletionSource<bool> AcceptanceSignal { get; }
    }

    private sealed class InboundTransferState
    {
        public InboundTransferState(
            string transferId,
            string callId,
            string fileName,
            string? mimeType,
            long? totalBytes,
            int? totalChunks,
            string? senderPeerId)
        {
            TransferId = transferId;
            CallId = callId;
            FileName = fileName;
            MimeType = mimeType;
            TotalBytes = totalBytes;
            TotalChunks = totalChunks;
            SenderPeerId = senderPeerId;
        }

        public string TransferId { get; }
        public string CallId { get; }
        public string FileName { get; }
        public string? MimeType { get; }
        public string? SenderPeerId { get; }
        public long? TotalBytes { get; set; }
        public int? TotalChunks { get; set; }
        public long ReceivedBytes { get; set; }
        public int ReceivedChunks { get; set; }
        public Dictionary<int, byte[]> Chunks { get; } = new();
    }
}
