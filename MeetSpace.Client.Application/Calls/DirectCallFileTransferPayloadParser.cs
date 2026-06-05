using System.Text.Json;
using MeetSpace.Client.Contracts.Calls;
using MeetSpace.Client.Contracts.Protocol;
using MeetSpace.Client.Shared.Json;

namespace MeetSpace.Client.App.Calls;

internal static class DirectCallFileTransferPayloadParser
{
    public static bool IsFileTransferType(string? type)
    {
        return ResolveKind(type) != DirectCallFileTransferEventKind.Unknown;
    }

    public static bool TryParseEvent(
        FeatureResponseEnvelope envelope,
        out DirectCallFileTransferEvent? transferEvent)
    {
        transferEvent = null;

        var kind = ResolveKind(envelope.Type);
        if (kind == DirectCallFileTransferEventKind.Unknown)
            return false;

        var payload = ResolvePayloadObject(envelope);
        var callId = ResolveString(
            envelope,
            payload,
            "callId",
            "call_id",
            "sessionId",
            "session_id");
        var transferId = ResolveString(
            envelope,
            payload,
            "transferId",
            "transfer_id",
            "fileTransferId",
            "file_transfer_id",
            "fileId",
            "file_id");

        if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(transferId))
            return false;

        var senderPeerId = ResolveString(
            envelope,
            payload,
            "senderPeerId",
            "sender_peer_id",
            "fromPeerId",
            "from_peer_id",
            "actorPeerId",
            "actor_peer_id",
            "peerId",
            "peer");
        var targetPeerId = ResolveString(
            envelope,
            payload,
            "targetPeerId",
            "target_peer_id",
            "toPeerId",
            "to_peer_id",
            "recipientPeerId",
            "recipient_peer_id");
        var sentAtUtc = ResolveTimestamp(envelope, payload);
        var eventType = envelope.Type;

        switch (kind)
        {
            case DirectCallFileTransferEventKind.Offer:
            {
                var fileName = ResolveString(envelope, payload, "fileName", "file_name", "name");
                var mimeType = ResolveString(envelope, payload, "mimeType", "mime_type", "contentType", "content_type");
                var fileSizeBytes = ResolveInt64(
                    envelope,
                    payload,
                    "fileSizeBytes",
                    "file_size_bytes",
                    "sizeBytes",
                    "size_bytes",
                    "fileSize",
                    "file_size");
                var chunkSizeBytes = ConvertToNullableInt(ResolveInt64(
                    envelope,
                    payload,
                    "chunkSizeBytes",
                    "chunk_size_bytes",
                    "chunkSize",
                    "chunk_size"));
                var chunkCount = ConvertToNullableInt(ResolveInt64(
                    envelope,
                    payload,
                    "chunkCount",
                    "chunk_count",
                    "totalChunks",
                    "total_chunks"));
                var encryptionAlgorithm = ResolveString(
                    envelope,
                    payload,
                    "encryptionAlgorithm",
                    "encryption_algorithm",
                    "algorithm");

                transferEvent = new DirectCallFileOfferEvent(
                    eventType,
                    callId,
                    transferId,
                    senderPeerId,
                    targetPeerId,
                    sentAtUtc,
                    string.IsNullOrWhiteSpace(fileName) ? transferId : fileName!,
                    mimeType,
                    fileSizeBytes,
                    chunkSizeBytes,
                    chunkCount,
                    encryptionAlgorithm);
                return true;
            }
            case DirectCallFileTransferEventKind.Accept:
            {
                var accepted = ResolveBoolean(envelope, payload, "accepted", "isAccepted", "ok") ?? true;
                var reason = ResolveString(envelope, payload, "reason", "message", "error");
                transferEvent = new DirectCallFileAcceptEvent(
                    eventType,
                    callId,
                    transferId,
                    senderPeerId,
                    targetPeerId,
                    sentAtUtc,
                    accepted,
                    reason);
                return true;
            }
            case DirectCallFileTransferEventKind.Chunk:
            {
                var chunkIndexRaw = ResolveInt64(envelope, payload, "chunkIndex", "chunk_index", "index");
                if (!chunkIndexRaw.HasValue || chunkIndexRaw.Value < 0 || chunkIndexRaw.Value > int.MaxValue)
                    return false;

                var chunkCount = ConvertToNullableInt(ResolveInt64(
                    envelope,
                    payload,
                    "chunkCount",
                    "chunk_count",
                    "totalChunks",
                    "total_chunks"));
                var encryptedPayloadJson = ResolveRawJson(
                    envelope,
                    payload,
                    "encryptedPayload",
                    "encrypted_payload",
                    "chunkPayload",
                    "chunk_payload",
                    "payload");
                if (string.IsNullOrWhiteSpace(encryptedPayloadJson))
                {
                    var cipherTextBase64 = ResolveString(
                        envelope,
                        payload,
                        "cipherTextBase64",
                        "cipher_text_base64",
                        "chunkBase64",
                        "chunk_base64");
                    var ivBase64 = ResolveString(
                        envelope,
                        payload,
                        "ivBase64",
                        "iv_base64",
                        "nonceBase64",
                        "nonce_base64");
                    if (!string.IsNullOrWhiteSpace(cipherTextBase64) &&
                        !string.IsNullOrWhiteSpace(ivBase64))
                    {
                        var tagBase64 = ResolveString(
                            envelope,
                            payload,
                            "tagBase64",
                            "tag_base64",
                            "tag");
                        var algorithm = ResolveString(
                            envelope,
                            payload,
                            "algorithm",
                            "encryptionAlgorithm",
                            "encryption_algorithm")
                            ?? "AES-256-GCM";
                        encryptedPayloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
                        {
                            ["Algorithm"] = algorithm,
                            ["CipherTextBase64"] = cipherTextBase64,
                            ["IvBase64"] = ivBase64,
                            ["TagBase64"] = tagBase64
                        });
                    }
                }
                var chunkSha256 = ResolveString(
                    envelope,
                    payload,
                    "chunkSha256",
                    "chunk_sha256",
                    "sha256",
                    "checksum");
                var isLastChunk = ResolveBoolean(
                    envelope,
                    payload,
                    "isLastChunk",
                    "is_last_chunk",
                    "finalChunk",
                    "final_chunk");

                transferEvent = new DirectCallFileChunkEvent(
                    eventType,
                    callId,
                    transferId,
                    senderPeerId,
                    targetPeerId,
                    sentAtUtc,
                    (int)chunkIndexRaw.Value,
                    chunkCount,
                    encryptedPayloadJson,
                    chunkSha256,
                    isLastChunk);
                return true;
            }
            case DirectCallFileTransferEventKind.Complete:
            {
                var fileSizeBytes = ResolveInt64(
                    envelope,
                    payload,
                    "fileSizeBytes",
                    "file_size_bytes",
                    "sizeBytes",
                    "size_bytes",
                    "totalBytes",
                    "total_bytes");
                var chunkCount = ConvertToNullableInt(ResolveInt64(
                    envelope,
                    payload,
                    "chunkCount",
                    "chunk_count",
                    "totalChunks",
                    "total_chunks"));
                var fileSha256 = ResolveString(
                    envelope,
                    payload,
                    "fileSha256",
                    "file_sha256",
                    "sha256",
                    "checksum");

                transferEvent = new DirectCallFileCompleteEvent(
                    eventType,
                    callId,
                    transferId,
                    senderPeerId,
                    targetPeerId,
                    sentAtUtc,
                    fileSizeBytes,
                    chunkCount,
                    fileSha256);
                return true;
            }
            case DirectCallFileTransferEventKind.Cancel:
            {
                var reason = ResolveString(envelope, payload, "reason", "message", "error");
                transferEvent = new DirectCallFileCancelEvent(
                    eventType,
                    callId,
                    transferId,
                    senderPeerId,
                    targetPeerId,
                    sentAtUtc,
                    reason);
                return true;
            }
            default:
                return false;
        }
    }

    private static DirectCallFileTransferEventKind ResolveKind(string? type)
    {
        if (string.Equals(type, ProtocolMessageTypes.DirectCallFileOffer, StringComparison.Ordinal) ||
            string.Equals(type, DirectCallProtocol.Actions.FileOffer, StringComparison.Ordinal))
        {
            return DirectCallFileTransferEventKind.Offer;
        }

        if (string.Equals(type, ProtocolMessageTypes.DirectCallFileAccept, StringComparison.Ordinal) ||
            string.Equals(type, DirectCallProtocol.Actions.FileAccept, StringComparison.Ordinal))
        {
            return DirectCallFileTransferEventKind.Accept;
        }

        if (string.Equals(type, ProtocolMessageTypes.DirectCallFileChunk, StringComparison.Ordinal) ||
            string.Equals(type, DirectCallProtocol.Actions.FileChunk, StringComparison.Ordinal))
        {
            return DirectCallFileTransferEventKind.Chunk;
        }

        if (string.Equals(type, ProtocolMessageTypes.DirectCallFileComplete, StringComparison.Ordinal) ||
            string.Equals(type, DirectCallProtocol.Actions.FileComplete, StringComparison.Ordinal))
        {
            return DirectCallFileTransferEventKind.Complete;
        }

        if (string.Equals(type, ProtocolMessageTypes.DirectCallFileCancel, StringComparison.Ordinal) ||
            string.Equals(type, DirectCallProtocol.Actions.FileCancel, StringComparison.Ordinal))
        {
            return DirectCallFileTransferEventKind.Cancel;
        }

        return DirectCallFileTransferEventKind.Unknown;
    }

    private static JsonElement ResolvePayloadObject(FeatureResponseEnvelope envelope)
    {
        if (envelope.TryGetPayload(out var payload) && payload.ValueKind == JsonValueKind.Object)
            return payload;

        return default;
    }

    private static string? ResolveString(FeatureResponseEnvelope envelope, JsonElement payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            var envelopeValue = envelope.GetString(key);
            if (!string.IsNullOrWhiteSpace(envelopeValue))
                return envelopeValue;
        }

        if (payload.ValueKind == JsonValueKind.Object)
        {
            var payloadValue = payload.GetString(keys);
            if (!string.IsNullOrWhiteSpace(payloadValue))
                return payloadValue;
        }

        return null;
    }

    private static long? ResolveInt64(FeatureResponseEnvelope envelope, JsonElement payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            var envelopeValue = envelope.GetInt64(key);
            if (envelopeValue.HasValue)
                return envelopeValue.Value;
        }

        if (payload.ValueKind == JsonValueKind.Object)
            return payload.GetInt64(keys);

        return null;
    }

    private static bool? ResolveBoolean(FeatureResponseEnvelope envelope, JsonElement payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            var envelopeValue = envelope.GetBoolean(key);
            if (envelopeValue.HasValue)
                return envelopeValue.Value;
        }

        if (payload.ValueKind == JsonValueKind.Object)
            return payload.GetBoolean(keys);

        return null;
    }

    private static string? ResolveRawJson(FeatureResponseEnvelope envelope, JsonElement payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (envelope.TryGetElement(key, out var envelopeElement) &&
                envelopeElement.ValueKind != JsonValueKind.Undefined &&
                envelopeElement.ValueKind != JsonValueKind.Null)
            {
                if (envelopeElement.ValueKind == JsonValueKind.String)
                    return envelopeElement.GetString();
                return envelopeElement.GetRawText();
            }
        }

        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetAnyProperty(out var payloadElement, keys) &&
            payloadElement.ValueKind != JsonValueKind.Undefined &&
            payloadElement.ValueKind != JsonValueKind.Null)
        {
            if (payloadElement.ValueKind == JsonValueKind.String)
                return payloadElement.GetString();
            return payloadElement.GetRawText();
        }

        return null;
    }

    private static DateTimeOffset? ResolveTimestamp(FeatureResponseEnvelope envelope, JsonElement payload)
    {
        var unixMs = ResolveInt64(envelope, payload, "sentAtUnixMs", "sent_at_unix_ms", "timestamp");
        if (unixMs.HasValue)
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs.Value);

        var timestampRaw = ResolveString(
            envelope,
            payload,
            "createdAt",
            "created_at",
            "sentAt",
            "sent_at",
            "timestamp");
        if (string.IsNullOrWhiteSpace(timestampRaw))
            return null;

        if (DateTimeOffset.TryParse(timestampRaw, out var parsed))
            return parsed;

        if (long.TryParse(timestampRaw, out var parsedUnix))
            return DateTimeOffset.FromUnixTimeMilliseconds(parsedUnix);

        return null;
    }

    private static int? ConvertToNullableInt(long? value)
    {
        if (!value.HasValue || value.Value < int.MinValue || value.Value > int.MaxValue)
            return null;

        return (int)value.Value;
    }
}
