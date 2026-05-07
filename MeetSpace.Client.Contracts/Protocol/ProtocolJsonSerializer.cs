using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetSpace.Client.Contracts.Protocol;

public sealed class ProtocolJsonSerializer
{
    private static readonly JsonSerializerOptions RequestOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public string SerializeRequest(FeatureRequestEnvelope envelope)
    {
        if (envelope == null)
            throw new ArgumentNullException(nameof(envelope));

        return JsonSerializer.Serialize(envelope, RequestOptions);
    }

    public FeatureResponseEnvelope? DeserializeResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var extensions = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in root.EnumerateObject())
                extensions[property.Name] = property.Value.Clone();

            var requestNode = TryGetObjectProperty(root, "request");
            var resultNode = TryGetObjectProperty(root, "result");
            var errorNode = TryGetObjectProperty(root, "error");

            var type = GetString(root, "type", "messageType", "message_type", "event");
            var objectName =
                GetString(root, "object", "obj", "feature") ??
                (requestNode.HasValue ? GetString(requestNode.Value, "object", "obj", "feature") : null);
            var agent =
                GetString(root, "agent", "module") ??
                (requestNode.HasValue ? GetString(requestNode.Value, "agent", "module") : null);
            var action =
                GetString(root, "action", "operation", "op") ??
                (requestNode.HasValue ? GetString(requestNode.Value, "action", "operation", "op") : null);
            var peer = GetString(root, "peer", "peerId", "peer_id");
            var ok = GetBoolean(root, "ok", "success", "accepted");
            var message =
                GetString(root, "message", "reason", "error", "statusText", "status_text") ??
                TryExtractNestedMessage(root);

            if (resultNode.HasValue)
            {
                ok ??= GetBoolean(resultNode.Value, "ok", "success", "accepted");
                message ??= GetString(resultNode.Value, "message", "reason", "error", "statusText", "status_text");

                if (!extensions.ContainsKey("data") &&
                    TryGetProperty(resultNode.Value, "data", out var resultData))
                {
                    extensions["data"] = resultData.Clone();
                }
            }

            if (errorNode.HasValue)
            {
                message ??= GetString(errorNode.Value, "message", "reason", "error", "statusText", "status_text");
                if (!extensions.ContainsKey("error"))
                    extensions["error"] = errorNode.Value.Clone();
                if (!ok.HasValue)
                    ok = false;
            }

            if (string.IsNullOrWhiteSpace(type))
            {
                var kind = GetString(root, "kind");
                if (string.Equals(kind, "response", StringComparison.OrdinalIgnoreCase) &&
                    (ok.HasValue || HasAny(root, "requestId", "clientRequestId", "correlationId")))
                {
                    type = ProtocolMessageTypes.DispatchResult;
                }
                else if (!string.IsNullOrWhiteSpace(kind) &&
                         !string.Equals(kind, "response", StringComparison.OrdinalIgnoreCase))
                {
                    type = kind;
                }
            }

            return new FeatureResponseEnvelope
            {
                Type = type ?? string.Empty,
                Object = objectName,
                Agent = agent,
                Action = action,
                Peer = peer,
                Ok = ok,
                Message = message,
                Extensions = extensions
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool HasAny(JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static JsonElement? TryGetObjectProperty(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (property.Value.ValueKind != JsonValueKind.Object)
                return null;

            return property.Value;
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            value = property.Value;
            return true;
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                return property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => bool.TrueString,
                    JsonValueKind.False => bool.FalseString,
                    _ => null
                };
            }
        }

        return null;
    }

    private static bool? GetBoolean(JsonElement element, params string[] names)
    {
        foreach (var property in element.EnumerateObject())
        {
            foreach (var name in names)
            {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                return property.Value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String when bool.TryParse(property.Value.GetString(), out var parsed) => parsed,
                    _ => null
                };
            }
        }

        return null;
    }

    private static string? TryExtractNestedMessage(JsonElement root)
    {
        foreach (var containerName in new[] { "payload", "data" })
        {
            foreach (var property in root.EnumerateObject())
            {
                if (!string.Equals(property.Name, containerName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (property.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var nested = property.Value;
                return GetString(nested, "message", "reason", "error", "statusText", "status_text");
            }
        }

        return null;
    }
}