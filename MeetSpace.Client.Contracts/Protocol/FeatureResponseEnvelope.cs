using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeetSpace.Client.Contracts.Protocol;

public sealed class FeatureResponseEnvelope
{
    public string Type { get; init; } = string.Empty;
    public string? Object { get; init; }
    public string? Agent { get; init; }
    public string? Action { get; init; }
    public string? Peer { get; init; }
    public bool? Ok { get; init; }
    public string? Message { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}