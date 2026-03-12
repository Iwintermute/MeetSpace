using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    public Dictionary<string, object?> Payload { get; init; } = new();
}