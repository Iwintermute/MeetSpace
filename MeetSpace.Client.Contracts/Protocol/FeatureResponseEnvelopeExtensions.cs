using System;
using System.Text.Json;

namespace MeetSpace.Client.Contracts.Protocol;

public static class FeatureResponseEnvelopeExtensions
{
    public static string? GetString(this FeatureResponseEnvelope envelope, string key)
    {
        if (envelope.Extensions is null || !envelope.Extensions.TryGetValue(key, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    public static long? GetInt64(this FeatureResponseEnvelope envelope, string key)
    {
        if (envelope.Extensions is null || !envelope.Extensions.TryGetValue(key, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed))
            return parsed;

        return null;
    }

    public static bool? GetBoolean(this FeatureResponseEnvelope envelope, string key)
    {
        if (envelope.Extensions is null || !envelope.Extensions.TryGetValue(key, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    public static bool TryGetElement(this FeatureResponseEnvelope envelope, string key, out JsonElement element)
    {
        if (envelope.Extensions is not null && envelope.Extensions.TryGetValue(key, out var raw))
        {
            element = raw.Clone();
            return true;
        }

        element = default;
        return false;
    }

    public static bool TryGetPayload(this FeatureResponseEnvelope envelope, out JsonElement payload)
    {
        if (TryGetElement(envelope, "payload", out payload))
            return true;

        if (TryGetElement(envelope, "data", out payload))
            return true;

        if (!string.IsNullOrWhiteSpace(envelope.Message))
        {
            var message = envelope.Message!.Trim();
            if (message.StartsWith("{", StringComparison.Ordinal) ||
                message.StartsWith("[", StringComparison.Ordinal))
            {
                try
                {
                    using var doc = JsonDocument.Parse(message);
                    payload = doc.RootElement.Clone();
                    return true;
                }
                catch
                {
                }
            }
        }

        payload = default;
        return false;
    }

    public static string? GetRequestId(this FeatureResponseEnvelope envelope)
    {
        return envelope.GetString("requestId")
            ?? envelope.GetString("clientRequestId")
            ?? envelope.GetString("correlationId");
    }
}